using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;

namespace ProtoBaseNet
{
    // Represents a logical pointer to an atom within the storage (transaction id + offset).
    // Equality is structural; two pointers are equal if both parts match.
    public class AtomPointer
    {
        public Guid TransactionId { get; set; }
        public int Offset { get; set; }

        // If a transaction id is not provided, a new Guid is generated.
        public AtomPointer(Guid? transactionId = null, int offset = 0)
        {
            TransactionId = transactionId ?? Guid.NewGuid();
            Offset = offset;
        }

        public override bool Equals(object? obj)
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

    // Base type for all persisted objects (“atoms”). It encapsulates:
    // - A storage pointer (AtomPointer)
    // - An owning transaction (Transaction)
    // - Load/Save lifecycle with JSON-based serialization
    // - Extension points for dynamic attributes and post-load hooks
    public abstract class Atom
    {
        // Pointer to the atom data within the storage. May be null before the first save.
        public AtomPointer? AtomPointer { get; set; }

        // Owning transaction used for I/O, object materialization, and nested saves.
        public ObjectTransaction? Transaction { get; set; }

        // State flags to avoid redundant I/O and re-entrancy loops.
        protected bool _loaded = false;
        protected bool _saved = false;

        protected Atom(ObjectTransaction? transaction = null, AtomPointer? atomPointer = null)
        {
            Transaction = transaction;
            AtomPointer = atomPointer;
        }
        
        // Allows derived types to support ad-hoc/dynamic attributes during load.
        // Default behavior is to signal missing members.
        protected virtual void SetDynamicAttribute(string attributeName, object value) => throw new MissingFieldException();

        // Allows derived types to expose dynamic attributes to be included in Save().
        protected virtual Dictionary<string, object> GetDynamicAttributes() => new Dictionary<string, object>();
        
