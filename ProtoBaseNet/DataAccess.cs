using System;
using System.Threading.Tasks;

namespace ProtoBaseNet
{
    internal sealed class SpaceContextManager : IDisposable
    {
        private readonly object _gate;
        private readonly ObjectSpace _space;
        private readonly IDisposable _storageContext;

        public SpaceContextManager(object gate, ObjectSpace space)
        {
            _space = space;
            _storageContext = space.Storage.RootContextManager();
            _gate = gate; 
            System.Threading.Monitor.Enter(_gate);
        }

        public void Dispose()
        {
            _storageContext.Dispose();
            System.Threading.Monitor.Exit(_gate);   
        }
    }

    public class ObjectSpace
    {
        internal SharedStorage Storage { get; }
        string State { get; set; }
        private readonly object _lock = new object();

        public ObjectSpace(SharedStorage storage)
        {
            Storage = storage;
            State = "Running";
        }

        public Database OpenDatabase(string databaseName) => new Database(this, databaseName);
        public Database NewDatabase(string databaseName) => new Database(this, databaseName);

        public void RenameDatabase(string oldName, string newName)
        {
        }

        public void RemoveDatabase(string name)
        {
        }

        internal IDisposable GetRootLocker()
        {
            return new SpaceContextManager(_lock, this);
        }

        internal object GetSpaceHistory(bool lockHistory = false) => null;

        internal RootObject GetSpaceRoot()
        {
            return null;
        }

        internal void SetSpaceRoot(RootObject newSpaceRoot)
        {
        }

        internal void SetSpaceRootLocked(RootObject newSpaceRoot, object currentHistory)
        {
        } // currentHistory is List

        public Literal? GetLiteral(string literal)
        {
            return null;    
        }
        
        internal Dictionary<string, Literal> GetLiterals(List<string> literals)
        {
            return null;
        }

        public void Close()
        {
        }
    }

    public class Database
    {
        internal new ObjectSpace ObjectSpace { get; }
        public string? DatabaseName { get; } 
        private string State { get; set; } = "Running";

        internal Database(ObjectSpace objectSpace, string? databaseName = null)
        {
            ObjectSpace = objectSpace;
            DatabaseName = databaseName;
        }
        
        public ObjectTransaction NewTransaction() => new ObjectTransaction(this);
        public Database NewBranchDatabase(string newDbName) => new Database(ObjectSpace, newDbName);
        public Database GetStateAt(DateTime when, string snapshotName) => null;
    }

    public class ObjectTransaction
    {
        private ObjectTransaction? EnclosingTransaction { get; set; }
        public object? TransactionRoot { get; set; } // Dictionary
        public object? CurrentRoot { get; set; } // Dictionary
        Database? Database { get; }
        SharedStorage ObjectStore { get; }
        ObjectSpace? ObjectSpace { get; }
        // ... and many more properties

        internal ObjectTransaction(
            Database? database = null,
            ObjectSpace? objectSpace = null,
            ObjectTransaction? enclosingTransaction = null)
        {
            if (database != null)
            {
                Database = database;
                ObjectSpace = database.ObjectSpace;
            }
            else if (objectSpace != null)
            {
                Database = null;
                ObjectSpace = objectSpace;                
            }
            else
            {
                throw new ArgumentException("Either database or objectSpace parameter must be provided");
            }

            ObjectStore = ObjectSpace.Storage;
            EnclosingTransaction = enclosingTransaction;
        }

        public SharedStorage Storage { get; protected set; }
        public DbLiteral GetLiteral(string value) => new DbLiteral(value);
        public Atom ReadObject(string className, AtomPointer atomPointer) => null;

        public void UpdateCreatedLiterals(ObjectTransaction transaction, object newLiterals)
        {
        }

        public object NewLiterals; // placeholder

        public void Commit()
        {
        }

        public void Abort()
        {
        }
    }
}