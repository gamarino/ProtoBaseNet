using System;
using System.Runtime.InteropServices;

namespace ProtoBaseNet
{
    public class RootObject : Atom
    {
        public Atom ObjectRoot { get; set; }
        public Atom LiteralRoot { get; set; }
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

    public class DbObject : Atom
    {
        protected DbDictionary<object>? Attributes = new DbDictionary<object>();
        
        public DbObject(DbDictionary<object> attributes, ObjectTransaction? transaction = null, AtomPointer? atomPointer = null, params object[] kwargs)
            : base(transaction, atomPointer)

        {
            Attributes = attributes;
        }
        
        public DbObject(ObjectTransaction? transaction = null, AtomPointer? atomPointer = null, params object[] kwargs)
            : base(transaction, atomPointer)

        {
            Attributes = null;
        }

        public virtual object GetAt(string name)
        {
            return Attributes.GetAt(name);
        }

        public virtual DbObject SetAt(string name, object value)
        {
            // ...
            return new DbObject(
                Attributes.SetAt(name, value),
                Transaction,
                AtomPointer);
        }

        public virtual bool HasDefinedAttr(string name)
        {
            Attributes.Has(name);
        }
    }

    public class MutableObject : DbObject
    {
        public int HashKey { get; }

        public MutableObject(int hashKey = 0, ObjectTransaction transaction = null, AtomPointer atomPointer = null)
            : base(null, transaction, atomPointer)
        {
            HashKey = hashKey == 0 ? Guid.NewGuid().GetHashCode() : hashKey;
        }

        public object GetAt(string name)
        {
            DbHashDictionary<object> mutables = this.Transaction.GetMutables();
            DbObject currentValue = mutables.GetAt(HashKey);
            if (currentValue == null) return null;
            return currentValue.GetAt(name);
        }

        public DbObject SetAt(string name, object value)
        {
            DbHashDictionary<object> mutables = this.Transaction.GetMutables();
            DbObject currentValue = mutables.GetAt(HashKey);
            if (currentValue == null) throw new Exception("Mutable object not found!");
            currentValue = currentValue.SetAt(name, value);
            mutables = mutables.SetAt(HashKey, currentValue);
            this.Transaction.SetMutables(mutables);
            return this;
        }

        public bool HasDefinedAttr(string name)
        {
            Attributes.Has(name);
        }

    }

    public class Literal : Atom
    {
        public string String { get; set; }

        public Literal(string str = null, ObjectTransaction transaction = null, AtomPointer atomPointer = null)
            : base(transaction, atomPointer)
        {
            String = str ?? "";
        }

        public override string ToString() => String;
    }

    public class BytesAtom : Atom
    {
        public string Filename { get; set; }
        public string Mimetype { get; set; }
        public byte[] Content { get; set; }

        public BytesAtom(string filename = null, string mimetype = null, byte[] content = null,
            ObjectTransaction transaction = null, AtomPointer atomPointer = null)
            : base(transaction, atomPointer)
        {
            Filename = filename;
            Mimetype = mimetype;
            Content = content;
        }
    }
}