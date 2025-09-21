using System;
using System.Threading.Tasks;

namespace ProtoBaseNet
{
    public interface IConcurrentOptimized
    {
        Atom RebaseOnConcurrentUpdate(Atom currentDbObject);
    }

    public abstract class AbstractSharedStorage
    {
        public abstract Task<AtomPointer> PushAtom(Atom atom);
        public abstract Task<Atom> GetAtom(AtomPointer atomPointer);
        public abstract Task<byte[]> GetBytes(AtomPointer atomPointer);
        public abstract Task<AtomPointer> PushBytes(byte[] data);
    }

    public abstract class BlockProvider
    {
        public abstract object GetConfigData(); // ConfigParser is python specific
        public abstract Tuple<Guid, int> GetNewWal();
        public abstract System.IO.Stream GetReader(Guid walId, int position);
        public abstract Guid GetWriterWal();
        public abstract System.IO.FileStream WriteStreamer(Guid walId);
        public abstract IDisposable RootContextManager();
        public abstract AtomPointer GetCurrentRootObject();
        public abstract void UpdateRootObject(AtomPointer newRoot);
        public abstract void CloseWal(Guid transactionId);
        public abstract void Close();
    }
}