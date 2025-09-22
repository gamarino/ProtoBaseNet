using System;
using System.Collections.Generic;
using System.Data;
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
    /// Represents the top-level façade over the storage backend.
    /// It is responsible for the database lifecycle, root history management, and global locking.
    /// </summary>
    public class ObjectSpace
    {
        internal SharedStorage Storage { get; }
        private string State { get; set; }
        private readonly object _lock = new();

        /// <summary>
        /// Initializes a new instance of the <see cref="ObjectSpace"/> class.
        /// </summary>
        /// <param name="storage">The storage backend to be used.</param>
        public ObjectSpace(SharedStorage storage)
        {
            Storage = storage ?? throw new ArgumentNullException(nameof(storage));
            State = "Running";
        }

        /// <summary>
        /// Opens an existing database by name.
        /// </summary>
        /// <param name="databaseName">The name of the database to open.</param>
        /// <returns>A <see cref="Database"/> instance.</returns>
        /// <exception cref="ProtoValidationException">Thrown if the object space is not running or the database does not exist.</exception>
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
        /// Creates a new database.
        /// </summary>
        /// <param name="databaseName">The name of the new database.</param>
        /// <returns>A <see cref="Database"/> instance for the newly created database.</returns>
        /// <exception cref="ProtoValidationException">Thrown if the object space is not running or a database with the same name already exists.</exception>
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
                        var newDatabases = databases.SetAt(databaseName, new DbDictionary<object>(updateTr));
                        var newRoot = new RootObject(newDatabases, currentRoot.LiteralRoot, updateTr);
                        var spaceHistory = GetSpaceHistory();
                        var newHistory = spaceHistory.InsertAt(0, newRoot);
                        newHistory.Transaction = updateTr;
                        newHistory.Save();
                        Storage.SetCurrentRoot(newHistory.AtomPointer!);

                        return new Database(this, databaseName);
                    }

                    throw new ProtoValidationException($"Database {databaseName} already exists!");
                }
            }
        }

        /// <summary>
        /// Renames an existing database.
        /// </summary>
        /// <param name="oldName">The current name of the database.</param>
        /// <param name="newName">The new name for the database.</param>
        /// <exception cref="ProtoValidationException">Thrown if the object space is not running, the old database does not exist, or the new name is already in use.</exception>
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
                        var newDatabases = tempDatabases.SetAt(newName, database!);
                        var newRoot = new RootObject(newDatabases, currentRoot.LiteralRoot, updateTr);
                        var spaceHistory = GetSpaceHistory();
                        var newHistory = spaceHistory.InsertAt(0, newRoot);
                        newHistory.Transaction = updateTr;
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
        /// Removes a database by name.
        /// </summary>
        /// <param name="name">The name of the database to remove.</param>
        /// <exception cref="ProtoValidationException">Thrown if the object space is not running or the database does not exist.</exception>
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
                        newHistory.Transaction = updateTr;
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

        private DbDictionary<object> ReadDbCatalog()
        {
            var spaceRoot = GetSpaceRoot();
            if (spaceRoot.ObjectRoot is not DbDictionary<object> dict)
                return new DbDictionary<object>();
            return dict;
        }

        internal IDisposable GetRootLocker() => new SpaceContextManager(_lock, this);

        internal DbList<RootObject> GetSpaceHistory(bool lockHistory = false)
        {
            var readTr = new ObjectTransaction(objectSpace: this, storage: Storage);
            var rootPointer = Storage.ReadCurrentRoot();

            if (rootPointer is not null)
            {
                var spaceHistory = new DbList<RootObject>();
                spaceHistory.Transaction = readTr;
                spaceHistory.AtomPointer = rootPointer;
                spaceHistory.Load();
                return spaceHistory;
            }

            var emptyHistory = new DbList<RootObject>();
            emptyHistory.Transaction = readTr;
            return emptyHistory;
        }

        internal RootObject GetSpaceRoot()
        {
            var readTr = new ObjectTransaction(objectSpace: this, storage: Storage);
            var spaceHistory = GetSpaceHistory();

            if (spaceHistory.Count == 0)
                return new RootObject(new DbDictionary<object>(readTr), new DbDictionary<object>(readTr), readTr);

            return spaceHistory.GetAt(0)!;
        }

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
    /// Represents a database within an <see cref="ObjectSpace"/>.
    /// Provides transactional access to a single, isolated object graph.
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
        /// Begins a new transaction to read or modify the database.
        /// </summary>
        /// <returns>A new <see cref="ObjectTransaction"/>.</returns>
        public ObjectTransaction NewTransaction()
        {
            var dbRoot = ReadDbRoot();
            return new ObjectTransaction(this, dbRoot: dbRoot);
        }

        /// <summary>
        /// Creates a new database (branch) and writes a creation timestamp in its root.
        /// </summary>
        /// <param name="newDbName">The name of the new database branch.</param>
        /// <returns>The newly created <see cref="Database"/>.</returns>
        public Database NewBranchDatabase(string newDbName)
        {
            var newDb = ObjectSpace.NewDatabase(newDbName);
            using var creationTr = newDb.NewTransaction();
            creationTr.SetRootObject("_creation_timestamp", new Literal(DateTime.Now.ToString()));
            creationTr.Commit();
            return newDb;
        }

        /// <summary>
        /// Retrieves a view of the database at a specific point in time.
        /// </summary>
        /// <param name="when">The timestamp to retrieve the state from.</param>
        /// <param name="snapshotName">The name for the snapshot.</param>
        /// <returns>A <see cref="Database"/> instance representing the state at the given time.</returns>
        /// <remarks>This feature is not yet implemented.</remarks>
        public Database GetStateAt(DateTime when, string snapshotName)
        {
            throw new NotImplementedException();
        }

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

        /// <summary>
        /// Disposes the database instance. This does not close the underlying ObjectSpace.
        /// </summary>
        public void Dispose()
        {
            State = "Closed";
        }
    }

    /// <summary>
    /// Represents a transaction for reading and writing objects.
    /// It tracks staged roots and literals and coordinates commit/abort operations.
    /// </summary>
    public class ObjectTransaction : IDisposable
    {
        private readonly object _lock = new();
        public string State { get; private set; } = "Running";
        
        private ObjectTransaction? EnclosingTransaction { get; }
        private DbDictionary<object>? TransactionRoot { get; set; }
        internal DbDictionary<object> NewRoots { get; set; }
        DbDictionary<object> RootBases = new DbDictionary<object>();
        internal DbDictionary<object> NewLiterals { get; set; }

        private DbHashDictionary<DbObject> _mutables = new();
        private readonly Dictionary<string, Literal> _literals = new();
        
        private Database? Database { get; }
        private ObjectSpace ObjectSpace { get; }
        internal SharedStorage Storage { get; }

        internal DbHashDictionary<DbObject> GetMutables()
        {
            return _mutables;
        }

        internal void SetMutables(DbHashDictionary<DbObject> mutables)
        {
            _mutables = mutables;
        }

        internal Atom ReadObject(string className, AtomPointer ap)
        {
            var type = Type.GetType(className) ?? 
                AppDomain.CurrentDomain.GetAssemblies()
                    .Select(a => a.GetType(className))
                    .FirstOrDefault(t => t != null);

            if (type == null) throw new ProtoValidationException($"Could not find type {className}");

            var atom = (Atom)Activator.CreateInstance(type)!;
            atom.AtomPointer = ap;
            atom.Transaction = this;
            atom.Load();
            return atom;
        }

        internal Literal GetLiteral(string value)
        {
            if (_literals.TryGetValue(value, out var literal))
            {
                return literal;
            }

            literal = new Literal(value) { Transaction = this };
            literal.Save();
            _literals[value] = literal;
            return literal;
        }

        internal void UpdateCreatedLiterals(ObjectTransaction transaction, DbDictionary<object> newLiterals)
        {
            // This method seems to be intended to save literals created in a transaction.
            // The current implementation of GetLiteral already saves the literal, so this might be redundant.
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ObjectTransaction"/> class.
        /// </summary>
        public ObjectTransaction(
            Database? database = null,
            ObjectSpace? objectSpace = null,
            DbDictionary<object>? dbRoot = null,
            SharedStorage? storage = null,
            ObjectTransaction? enclosingTransaction = null)
        {
            EnclosingTransaction = enclosingTransaction;
            
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
            
            NewRoots = new DbDictionary<object>(this);
            NewLiterals = new DbDictionary<object>(this);
        }

        /// <summary>
        /// Gets a named root object from the transaction's view.
        /// </summary>
        /// <param name="name">The name of the root object.</param>
        /// <returns>The root <see cref="Atom"/>, or null if not found.</returns>
        public Atom? GetRootObject(string name)
        {
            if (name is null) throw new ArgumentNullException(nameof(name));
            lock (_lock)
            {
                
                return TransactionRoot?.GetAt(name) as Atom;
            }
        }

        /// <summary>
        /// Stages an update for a named root object.
        /// </summary>
        /// <param name="name">The name of the root object.</param>
        /// <param name="value">The <see cref="Atom"/> to set as the root. If null, the root is removed.</param>
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
        /// Attempts to reconcile staged roots against the latest root snapshot without taking the global lock.
        /// Collections can provide a conflict-minimizing merge via ConcurrentUpdate when the StableId matches.
        /// If a collection cannot safely merge, the staged value is kept as-is and validated later under lock.
        /// </summary>
        /// <remarks>
        /// Contract for ConcurrentUpdate:
        /// - Input: previous collection snapshot (the current state at reconciliation time).
        /// - Output: merged collection that re-applies this transaction's changes on top of the provided snapshot,
        ///           or null to indicate non-mergeable changes (caller should fallback to conflict path).
        /// - Must be side-effect free w.r.t. storage; only returns a new Atom graph.
        /// </remarks>
        private DbDictionary<object> TryReconcileNewRootsAgainstCurrent()
        {
            var currentDbRoot = Database!.ReadDbRoot();
            var result = NewRoots;

            foreach (var (k, v) in NewRoots.AsIterable())
            {
                var key = (string)k;
                var stagedAtom = v as Atom;
                var currentAtom = currentDbRoot.GetAt(key) as Atom;

                // Only collections support merge semantics; other atoms are validated later under lock.
                if (stagedAtom is DbCollection stagedColl && currentAtom is DbCollection currentColl)
                {
                    // StableId equality indicates the same logical collection lineage; eligible for merge.
                    if (stagedColl.StableId == currentColl.StableId)
                    {
                        var merged = stagedColl.ConcurrentUpdate(currentColl);
                        if (merged is not null)
                        {
                            result = result.SetAt(key, merged);
                            continue;
                        }
                    }
                }

                // Keep staged value if no merge was possible; it will be checked under lock.
                result = result.SetAt(key, stagedAtom!);
            }

            return result;
        }

        /// <summary>
        /// Applies the reconciled new roots under the global space lock.
        /// Verifies that each root's base (captured in RootBases) still matches the current value,
        /// ensuring no concurrent conflicting update slipped in between reconciliation and commit.
        /// On success, persists the updated root snapshot atomically.
        /// </summary>
        /// <exception cref="ProtoDbConcurrencyException">Thrown when a root was modified concurrently.</exception>
        private void ApplyNewRootsUnderGlobalLock(DbDictionary<object> reconciledNewRoots)
        {
            using (ObjectSpace.GetRootLocker())
            {
                var currentDbRoot = Database!.ReadDbRoot();
                var newDbRoot = currentDbRoot;

                foreach (var (k, v) in reconciledNewRoots.AsIterable())
                {
                    var key = (string)k;
                    var stagedAtom = v as Atom;

                    var currentAtCommit = currentDbRoot.GetAt(key);
                    var expectedBase = RootBases.GetAt(key);

                    // Double-check under lock: if the base seen by this transaction differs from current, abort.
                    // Note: equality semantics should be identity-based or via a stable version/hash to avoid deep compares.
                    if (currentAtCommit != null && expectedBase != null && !ReferenceEquals(currentAtCommit, expectedBase))
                        throw new ProtoDbConcurrencyException($"Root {key} is already modified in another transaction!");

                    newDbRoot = newDbRoot.SetAt(key, stagedAtom!);
                }

                newDbRoot.Transaction = this;
                newDbRoot.Save();
                Database.SetDbRoot(newDbRoot);
            }
        }

        /// <summary>
        /// Commits the changes made in this transaction to the database.
        /// If this is a nested transaction, changes are merged into the parent transaction.
        /// </summary>
        /// <exception cref="ProtoValidationException">Thrown if the transaction is not in a running state.</exception>
        public void Commit()
        {
            lock (_lock)
            {
                if (State != "Running")
                    throw new ProtoValidationException($"Transaction is not running ({State}). It could not be committed!");

                if (EnclosingTransaction == null)
                {
                    // Pre root locking: try to recover from simoultaneous transactions
                    // modifing the same root.
                    
                    if (NewRoots.Count > 0)
                    {
                        // 1) Optimistic reconciliation outside the global lock:
                        //    Attempt to rebase staged roots against the latest database root, giving each collection
                        //    a chance to reapply changes from a newer snapshot in a conflict-free manner.
                        var reconciledNewRoots = TryReconcileNewRootsAgainstCurrent();

                        // 2) Critical section under global space lock:
                        //    Re-validate expected bases to prevent write-write conflicts and atomically persist the new root.
                        ApplyNewRootsUnderGlobalLock(reconciledNewRoots);
                    }
                }
                else
                {
                    EnclosingTransaction.NewRoots = EnclosingTransaction.NewRoots.Merge(NewRoots);
                    EnclosingTransaction.NewLiterals = EnclosingTransaction.NewLiterals.Merge(NewLiterals);
                }

                State = "Committed";
            }
        }

        /// <summary>
        /// Aborts the transaction, discarding all changes.
        /// </summary>
        /// <exception cref="ProtoValidationException">Thrown if the transaction is not in a running state.</exception>
        public void Abort()
        {
            lock (_lock)
            {
                if (State != "Running")
                    throw new ProtoValidationException($"Transaction is not running ({State}). It could not be aborted!");
                
                State = "Aborted";
            }
        }

        /// <summary>
        /// Disposes the transaction. If the transaction is still running, it will be aborted.
        /// </summary>
        public void Dispose()
        {
            if (State == "Running")
            {
                Abort();
            }
        }
    }
}
