using System;
using System.Collections.Generic;
using System.Threading;

namespace ProtoBaseNet
{
    /// <summary>
    /// Scoped lock + storage context manager for root operations.
    /// Acquires a monitor on a shared gate and opens a storage root-context.
    /// Guarantees release of both resources via IDisposable.
    /// </summary>
    internal sealed class SpaceContextManager : IDisposable
    {
        private readonly object _gate;
        private readonly IDisposable _storageContext;

        public SpaceContextManager(object gate, ObjectSpace space)
        {
            // Enter storage root-context before taking the in-process lock.
            _storageContext = space.Storage.RootContextManager();
            _gate = gate;
            Monitor.Enter(_gate);
        }

        public void Dispose()
        {
            // Ensure lock is released even if storage context disposal throws.
            try
            {
                _storageContext.Dispose();
            }
            finally
            {
                Monitor.Exit(_gate);
            }
        }
    }

    /// <summary>
    /// Top-level façade over the storage backend.
    /// Responsible for database lifecycle, root history management, and global locking.
    /// </summary>
    public class ObjectSpace
    {
        // Shared storage abstraction used by all operations.
        internal SharedStorage Storage { get; }
        // Simple state machine for open/close lifecycle.
        private string State { get; set; }
        // Process-wide gate for serializing root updates.
        private readonly object _lock = new();

        public ObjectSpace(SharedStorage storage)
        {
            Storage = storage ?? throw new ArgumentNullException(nameof(storage));
            State = "Running";
        }

        /// <summary>
        /// Opens an existing database by name. Throws if not found.
        /// Thread-safe via ObjectSpace lock.
        /// </summary>
        public Database OpenDatabase(string databaseName)
        {
            if (databaseName is null) throw new ArgumentNullException(nameof(databaseName));
            lock (_lock)
            {
                if (State != "Running")
                    throw new ProtoValidationException("Object space is not running!");

                var databases = ReadDbCatalog();
                if (databases.Has(databaseName))
                {
                    return new Database(this, databaseName);
                }

                throw new ProtoValidationException($"Database {databaseName} does not exist!");
            }
        }

        /// <summary>
        /// Creates a new database entry in the catalog and persists a new root snapshot.
        /// Protected by the root locker to coordinate with other writers.
        /// </summary>
        public Database NewDatabase(string databaseName)
        {
            if (databaseName is null) throw new ArgumentNullException(nameof(databaseName));
            lock (_lock)
            {
                if (State != "Running")
                    throw new ProtoValidationException("Object space is not running!");

                using (GetRootLocker())
                {
                    var updateTr = new ObjectTransaction(objectSpace: this, storage: Storage);
                    var currentHist = GetSpaceHistory();
                    var currentRoot = currentHist.Count > 0
                        ? (RootObject)currentHist.GetAt(0)!
                        : new RootObject(
                            objectRoot: new DbDictionary<object>(updateTr),
                            literalRoot: new DbDictionary<object>(updateTr),
                            transaction: updateTr);

                    var databases = currentRoot.ObjectRoot as DbDictionary<object>;
                    if (databases == null || !databases.Has(databaseName))
                    {
                        databases ??= new DbDictionary<object>(updateTr);
                        var newDatabases = databases.SetAt(databaseName, new DbDictionary<object>(updateTr));
                        var newRoot = new RootObject(newDatabases, currentRoot.LiteralRoot, updateTr);
                        var spaceHistory = GetSpaceHistory();
                        var newHistory = spaceHistory.InsertAt(0, newRoot);
                        newHistory.Save();
                        Storage.SetCurrentRoot(newHistory.AtomPointer!);

                        return new Database(this, databaseName);
                    }

                    throw new ProtoValidationException($"Database {databaseName} already exists!");
                }
            }
        }