        // Lazily loads the atom content from storage using the current Transaction and AtomPointer.
        // Deserialization is JSON-based and supports embedded Atoms, literals, and selected primitive types.
        protected virtual void Load()
        {
            if (_loaded) return;

            if (Transaction != null && AtomPointer != null)
            {
                // Synchronously retrieve raw bytes (GetAwaiter().GetResult) to keep the current sync API contract.
                var bytes = Transaction.Storage.GetBytes(AtomPointer).GetAwaiter().GetResult();
                if (bytes is { Length: > 0 })
                {
                    var jsonString = System.Text.Encoding.UTF8.GetString(bytes);
                    var loadedAtomDict = JsonSerializer.Deserialize<Dictionary<string, object>>(jsonString);
                    
                    if (loadedAtomDict != null)
                    {
                        // Normalize/convert raw JSON into runtime objects (including nested Atoms).
                        var loadedDict = JsonToDict(loadedAtomDict);

                        foreach (var kv in loadedDict)
                        {
                            var attributeName = kv.Key;
                            var attributeValue = kv.Value;

                            // Bind by field name (case-insensitive) on the runtime type.
                            var field = GetType().GetField(attributeName, BindingFlags.IgnoreCase | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                            if (field != null)
                            {
                                try
                                {
                                    // Attempt direct assignment first.
                                    field.SetValue(this, attributeValue);
                                }
                                catch (ArgumentException)
                                {
                                    // If types mismatch (e.g., JsonElement), attempt conversion to the target field type.
                                    object? convertedValue = TryConvert(attributeValue, field.FieldType);
                                    field.SetValue(this, convertedValue);
                                }
                                catch (MissingFieldException)
                                {
                                    // Defer to dynamic attribute handler if the field cannot be set.
                                    SetDynamicAttribute(attributeName, attributeValue);
                                }
                            }
                            else
                            {
                                // No matching field: treat as a dynamic attribute.
                                SetDynamicAttribute(attributeName, attributeValue);
                            }

                            // Propagate the current transaction to nested Atoms.
                            if (attributeValue is Atom atomValue)
                            {
                                atomValue.Transaction = Transaction;
                            }
                        }
                    }
                }
            }

            _loaded = true;
            AfterLoad();
        }

        // Hook for subclasses to run domain-specific logic after a successful Load().
        public virtual void AfterLoad()
        {
        }

        // Equality: if both sides have pointers, equality is pointer-based; otherwise, reference equality.
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

        // Converts a JSON object dictionary into runtime objects, including:
        // - DateTime/Date (roundtrip)
        // - TimeSpan (microseconds)
        // - Atoms by pointer (transaction_id + offset)
        // - Literal atoms by value or pointer
        protected virtual Dictionary<string, object> JsonToDict(Dictionary<string, object> jsonData)
        {
            var data = new Dictionary<string, object>();

            foreach (var entry in jsonData)
            {
                var name = entry.Key;
                var value = entry.Value;

                if (value is JsonElement { ValueKind: JsonValueKind.Object } jsonElement)
                {
                    var valueDict = JsonSerializer.Deserialize<Dictionary<string, object>>(jsonElement.GetRawText());
                    if (valueDict != null && valueDict.TryGetValue("className", out var classNameObj) && classNameObj is string className)
                    {
                        object? convertedValue = null;
                        switch (className)
                        {
                            case "datetime.datetime":
                                if (valueDict.TryGetValue("iso", out var isoDt))
                                    convertedValue = DateTime.Parse(isoDt?.ToString() ?? string.Empty, null, System.Globalization.DateTimeStyles.RoundtripKind);
                                break;
                            case "datetime.date":
                                if (valueDict.TryGetValue("iso", out var isoDate))
                                    convertedValue = DateTime.Parse(isoDate?.ToString() ?? string.Empty, null, System.Globalization.DateTimeStyles.RoundtripKind).Date;
                                break;
                            case "datetime.timedelta":
                                if (valueDict.TryGetValue("microseconds", out var microsObj) && long.TryParse(microsObj?.ToString(), out var micros))
                                    convertedValue = new TimeSpan(micros * 10);
                                break;
                            case "Literal":
                                // Literal atoms may be referenced by pointer or inline string.
                                if (valueDict.ContainsKey("transaction_id"))
                                {
                                    var ap = BuildPointer(valueDict);
                                    var literal = Transaction?.ReadObject(className, ap) as DbLiteral;
                                    literal?.Load();
                                    convertedValue = literal;
                                }
                                else
                                {
                                    if (valueDict.TryGetValue("string", out var strObj))
                                        convertedValue = Transaction?.GetLiteral(strObj?.ToString() ?? string.Empty);
                                }
                                break;
                            default:
                                // Generic Atom by pointer is required.
                                if (valueDict.ContainsKey("transaction_id"))
                                {
                                    var ap = BuildPointer(valueDict);
                                    convertedValue = Transaction?.ReadObject(className, ap);
                                }
                                else
                                {
                                    throw new ProtoValidationException($"Cannot load Atom of class {className} without a pointer.");
                                }
                                break;
                        }
                        if (convertedValue is not null)
                            data[name] = convertedValue;
                    }
                    else
                    {
                        // Non-typed JSON objects are preserved as dictionaries.
                        if (valueDict is not null)
                            data[name] = valueDict;
                    }
                }
                else if (value is JsonElement primitiveJe)
                {
                    // Normalize primitive JsonElement values to .NET primitives when possible.
                    data[name] = ConvertFromJsonElement(primitiveJe);
                }
                else
                {
                    // Raw primitive or already-converted value.
                    data[name] = value!;
                }
            }
            return data;
        }

        // Converts runtime data to a JSON-friendly dictionary. It:
        // - Ensures nested Atoms are saved and referenced by pointer
        // - Serializes DateTime/TimeSpan to structured forms
        // - Encodes byte[] as Base64
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
                        // Stored as Base64. A specialized BytesAtom can be introduced in the future.
                        jsonValue[name] = Convert.ToBase64String(bytes);
                        break;
                    case not null:
                        jsonValue[name] = value;
                        break;
                }
            }
            return jsonValue;
        }

