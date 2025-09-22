using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;

namespace ProtoBaseNet
{
    /// <summary>
    /// Represents a logical pointer to an atom within the storage, defined by a transaction ID and an offset.
    /// </summary>
    /// <remarks>
    /// Equality is structural; two pointers are equal if both their TransactionId and Offset match.
    /// </remarks>
    public class AtomPointer
    {
        /// <summary>
        /// Gets or sets the transaction identifier.
        /// </summary>
        public Guid TransactionId { get; set; }

        /// <summary>
        /// Gets or sets the offset within the transaction.
        /// </summary>
        public int Offset { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="AtomPointer"/> class.
        /// </summary>
        /// <param name="transactionId">The transaction ID. If null, a new GUID is generated.</param>
        /// <param name="offset">The offset.</param>
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

    /// <summary>
    /// The base type for all persisted objects (“atoms”) in ProtoBaseNet.
    /// </summary>
    /// <remarks>
    /// It encapsulates:
    /// - A storage pointer (<see cref="AtomPointer"/>)
    /// - An owning transaction (<see cref="ObjectTransaction"/>)
    /// - A load/save lifecycle with JSON-based serialization
    /// - Extension points for dynamic attributes and post-load hooks
    /// </remarks>
    public abstract class Atom
    {
        private static readonly Dictionary<AtomPointer, WeakReference<Atom>> _cache = new();
        private static int _cacheAccessCount = 0;
        private const int CacheCleanThreshold = 1000;

        /// <summary>
        /// Gets or sets the pointer to the atom's data in storage. May be null before the first save.
        /// </summary>
        public AtomPointer? AtomPointer { get; set; }

        /// <summary>
        /// Gets or sets the owning transaction used for I/O and object materialization.
        /// </summary>
        public ObjectTransaction? Transaction { get; set; }

        protected bool _loaded = false;
        protected bool _saved = false;

        protected Atom(ObjectTransaction? transaction = null, AtomPointer? atomPointer = null)
        {
            Transaction = transaction;
            AtomPointer = atomPointer;
        }

        private static void CleanCache()
        {
            var deadKeys = new List<AtomPointer>();
            foreach (var (key, weakRef) in _cache)
            {
                if (!weakRef.TryGetTarget(out _))
                {
                    deadKeys.Add(key);
                }
            }
            foreach (var key in deadKeys)
            {
                _cache.Remove(key);
            }
        }

        internal static Atom? GetFromCache(AtomPointer pointer)
        {
            _cacheAccessCount++;
            if (_cacheAccessCount > CacheCleanThreshold)
            {
                CleanCache();
                _cacheAccessCount = 0;
            }

            if (_cache.TryGetValue(pointer, out var weakRef) && weakRef.TryGetTarget(out var atom))
            {
                return atom;
            }
            return null;
        }

        internal static void AddToCache(Atom atom)
        {
            if (atom.AtomPointer != null)
            {
                _cache[atom.AtomPointer] = new WeakReference<Atom>(atom);
            }
        }
        
        protected virtual void SetDynamicAttribute(string attributeName, object value) => throw new MissingFieldException();

        protected virtual Dictionary<string, object> GetDynamicAttributes() => new Dictionary<string, object>();
        
        internal virtual void Load()
        {
            if (_loaded) return;

            if (Transaction != null && AtomPointer != null)
            {
                var bytes = Transaction.Storage.GetBytes(AtomPointer).GetAwaiter().GetResult();
                if (bytes is { Length: > 0 })
                {
                    var jsonString = System.Text.Encoding.UTF8.GetString(bytes);
                    var loadedAtomDict = JsonSerializer.Deserialize<Dictionary<string, object>>(jsonString);
                    
                    if (loadedAtomDict != null)
                    {
                        var loadedDict = JsonToDict(loadedAtomDict);

                        foreach (var kv in loadedDict)
                        {
                            var attributeName = kv.Key;
                            var attributeValue = kv.Value;

                            var field = GetType().GetField(attributeName, BindingFlags.IgnoreCase | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                            if (field != null)
                            {
                                try
                                {
                                    field.SetValue(this, attributeValue);
                                }
                                catch (ArgumentException)
                                {
                                    object? convertedValue = TryConvert(attributeValue, field.FieldType);
                                    field.SetValue(this, convertedValue);
                                }
                                catch (MissingFieldException)
                                {
                                    SetDynamicAttribute(attributeName, attributeValue);
                                }
                            }
                            else
                            {
                                SetDynamicAttribute(attributeName, attributeValue);
                            }

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

        /// <summary>
        /// A hook for subclasses to run domain-specific logic after a successful Load operation.
        /// </summary>
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
                        if (valueDict is not null)
                            data[name] = valueDict;
                    }
                }
                else if (value is JsonElement primitiveJe)
                {
                    data[name] = ConvertFromJsonElement(primitiveJe);
                }
                else
                {
                    data[name] = value!;
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
                        jsonValue[name] = Convert.ToBase64String(bytes);
                        break;
                    case not null:
                        jsonValue[name] = value;
                        break;
                }
            }
            return jsonValue;
        }

        internal virtual void Save()
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
                
                foreach (var dynamicAttribute in GetDynamicAttributes())
                {
                    jsonValue[dynamicAttribute.Key] = dynamicAttribute.Value;
                }
            }

            var finalJson = DictToJson(jsonValue);
            var jsonString = JsonSerializer.Serialize(finalJson);
            var bytes = System.Text.Encoding.UTF8.GetBytes(jsonString);
            AtomPointer = Transaction.Storage.PushBytes(bytes).GetAwaiter().GetResult();

            _saved = false;
        }

        public override int GetHashCode()
        {
            Save();
            return AtomPointer?.GetHashCode() ?? 0;
        }

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
                return je.ToString();
            }
        }

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