        /// <summary>
        /// Renames an existing database. Fails if the source does not exist or the target already exists.
        /// Persists a new root snapshot in the history.
        /// </summary>
        public void RenameDatabase(string oldName, string newName)
        {
            if (oldName is null) throw new ArgumentNullException(nameof(oldName));
            if (newName is null) throw new ArgumentNullException(nameof(newName));

            lock (_lock)
            {
                if (State != "Running")
                    throw new ProtoValidationException("Object space is not running!");

                using (GetRootLocker())
                {
                    var updateTr = new ObjectTransaction(objectSpace: this, storage: Storage);
                    var currentHist = GetSpaceHistory();
                    var currentRoot = currentHist.Count > 0
                        ? (RootObject)currentHist.GetAt(0)!
                        : new RootObject(
                            objectRoot: new DbDictionary<object>(updateTr),
                            literalRoot: new DbDictionary<object>(updateTr),
                            transaction: updateTr);

                    var databases = currentRoot.ObjectRoot as DbDictionary<object>;
                    if (databases != null && databases.Has(oldName))
                    {
                        var database = databases.GetAt(oldName);
                        var tempDatabases = databases.RemoveAt(oldName);
                        if (tempDatabases.Has(newName))
                        {
                            throw new ProtoValidationException($"Database {newName} already exists!");
                        }

                        var newDatabases = tempDatabases.SetAt(newName, database!);
                        var newRoot = new RootObject(newDatabases, currentRoot.LiteralRoot, updateTr);
                        var spaceHistory = GetSpaceHistory();
                        var newHistory = spaceHistory.InsertAt(0, newRoot);
                        newHistory.Save();
                        Storage.SetCurrentRoot(newHistory.AtomPointer!);
                    }
                    else
                    {
                        throw new ProtoValidationException($"Database {oldName} does not exist!");
                    }
                }
            }
        }

        /// <summary>
        /// Removes a database by name and persists a new root snapshot.
        /// </summary>
        public void RemoveDatabase(string name)
        {
            if (name is null) throw new ArgumentNullException(nameof(name));
            lock (_lock)
            {
                if (State != "Running")
                    throw new ProtoValidationException("Object space is not running!");

                using (GetRootLocker())
                {
                    var updateTr = new ObjectTransaction(objectSpace: this, storage: Storage);
                    var currentHist = GetSpaceHistory();
                    var currentRoot = currentHist.Count > 0
                        ? (RootObject)currentHist.GetAt(0)!
                        : new RootObject(transaction: updateTr);

                    var databases = currentRoot.ObjectRoot as DbDictionary<object>;
                    if (databases != null && databases.Has(name))
                    {
                        var newDatabases = databases.RemoveAt(name);
                        var newRoot = new RootObject(newDatabases, currentRoot.LiteralRoot, updateTr);
                        var spaceHistory = GetSpaceHistory();
                        var newHistory = spaceHistory.InsertAt(0, newRoot);
                        newHistory.Save();
                        Storage.SetCurrentRoot(newHistory.AtomPointer!);
                    }
                    else
                    {
                        throw new ProtoValidationException($"Database {name} does not exist!");
                    }
                }
            }
        }

        /// <summary>
        /// Returns the current database catalog (dictionary) from the root object.
        /// If none is present, returns an empty catalog.
        /// </summary>
        private DbDictionary<object> ReadDbCatalog()
        {
            var spaceRoot = GetSpaceRoot();
            if (spaceRoot.ObjectRoot is not DbDictionary<object> dict)
                return new DbDictionary<object>();
            return dict;
        }

        /// <summary>
        /// Creates a scoped lock and storage context for root modifications.
        /// </summary>
        internal IDisposable GetRootLocker() => new SpaceContextManager(_lock, this);

        /// <summary>
        /// Loads the entire history list of root snapshots.
        /// If the storage has no current root pointer, returns an empty list.
        /// </summary>
        internal DbList<RootObject> GetSpaceHistory(bool lockHistory = false)
        {
            var readTr = new ObjectTransaction(objectSpace: this, storage: Storage);
            var rootPointer = Storage.ReadCurrentRoot();

            // Presence of a pointer indicates an existing history.
            if (rootPointer is not null)
            {
                var spaceHistory = new DbList<RootObject>(readTr, rootPointer);
                spaceHistory.Load();
                return spaceHistory;
            }

            return new DbList<RootObject>(readTr);
        }

        /// <summary>
        /// Returns the latest root object in the history, or a freshly initialized one.
        /// </summary>
        internal RootObject GetSpaceRoot()
        {
            var readTr = new ObjectTransaction(objectSpace: this, storage: Storage);
            var spaceHistory = GetSpaceHistory();

            if (spaceHistory.Count == 0)
                return new RootObject(new DbDictionary<object>(readTr), new DbDictionary<object>(readTr), readTr);

            return spaceHistory.GetAt(0)!;
        }

