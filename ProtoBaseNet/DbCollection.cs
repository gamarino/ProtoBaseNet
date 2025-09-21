namespace ProtoBaseNet
{
    // Base abstraction for secondary indexes over collections.
    // Implementations should provide:
    // - Add2Index: incorporate an item into the index
    // - RemoveFromIndex: remove an item from the index
    // - GetEmpty: return a structurally empty index instance (same type/config)
    public abstract class Index : Atom
    {
        // Adds the given item to the index structure.
        // Implementations must be idempotent or handle duplicates according to their semantics.
        public void Add2Index(object? item)
        {
            throw new NotImplementedException();    
        }
        
        // Removes the given item from the index structure.
        // Should not throw if the item is not present (no-op removal preferred).
        public void RemoveFromIndex(object? item)
        {
            throw new NotImplementedException();
        }

        // Returns a new, empty index of the same shape/configuration as this instance.
        public Index GetEmpty()
        {
            throw new NotImplementedException();
        }
        
    }
    
    // Abstract persistent collection base.
    // Provides index management hooks and a concurrency-friendly update entry point.
    public abstract class DbCollection : Atom
    {
        // Logical item count (override in concrete collections).
        public int Count { get; } = 0;

        // Optional dictionary of secondary indexes keyed by attribute name.
        internal DbDictionary<Index>? Indexes { get; set; } // analogous to a dictionary in the reference implementation

        // Adds or replaces an index for the given attribute. Returns a new collection by default.
        public virtual DbCollection IndexAdd(string attributeName, Index newIndex) => this;

        // Removes an index for the given attribute. Returns a new collection by default.
        public virtual DbCollection IndexRemove(string attributeName) => this;

        // Concurrent update hook: given a previous snapshot, returns a reconciled collection
        // or null if the operation cannot be applied safely (e.g., due to conflicts).
        public DbCollection? ConcurrentUpdate(DbCollection previousDbCollection) => null;
        
    }

    // Query plan abstraction over collections.
    // Supports optimization, reindexing, and execution into a materialized result.
    public abstract class QueryPlan : DbCollection
    {
        // Applies cost-based or rule-based optimizations; default is identity.
        public DbCollection Optimize() => this;

        // Rebuilds or refreshes indexes as needed; default is identity.
        public DbCollection ReIndex() => this;

        // Executes the plan and returns a concrete collection (or null on failure/no-op).
        public DbCollection? Execute() => null;
    }

}
