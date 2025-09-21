namespace ProtoBaseNet;

public class MemoryStorage : SharedStorage
{
    private readonly Dictionary<AtomPointer, Atom> _atoms = new();
    private readonly Dictionary<AtomPointer, byte[]> _bytes = new();
    private AtomPointer _currentRoot = new AtomPointer();
    private readonly object _lock = new object();

    public override Task<AtomPointer> PushAtom(Atom atom)
    {
        var pointer = new AtomPointer();
        _atoms[pointer] = atom;
        return Task.FromResult(pointer);
    }

    public override Task<Atom> GetAtom(AtomPointer atomPointer)
    {
        return Task.FromResult(_atoms[atomPointer]);
    }

    public override Task<byte[]> GetBytes(AtomPointer atomPointer)
    {
        return Task.FromResult(_bytes[atomPointer]);
    }

    public override Task<AtomPointer> PushBytes(byte[] data)
    {
        var pointer = new AtomPointer();
        _bytes[pointer] = data;
        return Task.FromResult(pointer);
    }

    public override IDisposable RootContextManager()
    {
        return new DisposableAction(() => { });
    }

    public override AtomPointer ReadCurrentRoot()
    {
        return _currentRoot;
    }

    public override void SetCurrentRoot(AtomPointer newRootPointer)
    {
        _currentRoot = newRootPointer;
    }

    public override void FlushWal()
    {
        // No-op for memory storage
    }

    public override void Close()
    {
        _atoms.Clear();
        _bytes.Clear();
    }

    private class DisposableAction : IDisposable
    {
        private readonly Action _action;

        public DisposableAction(Action action)
        {
            _action = action;
        }

        public void Dispose()
        {
            _action();
        }
    }
}