        /// <summary>
        /// Appends a new root snapshot to the history and updates the storage's current root pointer.
        /// </summary>
        internal void SetSpaceRoot(RootObject newSpaceRoot)
        {
            var updateTr = new ObjectTransaction(objectSpace: this, storage: Storage);
            var spaceHistory = GetSpaceHistory();

            newSpaceRoot.Transaction = updateTr;
            var newSpaceHistory = spaceHistory.InsertAt(0, newSpaceRoot);
            newSpaceHistory.Save();

            Storage.SetCurrentRoot(newSpaceHistory.AtomPointer!);
        }

        /// <summary>
        /// Variant of SetSpaceRoot that reuses a preloaded history list under the same lock/transaction.
        /// </summary>
        internal void SetSpaceRootLocked(RootObject newSpaceRoot, DbList<RootObject> currentHistory)
        {
            var updateTr = new ObjectTransaction(objectSpace: this, storage: Storage);
            newSpaceRoot.Transaction = updateTr;

            currentHistory.Transaction = updateTr;
            var spaceHistory = currentHistory.InsertAt(0, newSpaceRoot);
            spaceHistory.Save();
            Storage.SetCurrentRoot(spaceHistory.AtomPointer!);
        }

        /// <summary>
        /// Closes the object space and releases underlying storage resources.
        /// </summary>
        public void Close()
        {
            lock (_lock)
            {
                if (State != "Running")
                    throw new ProtoValidationException("Object space is not running!");

                Storage.Close();
                State = "Closed";
            }
        }
    }

    /// <summary>
    /// Database façade inside an ObjectSpace.
    /// Provides transactional access to a single database root.
    /// </summary>
    public class Database : IDisposable
    {
        internal ObjectSpace ObjectSpace { get; }
        public string DatabaseName { get; }
        private string State { get; set; } = "Running";

        internal Database(ObjectSpace objectSpace, string databaseName)
        {
            ObjectSpace = objectSpace ?? throw new ArgumentNullException(nameof(objectSpace));
            DatabaseName = databaseName ?? throw new ArgumentNullException(nameof(databaseName));
        }

        /// <summary>
        /// Begins a new transaction bound to this database.
        /// </summary>
        public ObjectTransaction NewTransaction()
        {
            var dbRoot = ReadDbRoot();
            return new ObjectTransaction(this, dbRoot: dbRoot);
        }

        /// <summary>
        /// Creates a new database (branch) and writes a creation timestamp in its root.
        /// </summary>
        public Database NewBranchDatabase(string newDbName)
        {
            var newDb = ObjectSpace.NewDatabase(newDbName);
            using var creationTr = newDb.NewTransaction();
            creationTr.SetRootObject("_creation_timestamp", new Literal(DateTime.Now.ToString()));
            creationTr.Commit();
            return newDb;
        }

        /// <summary>
        /// Placeholder for time-travel: retrieve a database view at a given time.
        /// </summary>
        public Database GetStateAt(DateTime when, string snapshotName)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Reads the current database root from the space root, or returns an empty root if not present.
        /// </summary>
        internal DbDictionary<object> ReadDbRoot()
        {
            var readTr = new ObjectTransaction(this);
            var spaceRoot = ObjectSpace.GetSpaceRoot();

            if (spaceRoot.ObjectRoot is DbDictionary<object> dbCatalog && dbCatalog.Has(DatabaseName))
            {
                if (dbCatalog.GetAt(DatabaseName) is DbDictionary<object> dbRoot)
                {
                    dbRoot.Load();
                    return dbRoot;
                }
            }

            return new DbDictionary<object>(readTr);
        }

        /// <summary>
        /// Persists a new database root under this database name and updates the space root.
        /// </summary>
        internal void SetDbRoot(DbDictionary<object> newDbRoot)
        {
            var updateTr = new ObjectTransaction(this);
            var initialRoot = ObjectSpace.GetSpaceRoot();
            initialRoot.Transaction = updateTr;
            initialRoot.Load();

            var currentObjectRoot = initialRoot.ObjectRoot as DbDictionary<object> ?? new DbDictionary<object>(updateTr);
            var newObjectRoot = currentObjectRoot.SetAt(DatabaseName, newDbRoot);
            var newSpaceRoot = new RootObject(newObjectRoot, initialRoot.LiteralRoot, updateTr);
            newSpaceRoot.Save();

            ObjectSpace.SetSpaceRoot(newSpaceRoot);
            updateTr.Abort();
        }

        public void Dispose()
        {
            State = "Closed";
        }
    }

