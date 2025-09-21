using System;

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

    public class DBObject : Atom
    {
        public DBObject(ObjectTransaction? transaction = null, AtomPointer? atomPointer = null, params object[] kwargs)
            : base(transaction, atomPointer)

        {
            // handle kwargs
        }

        public DBObject SetAt(string name, object value)
        {
            // ...
            return new DBObject();
        }

        public bool HasDefinedAttr(string name)
        {
            // ...
            return false;
        }
    }

    public class MutableObject : Atom
    {
        public int HashKey { get; set; }

        public MutableObject(int hashKey = 0, ObjectTransaction transaction = null, AtomPointer atomPointer = null)
            : base(transaction, atomPointer)
        {
            HashKey = hashKey == 0 ? Guid.NewGuid().GetHashCode() : hashKey;
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