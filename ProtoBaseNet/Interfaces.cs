using System;
using System.Threading.Tasks;

namespace ProtoBaseNet
{
    // High-level, asynchronous storage abstraction used by the object space.
    // Responsibilities:
    // - Persist/retrieve Atoms and raw byte payloads
    // - Manage a global "root" pointer for snapshot/history navigation
    // - Provide a root-level context guard (RootContextManager) to coordinate concurrent writers
    // - Control WAL flushing and lifecycle closure
    //
    // Notes:
    // - Implementations should guarantee durability and consistency semantics appropriate
    //   for the chosen backend (filesystem, database, cloud store, etc.).
    // - All I/O is Task-based to support non-blocking callers; callers may still block if needed.
    public abstract class SharedStorage
    {
        // Persist a fully materialized Atom and return its storage pointer.
        public abstract Task<AtomPointer> PushAtom(Atom atom);

        // Retrieve an Atom by pointer (includes deserialization/materialization).
        public abstract Task<Atom> GetAtom(AtomPointer atomPointer);

        // Retrieve raw bytes by pointer (low-level access).
        public abstract Task<byte[]> GetBytes(AtomPointer atomPointer);

        // Persist raw bytes and return the resulting pointer.
        public abstract Task<AtomPointer> PushBytes(byte[] data);

        // Acquire a root-context guard for critical sections (e.g., root updates).
        // Intended to be used with using/IDisposable for correct release.
        public abstract IDisposable RootContextManager();

        // Read the current root pointer (latest snapshot).
        public abstract AtomPointer ReadCurrentRoot();

        // Atomically update the current root pointer to a new snapshot.
        public abstract void SetCurrentRoot(AtomPointer newRootPointer);

        // Ensure write-ahead log (or equivalent) is flushed to durable storage.
        public abstract void FlushWal();

        // Close and release storage resources.
        public abstract void Close();
        
    }

    // Lower-level block storage provider, if a layered design is used.
    // Typically wraps WAL management, sequential/append-only writers, and random-access readers.
    // Serves as a building block for SharedStorage implementations.
    public abstract class BlockProvider
    {
        // Returns backend-specific configuration object (opaque to callers).
        public abstract object GetConfigData(); // ConfigParser is python specific

        // Allocates a new WAL stream identifier and initial position.
        public abstract Tuple<Guid, int> GetNewWal();

        // Random-access reader for a given WAL id and position.
        public abstract System.IO.Stream GetReader(Guid walId, int position);

        // Returns the current WAL id used for writing.
        public abstract Guid GetWriterWal();

        // Opens a writable file stream for the given WAL id.
        public abstract System.IO.FileStream WriteStreamer(Guid walId);

        // Acquire a root-context guard for coordinated updates at this layer.
        public abstract IDisposable RootContextManager();

        // Read the current root object pointer (as stored at the block layer).
        public abstract AtomPointer GetCurrentRootObject();

        // Update the root object pointer atomically.
        public abstract void UpdateRootObject(AtomPointer newRoot);

        // Close a WAL stream after a transaction is finalized.
        public abstract void CloseWal(Guid transactionId);

        // Close and release provider resources.
        public abstract void Close();
    }
}