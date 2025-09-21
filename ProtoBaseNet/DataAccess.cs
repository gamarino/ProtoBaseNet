using System;
using System.Collections.Generic;
using System.Threading;

namespace ProtoBaseNet
{
    /// <summary>
    /// Manages acquiring and releasing a lock on the ObjectSpace.
    /// </summary>
    internal sealed class SpaceContextManager : IDisposable
    {
        private readonly object _gate;
        private readonly IDisposable _storageContext;

        public SpaceContextManager(object gate, ObjectSpace space)
        {
            _storageContext = space.Storage.RootContextManager();
            _gate = gate;
            Monitor.Enter(_gate);
        }

        public void Dispose()
        {
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
    /// Represents the main entry point to the storage, managing databases.
    /// </summary>
    public class ObjectSpace
    {
        internal SharedStorage Storage { get; }
        private string State { get; set; }
        private readonly object _lock = new();

        public ObjectSpace(SharedStorage storage)
        {
            Storage = storage ?? throw new ArgumentNullException(nameof(storage));
            State = "Running";
        }

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

            // AtomPointer is a struct-like reference holder; assume default is "unset"
            if (rootPointer is not null)
            {
                var spaceHistory = new DbList<RootObject>(readTr, rootPointer);
                spaceHistory.Load();
                return spaceHistory;
            }

            return new DbList<RootObject>(readTr);
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

        internal void SetSpaceRootLocked(RootObject newSpaceRoot, DbList<RootObject> currentHistory)
        {
            var updateTr = new ObjectTransaction(objectSpace: this, storage: Storage);
            newSpaceRoot.Transaction = updateTr;

            currentHistory.Transaction = updateTr;
            var spaceHistory = currentHistory.InsertAt(0, newSpaceRoot);
            spaceHistory.Save();
            Storage.SetCurrentRoot(spaceHistory.AtomPointer!);
        }

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

        public ObjectTransaction NewTransaction()
        {
            var dbRoot = ReadDbRoot();
            return new ObjectTransaction(this, dbRoot: dbRoot);
        }

        public Database NewBranchDatabase(string newDbName)
        {
            var newDb = ObjectSpace.NewDatabase(newDbName);
            using var creationTr = newDb.NewTransaction();
            creationTr.SetRootObject("_creation_timestamp", new Literal(DateTime.Now.ToString()));
            creationTr.Commit();
            return newDb;
        }

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

        public void Dispose()
        {
            State = "Closed";
        }
    }

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

        public Atom? GetRootObject(string name)
        {
            if (name is null) throw new ArgumentNullException(nameof(name));
            lock (_lock)
            {
                return TransactionRoot?.GetAt(name);
            }
        }

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
                    EnclosingTransaction.NewRoots = EnclosingTransaction.NewRoots.Merge(NewRoots);
                    EnclosingTransaction.NewLiterals = EnclosingTransaction.NewLiterals.Merge(NewLiterals);
                }

                State = "Committed";
            }
        }

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
            if (State == "Running")
            {
                Abort();
            }
        }
    }
}
