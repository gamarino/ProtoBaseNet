using System;

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

        protected Atom(ObjectTransaction transaction = null, AtomPointer atomPointer = null)
        {
            Transaction = transaction;
            AtomPointer = atomPointer;
        }

        protected virtual void Load()
        {
            // ...
            _loaded = true;
            AfterLoad();
        }

        public virtual void AfterLoad()
        {
        }

        protected virtual void Save()
        {
            // ...
        }

        public override int GetHashCode()
        {
            Save();
            return AtomPointer.GetHashCode();
        }
    }
}