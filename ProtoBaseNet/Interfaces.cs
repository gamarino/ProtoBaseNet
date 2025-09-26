using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ProtoBaseNet
{
    /// <summary>
    /// High-level, asynchronous storage abstraction used by the object space.
    /// Responsibilities:
    /// - Persist/retrieve Atoms and raw byte payloads
    /// - Manage a global "root" pointer for snapshot/history navigation
    /// - Provide a root-level context guard (RootContextManager) to coordinate concurrent writers
    /// - Control WAL flushing and lifecycle closure
    /// </summary>
    /// <remarks>
    /// Implementations should guarantee durability and consistency semantics appropriate
    /// for the chosen backend (filesystem, database, cloud store, etc.).
    /// All I/O is Task-based to support non-blocking callers.
    /// </remarks>
    public abstract class SharedStorage
    {
        /// <summary>
        /// Persists a fully materialized Atom and returns its storage pointer.
        /// </summary>
        /// <param name="atom">The atom to persist.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains the pointer to the stored atom.</returns>
        public abstract Task<AtomPointer> PushAtom(Atom atom);

        /// <summary>
        /// Retrieves an Atom by its pointer.
        /// </summary>
        /// <param name="atomPointer">The pointer to the atom to retrieve.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains the retrieved atom.</returns>
        public abstract Task<Atom> GetAtom(AtomPointer atomPointer);

        /// <summary>
        /// Retrieves raw bytes by a pointer.
        /// </summary>
        /// <param name="atomPointer">The pointer to the data to retrieve.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains the retrieved byte array.</returns>
        public abstract Task<byte[]> GetBytes(AtomPointer atomPointer);

        /// <summary>
        /// Persists raw bytes and returns the resulting pointer.
        /// </summary>
        /// <param name="data">The byte array to persist.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains the pointer to the stored data.</returns>
        public abstract Task<AtomPointer> PushBytes(byte[] data);

        /// <summary>
        /// Retrieves atom data as a dictionary by its pointer.
        /// </summary>
        /// <param name="atomPointer">The pointer to the atom data to retrieve.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains the retrieved atom data as a dictionary.</returns>
        public abstract Task<IDictionary<string, object>> ReadAtom(AtomPointer atomPointer);

        /// <summary>
        /// Persists atom data from a dictionary and returns the resulting pointer.
        /// </summary>
        /// <param name="data">The atom data to persist.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains the pointer to the stored atom data.</returns>
        public abstract Task<AtomPointer> WriteAtom(IDictionary<string, object> data);

        /// <summary>
        /// Acquires a root-context guard for critical sections, such as root updates.
        /// </summary>
        /// <returns>An IDisposable that releases the context when disposed.</returns>
        public abstract IDisposable RootContextManager();

        /// <summary>
        /// Reads the current root pointer, which points to the latest snapshot of the database.
        /// </summary>
        /// <returns>The current root pointer.</returns>
        public abstract AtomPointer ReadCurrentRoot();

        /// <summary>
        /// Atomically updates the current root pointer to a new snapshot.
        /// </summary>
        /// <param name="newRootPointer">The new root pointer.</param>
        public abstract void SetCurrentRoot(AtomPointer newRootPointer);

        /// <summary>
        /// Ensures that the write-ahead log (or equivalent) is flushed to durable storage.
        /// </summary>
        public abstract void FlushWal();

        /// <summary>
        /// Closes the storage and releases any resources.
        /// </summary>
        public abstract void Close();
    }

    /// <summary>
    /// Represents a lower-level block storage provider.
    /// This is typically used as a building block for SharedStorage implementations, handling the details of
    /// write-ahead logging, and file I/O.
    /// </summary>
    public abstract class BlockProvider
    {
        /// <summary>
        /// Gets the backend-specific configuration data.
        /// </summary>
        /// <returns>An object containing configuration data.</returns>
        public abstract object GetConfigData();

        /// <summary>
        /// Allocates a new WAL stream identifier and initial position.
        /// </summary>
        /// <returns>A tuple containing the new WAL GUID and its starting position.</returns>
        public abstract Tuple<Guid, int> GetNewWal();

        /// <summary>
        /// Gets a stream for reading from a specific position in a WAL.
        /// </summary>
        /// <param name="walId">The ID of the WAL to read from.</param>
        /// <param name="position">The position in the WAL to start reading from.</param>
        /// <returns>A stream for reading the data.</returns>
        public abstract System.IO.Stream GetReader(Guid walId, int position);

        /// <summary>
        /// Gets the GUID of the current WAL used for writing.
        /// </summary>
        /// <returns>The writer WAL GUID.</returns>
        public abstract Guid GetWriterWal();

        /// <summary>
        /// Opens a writable file stream for the given WAL.
        /// </summary>
        /// <param name="walId">The ID of the WAL to write to.</param>
        /// <returns>A file stream for writing.</returns>
        public abstract System.IO.FileStream WriteStreamer(Guid walId);

        /// <summary>
        /// Acquires a root-context guard for coordinated updates at the block layer.
        /// </summary>
        /// <returns>An IDisposable that releases the context when disposed.</returns>
        public abstract IDisposable RootContextManager();

        /// <summary>
        /// Reads the current root object pointer as stored at the block layer.
        /// </summary>
        /// <returns>The current root pointer.</returns>
        public abstract AtomPointer GetCurrentRootObject();

        /// <summary>
        /// Atomically updates the root object pointer.
        /// </summary>
        /// <param name="newRoot">The new root pointer.</param>
        public abstract void UpdateRootObject(AtomPointer newRoot);

        /// <summary>
        /// Closes a WAL stream after a transaction is finalized.
        /// </summary>
        /// <param name="transactionId">The ID of the transaction whose WAL should be closed.</param>
        public abstract void CloseWal(Guid transactionId);

        /// <summary>
        /// Closes the block provider and releases all resources.
        /// </summary>
        public abstract void Close();
    }
}