        // Persists the current Atom if not already saved. Invokes Load() to ensure full state,
        // then serializes fields plus dynamic attributes and pushes the payload to storage.
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
                // Special case: literals are persisted by value.
                jsonValue["string"] = literal.Value;
            }
            else
            {
                // Serialize non-private, non-readonly instance fields except internal runtime fields.
                var fields = GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                foreach (var field in fields)
                {
                    if (field.IsLiteral || field.IsInitOnly || field.Name.StartsWith("_") || field.Name.Contains("k__BackingField") || field.Name is "AtomPointer" or "Transaction") continue;

                    var value = field.GetValue(this);
                    if (value == null) continue;

                    if (value is string str)
                    {
                        // Strings are saved via literal atoms to ensure deduplication and stable referencing.
                        var literalAtom = Transaction.GetLiteral(str);
                        if (literalAtom.AtomPointer == null)
                        {
                            // Ensure literals created during this tx are persisted.
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
                
                // Append dynamic attributes provided by subclasses.
                foreach (var dynamicAttribute in GetDynamicAttributes())
                {
                    jsonValue[dynamicAttribute.Key] = dynamicAttribute.Value;
                }
            }

            // Final JSON normalization then persist to storage.
            var finalJson = DictToJson(jsonValue);
            var jsonString = JsonSerializer.Serialize(finalJson);
            var bytes = System.Text.Encoding.UTF8.GetBytes(jsonString);
            AtomPointer = Transaction.Storage.PushBytes(bytes).GetAwaiter().GetResult();

            // Reset the flag so subsequent modifications within the same transaction can be re-saved.
            _saved = false;
        }

        public override int GetHashCode()
        {
            // Ensure persisted identity before computing a stable hash based on the pointer.
            Save();
            return AtomPointer?.GetHashCode() ?? 0;
        }

        // Attempts to convert a deserialized value into the specified target type.
        // Handles JsonElement primitives and falls back to ChangeType or identity.
        private static object? TryConvert(object? value, Type targetType)
        {
            if (value is null) return null;
            if (targetType.IsInstanceOfType(value)) return value;

            if (value is JsonElement je) return ConvertFromJsonElement(je, targetType);

            try
            {
                return Convert.ChangeType(value, targetType);
            }
            catch
            {
                return value;
            }
        }

        // Converts a JsonElement into a reasonable .NET type, optionally guided by the target type.
        private static object? ConvertFromJsonElement(JsonElement je, Type? targetType = null)
        {
            try
            {
                return je.ValueKind switch
                {
                    JsonValueKind.String => targetType == typeof(DateTime)
                        ? DateTime.Parse(je.GetString() ?? string.Empty, null, System.Globalization.DateTimeStyles.RoundtripKind)
                        : je.GetString(),
                    JsonValueKind.Number => targetType != null
                        ? Convert.ChangeType(je.GetDouble(), targetType)
                        : je.TryGetInt64(out var l) ? l : je.GetDouble(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.Null => null,
                    JsonValueKind.Object or JsonValueKind.Array => JsonSerializer.Deserialize<object>(je.GetRawText()),
                    _ => je.ToString()
                };
            }
            catch
            {
                // As a last resort, return the raw JSON text.
                return je.ToString();
            }
        }

        // Builds an AtomPointer from a JSON dictionary containing "transaction_id" and "offset".
        private static AtomPointer BuildPointer(Dictionary<string, object> dict)
        {
            var txStr = dict.TryGetValue("transaction_id", out var t) ? t?.ToString() : null;
            var offStr = dict.TryGetValue("offset", out var o) ? o?.ToString() : null;
            if (!Guid.TryParse(txStr, out var gid)) throw new ProtoValidationException("Invalid transaction_id in atom pointer.");
            if (!int.TryParse(offStr, out var off)) throw new ProtoValidationException("Invalid offset in atom pointer.");
            return new AtomPointer(gid, off);
        }
    }
}
