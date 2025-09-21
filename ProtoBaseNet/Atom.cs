using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace ProtoBaseNet
{
   
    public class AtomPointer
    {
        public Guid TransactionId { get; set; }
        public int Offset { get; set; }

        public AtomPointer(Guid? transactionId = null, int offset = 0)
        {
            TransactionId = transactionId ?? Guid.NewGuid();
            Offset = offset;
        }

        public override bool Equals(object obj)
        {
            return obj is AtomPointer pointer &&
                   TransactionId.Equals(pointer.TransactionId) &&
                   Offset == pointer.Offset;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(TransactionId, Offset);
        }
    }

    public abstract class Atom
    {
        public AtomPointer? AtomPointer { get; set; }
        public ObjectTransaction? Transaction { get; set; }
        protected bool _loaded = false;
        protected bool _saved = false;

        protected Atom(ObjectTransaction? transaction = null, AtomPointer? atomPointer = null)
        {
            Transaction = transaction;
            AtomPointer = atomPointer;
        }
        
        protected virtual void SetDynamicAttribute(string attributeName, object value) => throw new MissingFieldException();
        protected virtual Dictionary<string, object> GetDynamicAttributes() => new Dictionary<string, object>();
        
        protected virtual void Load()
        {
            if (_loaded) return;

            if (Transaction != null && AtomPointer != null)
            {
                var bytes = Transaction.Storage.GetBytes(AtomPointer).Result;
                var jsonString = System.Text.Encoding.UTF8.GetString(bytes);
                var loadedAtomDict = JsonSerializer.Deserialize<Dictionary<string, object>>(jsonString);
                
                if (loadedAtomDict != null)
                {
                    var loadedDict = JsonToDict(loadedAtomDict);

                    foreach (var (attributeName, attributeValue) in loadedDict)
                    {
                        var field = GetType().GetField(attributeName, BindingFlags.IgnoreCase | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (field != null)
                        {
                            try
                            {
                                field.SetValue(this, attributeValue);
                            }
                            catch (ArgumentException)
                            {
                                // Try to convert
                                if (attributeValue is JsonElement jsonElement)
                                {
                                    var convertedValue = Convert.ChangeType(jsonElement.ToString(), field.FieldType);
                                    field.SetValue(this, convertedValue);
                                }
                            }
                            catch (MissingFieldException)
                            {
                                this.SetDynamicAttribute(attributeName, attributeValue);
                            }
                        }

                        if (attributeValue is Atom atomValue)
                        {
                            atomValue.Transaction = Transaction;
                        }
                    }
                }
            }

            _loaded = true;
            AfterLoad();
        }

        public virtual void AfterLoad()
        {
        }

        public override bool Equals(object? obj)
        {
            if (obj is Atom other)
            {
                if (AtomPointer != null && other.AtomPointer != null)
                {
                    return AtomPointer.Equals(other.AtomPointer);
                }
                return ReferenceEquals(this, other);
            }
            return false;
        }

        protected virtual Dictionary<string, object> JsonToDict(Dictionary<string, object> jsonData)
        {
            var data = new Dictionary<string, object>();

            foreach (var (name, value) in jsonData)
            {
                if (value is JsonElement { ValueKind: JsonValueKind.Object } jsonElement)
                {
                    var valueDict = JsonSerializer.Deserialize<Dictionary<string, object>>(jsonElement.GetRawText());
                    if (valueDict != null && valueDict.TryGetValue("className", out var classNameObj) && classNameObj is string className)
                    {
                        object? convertedValue = null;
                        switch (className)
                        {
                            case "datetime.datetime":
                                convertedValue = DateTime.Parse(valueDict["iso"].ToString() ?? string.Empty);
                                break;
                            case "datetime.date":
                                convertedValue = DateTime.Parse(valueDict["iso"].ToString() ?? string.Empty);
                                break;
                            case "datetime.timedelta":
                                convertedValue = new TimeSpan(long.Parse(valueDict["microseconds"].ToString() ?? "0") * 10);
                                break;
                            case "Literal":
                                if (valueDict.ContainsKey("transaction_id"))
                                {
                                    var ap = new AtomPointer(Guid.Parse(valueDict["transaction_id"].ToString() ?? string.Empty), Convert.ToInt32(valueDict["offset"]));
                                    var literal = Transaction?.ReadObject(className, ap) as DbLiteral;
                                    literal?.Load();
                                    convertedValue = literal;
                                }
                                else
                                {
                                    convertedValue = Transaction?.GetLiteral(valueDict["string"].ToString() ?? string.Empty);
                                }
                                break;
                            default:
                                if (valueDict.ContainsKey("transaction_id"))
                                {
                                    var ap = new AtomPointer(Guid.Parse(valueDict["transaction_id"].ToString() ?? string.Empty), Convert.ToInt32(valueDict["offset"]));
                                    convertedValue = Transaction?.ReadObject(className, ap);
                                }
                                else
                                {
                                    throw new ProtoValidationException($"Cannot load Atom of class {className} without a pointer.");
                                }
                                break;
                        }
                        data[name] = convertedValue ?? data[name];
                    }
                    else
                    {
                        data[name] = valueDict ?? data[name];
                    }
                }
                else
                {
                    data[name] = value;
                }
            }
            return data;
        }

        protected virtual Dictionary<string, object> DictToJson(Dictionary<string, object> data)
        {
            var jsonValue = new Dictionary<string, object>();
            foreach (var (name, value) in data)
            {
                switch (value)
                {
                    case Atom atom:
                    {
                        if (atom.Transaction == null) atom.Transaction = Transaction;
                        atom.Save();
                        if (atom.AtomPointer == null) throw new ProtoCorruptionException($"Corruption saving nested Atom: attr={name}");
                        jsonValue[name] = new Dictionary<string, object>
                        {
                            { "className", value.GetType().Name },
                            { "transaction_id", atom.AtomPointer.TransactionId.ToString() },
                            { "offset", atom.AtomPointer.Offset }
                        };
                        break;
                    }
                    case DateTime dt:
                        jsonValue[name] = new Dictionary<string, object>
                        {
                            { "className", "datetime.datetime" },
                            { "iso", dt.ToString("o") }
                        };
                        break;
                    case TimeSpan ts:
                        jsonValue[name] = new Dictionary<string, object>
                        {
                            { "className", "datetime.timedelta" },
                            { "microseconds", ts.Ticks / 10 }
                        };
                        break;
                    case byte[] bytes:
                        // In python this saves a BytesAtom. I don't have that class.
                        // I will save it as base64 string for now.
                        jsonValue[name] = Convert.ToBase64String(bytes);
                        break;
                    case not null:
                        jsonValue[name] = value;
                        break;
                }
            }
            return jsonValue;
        }


        protected virtual void Save()
        {
            Load();
            if (AtomPointer != null) return;
            if (_saved) return;

            _saved = true;
            if (Transaction == null) throw new ProtoValidationException("An DBObject can only be saved within a given transaction!");

            var jsonValue = new Dictionary<string, object>
            {
                { "className", GetType().Name }
            };

            if (this is DbLiteral literal)
            {
                jsonValue["string"] = literal.Value;
            }
            else
            {
                var fields = GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                foreach (var field in fields)
                {
                    if (field.IsLiteral || field.IsInitOnly || field.Name.StartsWith("_") || field.Name.Contains("k__BackingField") || field.Name is "AtomPointer" or "Transaction") continue;

                    var value = field.GetValue(this);
                    if (value == null) continue;

                    if (value is string str)
                    {
                        var literalAtom = Transaction.GetLiteral(str);
                        if (literalAtom.AtomPointer == null)
                        {
                            Transaction.UpdateCreatedLiterals(Transaction, Transaction.NewLiterals);
                        }
                        if (literalAtom.AtomPointer == null) throw new ProtoCorruptionException("Corruption saving string as literal!");

                        jsonValue[field.Name] = new Dictionary<string, object>
                        {
                            { "className", literalAtom.GetType().Name },
                            { "transaction_id", literalAtom.AtomPointer.TransactionId.ToString() },
                            { "offset", literalAtom.AtomPointer.Offset }
                        };
                    }
                    else
                    {
                        jsonValue[field.Name] = value;
                    }
                }
                
                // Load dynamic attributes into json value
                foreach (var dynamicAttribute in GetDynamicAttributes())
                {
                    jsonValue[dynamicAttribute.Key] = dynamicAttribute.Value;
                }
            }

            var finalJson = DictToJson(jsonValue);
            var jsonString = JsonSerializer.Serialize(finalJson);
            var bytes = System.Text.Encoding.UTF8.GetBytes(jsonString);
            AtomPointer = Transaction.Storage.PushBytes(bytes).Result;

            _saved = false; // Reset for potential future saves in same transaction if object is modified again.
        }

        public override int GetHashCode()
        {
            Save();
            return AtomPointer?.GetHashCode() ?? 0;
        }
    }
}
