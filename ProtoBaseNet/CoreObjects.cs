using System;
using System.Reflection;

namespace ProtoBaseNet
{
    /// <summary>
    /// Represents the root entry for the object graph stored in the space.
    /// It holds pointers to the database object root and the literals root,
    /// along with a creation timestamp for historical queries and auditing.
    /// </summary>
    public class RootObject : Atom
    {
        /// <summary>
        /// Gets or sets the root where application objects and databases are anchored.
        /// </summary>
        public Atom? ObjectRoot { get; set; }

        /// <summary>
        /// Gets or sets the root where shared literals (e.g., deduplicated strings) are anchored.
        /// </summary>
        public Atom? LiteralRoot { get; set; }

        /// <summary>
        /// Gets or sets the timestamp when this root instance was created.
        /// </summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="RootObject"/> class.
        /// </summary>
        /// <param name="objectRoot">The root for application objects.</param>
        /// <param name="literalRoot">The root for shared literals.</param>
        /// <param name="transaction">The transaction context.</param>
        /// <param name="atomPointer">The pointer to this atom in storage.</param>
        public RootObject(Atom? objectRoot = null, Atom? literalRoot = null, ObjectTransaction? transaction = null,
            AtomPointer? atomPointer = null)
            : base(transaction, atomPointer)
        {
            ObjectRoot = objectRoot;
            LiteralRoot = literalRoot;
            CreatedAt = DateTime.Now;
        }
    }

    /// <summary>
    /// A base object with attribute storage based on a key-value dictionary.
    /// The default behavior is functional: <see cref="SetAt"/> returns a new <see cref="DbObject"/> instance
    /// with an updated attributes map, leaving the original untouched.
    /// </summary>
    public class DbObject : Atom
    {
        /// <summary>
        /// The backing attribute map.
        /// </summary>
        protected DbDictionary<object> Attributes = new();

        /// <summary>
        /// Initializes a new instance of the <see cref="DbObject"/> class with existing attributes.
        /// </summary>
        /// <param name="attributes">The initial attributes for the object.</param>
        /// <param name="transaction">The transaction context.</param>
        /// <param name="atomPointer">The pointer to this atom in storage.</param>
        public DbObject(DbDictionary<object> attributes, ObjectTransaction? transaction = null, AtomPointer? atomPointer = null, params object[] kwargs)
            : base(transaction, atomPointer)
        {
            Attributes = attributes ?? new DbDictionary<object>();
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="DbObject"/> class.
        /// </summary>
        /// <param name="transaction">The transaction context.</param>
        /// <param name="atomPointer">The pointer to this atom in storage.</param>
        public DbObject(ObjectTransaction? transaction = null, AtomPointer? atomPointer = null, params object[] kwargs)
            : base(transaction, atomPointer)
        {
            Attributes = new DbDictionary<object>();
        }

        /// <summary>
        /// Retrieves an attribute value by name.
        /// </summary>
        /// <param name="name">The name of the attribute.</param>
        /// <returns>The attribute value, or null if not present.</returns>
        public virtual object? GetAt(string name)
        {
            // Se usan BindingFlags para asegurar que se encuentren campos públicos y no públicos.
            var fieldDefinition = GetType().GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (fieldDefinition != null)
            {
                // El método correcto es GetValue(), pasando 'this' como la instancia de la cual leer el valor.
                return fieldDefinition.GetValue(this);
            }
            
            return Attributes.GetAt(name);
        }

        /// <summary>
        /// Returns a new <see cref="DbObject"/> with an attribute set to a new value.
        /// This preserves the immutability of the original instance.
        /// </summary>
        /// <param name="name">The name of the attribute to set.</param>
        /// <param name="value">The new value for the attribute.</param>
        /// <returns>A new <see cref="DbObject"/> with the updated attribute.</returns>
        public virtual DbObject SetAt(string name, object value)
        {
            return new DbObject(
                Attributes.SetAt(name, value),
                Transaction,
                AtomPointer);
        }

        /// <summary>
        /// Checks whether an attribute key is present.
        /// </summary>
        /// <param name="name">The name of the attribute.</param>
        /// <returns>True if the attribute is defined, otherwise false.</returns>
        public virtual bool HasDefinedAttr(string name)
        {
            return Attributes.Has(name);
        }
    }