    /// <summary>
    /// Transaction boundary for reading/writing roots and atoms.
    /// Tracks staged roots/literals and coordinates commit/abort with the object space.
    /// </summary>
    public class ObjectTransaction : IDisposable
    {
        private readonly object _lock = new();
        public string State { get; private set; } = "Running";
        
        private ObjectTransaction? EnclosingTransaction { get; }
        private DbDictionary<object>? TransactionRoot { get; set; }
        internal DbDictionary<object> NewRoots { get; set; }
        internal DbDictionary<object> NewLiterals { get; set; }
        
        private Database? Database { get; }
        private ObjectSpace ObjectSpace { get; }
        internal SharedStorage Storage { get; }

        public ObjectTransaction(
            Database? database = null,
            ObjectSpace? objectSpace = null,
            DbDictionary<object>? dbRoot = null,
            SharedStorage? storage = null,
            ObjectTransaction? enclosingTransaction = null)
        {
            EnclosingTransaction = enclosingTransaction;
            
            // Wiring of context: either bound to a Database (preferred) or directly to an ObjectSpace.
            if (database is not null)
            {
                Database = database;
                ObjectSpace = database.ObjectSpace;
            }
            else if (objectSpace is not null)
            {
                ObjectSpace = objectSpace;
            }
            else
            {
                throw new ArgumentException("Either database or objectSpace must be provided.");
            }

            Storage = storage ?? ObjectSpace.Storage;
            TransactionRoot = dbRoot;
            
            // Staging areas for newly created or modified roots/literals within this transaction.
            NewRoots = new DbDictionary<object>(this);
            NewLiterals = new DbDictionary<object>(this);
        }

        /// <summary>
        /// Reads a named root object from the transaction view (staged or base).
        /// </summary>
        public Atom? GetRootObject(string name)
        {
            if (name is null) throw new ArgumentNullException(nameof(name));
            lock (_lock)
            {
                return TransactionRoot?.GetAt(name);
            }
        }

        /// <summary>
        /// Stages a root object update and ensures the atom is saved within this transaction.
        /// </summary>
        public void SetRootObject(string name, Atom? value)
        {
            if (name is null) throw new ArgumentNullException(nameof(name));

            if (value != null)
            {
                value.Transaction = this;
                value.Save();
            }

            lock (_lock)
            {
                NewRoots = NewRoots.SetAt(name, value!);
                if (TransactionRoot != null)
                {
                    TransactionRoot = TransactionRoot.SetAt(name, value!);
                }
                else
                {
                    TransactionRoot = new DbDictionary<object>(this).SetAt(name, value!);
                }
            }
        }

        /// <summary>
        /// Commits staged changes:
        /// - If this is a top-level transaction, merges roots and updates the database root under a space lock.
        /// - If nested, merges staged data into the enclosing transaction.
        /// </summary>
        public void Commit()
        {
            lock (_lock)
            {
                if (State != "Running")
                    throw new ProtoValidationException($"Transaction is not running ({State}). It could not be committed!");

                if (EnclosingTransaction == null)
                {
                    if (NewRoots.Count > 0)
                    {
                        using (ObjectSpace.GetRootLocker())
                        {
                            var currentDbRoot = Database!.ReadDbRoot();
                            var newDbRoot = currentDbRoot;

                            foreach (var (key, value) in NewRoots.AsIterable())
                            {
                                newDbRoot = newDbRoot.SetAt((string)key, value!);
                            }
                            
                            newDbRoot.Transaction = this;
                            newDbRoot.Save();
                            Database.SetDbRoot(newDbRoot);
                        }
                    }
                }
                else
                {
                    // Propagate staged data upward for a single final commit at the top level.
                    EnclosingTransaction.NewRoots = EnclosingTransaction.NewRoots.Merge(NewRoots);
                    EnclosingTransaction.NewLiterals = EnclosingTransaction.NewLiterals.Merge(NewLiterals);
                }

                State = "Committed";
            }
        }

        /// <summary>
        /// Aborts the transaction. No changes are pushed to the storage.
        /// </summary>
        public void Abort()
        {
            lock (_lock)
            {
                if (State != "Running")
                    throw new ProtoValidationException($"Transaction is not running ({State}). It could not be aborted!");
                
                State = "Aborted";
            }
        }

        public void Dispose()
        {
            // Best-effort rollback if the user forgets to commit/abort.
            if (State == "Running")
            {
                Abort();
            }
        }
    }
}
