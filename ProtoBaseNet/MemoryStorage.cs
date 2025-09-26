using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ProtoBaseNet;

// In-memory implementation of SharedStorage for testing and single-process scenarios.
// Characteristics:
// - Non-durable: data is lost when the process exits.
// - Simple pointer model: every Push* allocates a fresh AtomPointer; no de-duplication.
// - Thread-safety: minimal; a private lock exists but is currently unused by operations below.
// - Root management: maintains a single current root pointer in memory.
public class MemoryStorage : SharedStorage
{
    // In-memory maps of pointers to atoms and raw bytes.
    private readonly Dictionary<AtomPointer, Atom> _atoms = new();
    private readonly Dictionary<AtomPointer, byte[]> _bytes = new();
    private readonly Dictionary<AtomPointer, IDictionary<string, object>> _atomData = new();

    // Current root pointer for snapshot navigation.
    private AtomPointer _currentRoot = new AtomPointer();

    // Gate for potential synchronization across threads (not actively used here).
    private readonly object _lock = new object();

    // Stores a materialized Atom and returns a new pointer.
    public override Task<AtomPointer> PushAtom(Atom atom)
    {
        var pointer = new AtomPointer();
        _atoms[pointer] = atom;
        return Task.FromResult(pointer);
    }

    // Retrieves a previously stored Atom by pointer.
    public override Task<Atom> GetAtom(AtomPointer atomPointer)
    {
        return Task.FromResult(_atoms[atomPointer]);
    }

    // Retrieves raw bytes by pointer.
    public override Task<byte[]> GetBytes(AtomPointer atomPointer)
    {
        return Task.FromResult(_bytes[atomPointer]);
    }

    // Stores raw bytes and returns a new pointer.
    public override Task<AtomPointer> PushBytes(byte[] data)
    {
        var pointer = new AtomPointer();
        _bytes[pointer] = data;
        return Task.FromResult(pointer);
    }

    // Stores atom data as a dictionary and returns a new pointer.
    public override Task<IDictionary<string, object>> ReadAtom(AtomPointer atomPointer)
    {
        return Task.FromResult(_atomData[atomPointer]);
    }

    // Retrieves atom data as a dictionary by pointer.
    public override Task<AtomPointer> WriteAtom(IDictionary<string, object> data)
    {
        var pointer = new AtomPointer();
        _atomData[pointer] = data;
        return Task.FromResult(pointer);
    }

    // Returns a disposable root-context guard.
    // No-op here since memory storage does not require external coordination.
    public override IDisposable RootContextManager()
    {
        return new DisposableAction(() => { });
    }

    // Returns the current root pointer.
    public override AtomPointer ReadCurrentRoot()
    {
        return _currentRoot;
    }

    // Sets the current root pointer.
    public override void SetCurrentRoot(AtomPointer newRootPointer)
    {
        _currentRoot = newRootPointer;
    }

    // No-op: there is no WAL for in-memory storage.
    public override void FlushWal()
    {
        // No-op for memory storage
    }

    // Clears all in-memory data structures.
    public override void Close()
    {
        _atoms.Clear();
        _bytes.Clear();
    }

    // Simple disposable that executes the provided action on Dispose.
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