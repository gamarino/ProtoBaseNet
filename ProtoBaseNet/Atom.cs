using System;
using System.Collections.Generic;
using System.Reflection;

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
    /// - A load/save lifecycle with dictionary-based serialization
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
        
        public virtual void Load(ObjectTransaction? transaction)
        {
            if (_loaded) return;
            
            if (transaction is null)
                Transaction = transaction;

            if (Transaction != null && AtomPointer != null)
            {
                var loadedDict = Transaction.Storage.ReadAtom(AtomPointer).GetAwaiter().GetResult();
                if (loadedDict != null)
                {
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

        private Dictionary<string, object> _toDictionary()
        {
            var data = new Dictionary<string, object>
            {
                { "className", GetType().Name }
            };

            if (this is DbLiteral literal)
            {
                data["string"] = literal.Value;
            }
            else
            {
                var fields = GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                foreach (var field in fields)
                {
                    if (field.IsLiteral || field.IsInitOnly || field.Name.StartsWith("_") || field.Name.Contains("k__BackingField") || field.Name is "AtomPointer" or "Transaction") continue;

                    var value = field.GetValue(this);
                    if (value == null) continue;

                    if (value is Atom valueAtom)
                        valueAtom.Save(Transaction);
                    
                    if (value is string str)
                    {
                        var literalAtom = Transaction.GetLiteral(str);
                        if (literalAtom.AtomPointer == null)
                        {
                            Transaction.UpdateCreatedLiterals(Transaction, Transaction.NewLiterals);
                        }
                        if (literalAtom.AtomPointer == null) throw new ProtoCorruptionException("Corruption saving string as literal!");

                        data[field.Name] = new Dictionary<string, object>
                        {
                            { "className", literalAtom.GetType().Name },
                            { "transaction_id", literalAtom.AtomPointer.TransactionId.ToString() },
                            { "offset", literalAtom.AtomPointer.Offset }
                        };
                    }
                    else
                    {
                        data[field.Name] = value;
                    }
                }
                
                foreach (var dynamicAttribute in GetDynamicAttributes())
                {
                    data[dynamicAttribute.Key] = dynamicAttribute.Value;
                }
            }
            return data;
        }

        public virtual void Save(ObjectTransaction? transaction = null)
        {
            if (transaction is null)
                transaction = Transaction;
            
            Load(transaction);
            if (AtomPointer != null) return;

            _saved = true;
            if (Transaction == null) throw new ProtoValidationException("An DBObject can only be saved within a given transaction!");

            var data = _toDictionary();
            AtomPointer = Transaction.Storage.WriteAtom(data).GetAwaiter().GetResult();

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

            try
            {
                return Convert.ChangeType(value, targetType);
            }
            catch
            {
                return value;
            }
        }
    }
}
