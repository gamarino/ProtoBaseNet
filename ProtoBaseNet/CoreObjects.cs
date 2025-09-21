using System;

namespace ProtoBaseNet
{
    // Represents the root entry for the object graph stored in the space.
    // It holds pointers to the database object root and the literals root,
    // along with a creation timestamp for historical queries/auditing.
    public class RootObject : Atom
    {
        // Root where application objects/databases are anchored.
        public Atom? ObjectRoot { get; set; }

        // Root where shared literals (e.g., deduplicated strings) are anchored.
        public Atom? LiteralRoot { get; set; }

        // Timestamp when this root instance was created.
        public DateTime CreatedAt { get; set; }

        public RootObject(Atom? objectRoot = null, Atom? literalRoot = null, ObjectTransaction? transaction = null,
            AtomPointer? atomPointer = null)
            : base(transaction, atomPointer)
        {
            ObjectRoot = objectRoot;
            LiteralRoot = literalRoot;
            CreatedAt = DateTime.Now;
        }
    }

    // Base object with attribute storage based on a key/value dictionary.
    // The default behavior is “functional”: SetAt returns a new DbObject instance
    // with an updated attributes map, leaving the original untouched.
    public class DbObject : Atom
    {
        // Backing attribute map.
        protected DbDictionary<object> Attributes = new();

        public DbObject(DbDictionary<object> attributes, ObjectTransaction? transaction = null, AtomPointer? atomPointer = null, params object[] kwargs)
            : base(transaction, atomPointer)
        {
            // Ensure a non-null attributes container.
            Attributes = attributes ?? new DbDictionary<object>();
        }

        public DbObject(ObjectTransaction? transaction = null, AtomPointer? atomPointer = null, params object[] kwargs)
            : base(transaction, atomPointer)
        {
            Attributes = new DbDictionary<object>();
        }

        // Retrieves an attribute value by name. Returns null if not present.
        public virtual object? GetAt(string name)
        {
            return Attributes.GetAt(name);
        }

        // Returns a new DbObject with the attribute set to the provided value.
        // This preserves immutability of the original instance by design.
        public virtual DbObject SetAt(string name, object value)
        {
            return new DbObject(
                Attributes.SetAt(name, value),
                Transaction,
                AtomPointer);
        }

        // Checks whether an attribute key is present.
        public virtual bool HasDefinedAttr(string name)
        {
            return Attributes.Has(name);
        }
    }

    // A wrapper that provides mutability semantics over a DbObject via an external
    // mutable dictionary indexed by a stable HashKey. It redirects reads/writes
    // to a per-transaction mutable store.
    public class MutableObject : DbObject
    {
        // Logical key that identifies the mutable entry in the transaction-scoped store.
        public int HashKey { get; }

        public MutableObject(int hashKey = 0, ObjectTransaction? transaction = null, AtomPointer? atomPointer = null)
            : base(transaction, atomPointer)
        {
            // If no key provided, generate a non-cryptographic stable hash code.
            HashKey = hashKey == 0 ? Guid.NewGuid().GetHashCode() : hashKey;
        }

        // Reads an attribute from the current mutable snapshot.
        public new object? GetAt(string name)
        {
            var mutables = this.Transaction!.GetMutables();
            var currentValue = mutables.GetAt(HashKey);
            if (currentValue == null) return null;
            return currentValue.GetAt(name);
        }

        // Mutates an attribute by updating the current snapshot in the transaction store.
        // Returns this instance for fluent chaining.
        public new MutableObject SetAt(string name, object value)
        {
            var mutables = this.Transaction!.GetMutables();
            var currentValue = mutables.GetAt(HashKey);
            if (currentValue == null) throw new InvalidOperationException("Mutable object not found!");
            currentValue = currentValue.SetAt(name, value);
            mutables = mutables.SetAt(HashKey, currentValue);
            this.Transaction.SetMutables(mutables);
            return this;
        }

        // Checks if the attribute is defined in the current mutable snapshot.
        public new bool HasDefinedAttr(string name)
        {
            var mutables = this.Transaction!.GetMutables();
            var currentValue = mutables.GetAt(HashKey);
            return currentValue != null && currentValue.HasDefinedAttr(name);
        }
    }

    // Simple Atom that wraps a string value. Useful for stable, deduplicated references.
    public class Literal : Atom
    {
        public string String { get; set; }

        public Literal(string? str = null, ObjectTransaction? transaction = null, AtomPointer? atomPointer = null)
            : base(transaction, atomPointer)
        {
            String = str ?? string.Empty;
        }

        public override string ToString() => String;
    }

    // Atom that stores binary content with optional metadata.
    // Content is kept in-memory as a byte array; larger content may require a streaming strategy.
    public class BytesAtom : Atom
    {
        public string? Filename { get; set; }
        public string? Mimetype { get; set; }
        public byte[] Content { get; set; }

        public BytesAtom(string? filename = null, string? mimetype = null, byte[]? content = null,
            ObjectTransaction? transaction = null, AtomPointer? atomPointer = null)
            : base(transaction, atomPointer)
        {
            Filename = filename;
            Mimetype = mimetype;
            Content = content ?? Array.Empty<byte>();
        }
    }
}