    /// <summary>
    /// A wrapper that provides mutability semantics over a <see cref="DbObject"/>.
    /// It redirects reads and writes to a per-transaction mutable store, indexed by a stable <see cref="HashKey"/>.
    /// </summary>
    public class MutableObject : DbObject
    {
        /// <summary>
        /// Gets the logical key that identifies the mutable entry in the transaction-scoped store.
        /// </summary>
        public int HashKey { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="MutableObject"/> class.
        /// </summary>
        /// <param name="hashKey">The hash key to identify the object. If 0, a new key is generated.</param>
        /// <param name="transaction">The transaction context.</param>
        /// <param name="atomPointer">The pointer to this atom in storage.</param>
        public MutableObject(int hashKey = 0, ObjectTransaction? transaction = null, AtomPointer? atomPointer = null)
            : base(transaction, atomPointer)
        {
            HashKey = hashKey == 0 ? Guid.NewGuid().GetHashCode() : hashKey;
        }

        /// <summary>
        /// Reads an attribute from the current mutable snapshot in the transaction.
        /// </summary>
        /// <param name="name">The name of the attribute.</param>
        /// <returns>The attribute value, or null if not found.</returns>
        public new object? GetAt(string name)
        {
            var mutables = this.Transaction!.GetMutables();
            var currentValue = mutables.GetAt(HashKey);

            if (currentValue == null) return null;
            return currentValue.GetAt(name);
        }

        /// <summary>
        /// Mutates an attribute by updating the current snapshot in the transaction store.
        /// </summary>
        /// <param name="name">The name of the attribute to set.</param>
        /// <param name="value">The new value.</param>
        /// <returns>This instance for fluent chaining.</returns>
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

        /// <summary>
        /// Checks if an attribute is defined in the current mutable snapshot.
        /// </summary>
        /// <param name="name">The name of the attribute.</param>
        /// <returns>True if the attribute is defined, otherwise false.</returns>
        public new bool HasDefinedAttr(string name)
        {
            var mutables = this.Transaction!.GetMutables();
            var currentValue = mutables.GetAt(HashKey);
            return currentValue != null && currentValue.HasDefinedAttr(name);
        }
    }

    /// <summary>
    /// A simple <see cref="Atom"/> that wraps a string value. Used for stable, deduplicated references.
    /// </summary>
    public class Literal : Atom
    {
        /// <summary>
        /// Gets or sets the string value.
        /// </summary>
        public string String { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="Literal"/> class.
        /// </summary>
        /// <param name="str">The string value.</param>
        /// <param name="transaction">The transaction context.</param>
        /// <param name="atomPointer">The pointer to this atom in storage.</param>
        public Literal(string? str = null, ObjectTransaction? transaction = null, AtomPointer? atomPointer = null)
            : base(transaction, atomPointer)
        {
            String = str ?? string.Empty;
        }

        /// <summary>
        /// Returns the string value of the literal.
        /// </summary>
        /// <returns>The string value.</returns>
        public override string ToString() => String;
    }

    /// <summary>
    /// An <see cref="Atom"/> that stores binary content with optional metadata.
    /// </summary>
    /// <remarks>
    /// The content is kept in-memory as a byte array. Larger content may require a streaming strategy.
    /// </remarks>
    public class BytesAtom : Atom
    {
        /// <summary>
        /// Gets or sets the optional filename for the binary content.
        /// </summary>
        public string? Filename { get; set; }

        /// <summary>
        /// Gets or sets the optional MIME type for the binary content.
        /// </summary>
        public string? Mimetype { get; set; }

        /// <summary>
        /// Gets or sets the binary content as a byte array.
        /// </summary>
        public byte[] Content { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="BytesAtom"/> class.
        /// </summary>
        /// <param name="filename">The filename.</param>
        /// <param name="mimetype">The MIME type.</param>
        /// <param name="content">The binary content.</param>
        /// <param name="transaction">The transaction context.</param>
        /// <param name="atomPointer">The pointer to this atom in storage.</param>
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