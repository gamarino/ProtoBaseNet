namespace ProtoBaseNet
{
    /// <summary>
    /// Base abstraction for secondary indexes over collections.
    /// </summary>
    public abstract class Index : Atom
    {
        /// <summary>
        /// Adds the given item to the index structure.
        /// </summary>
        /// <param name="item">The item to add to the index.</param>
        /// <remarks>Implementations must be idempotent or handle duplicates according to their semantics.</remarks>
        public void Add2Index(object? item)
        {
            throw new NotImplementedException();    
        }
        
        /// <summary>
        /// Removes the given item from the index structure.
        /// </summary>
        /// <param name="item">The item to remove from the index.</param>
        /// <remarks>Should not throw if the item is not present (no-op removal preferred).</remarks>
        public void RemoveFromIndex(object? item)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Returns a new, empty index of the same shape and configuration as this instance.
        /// </summary>
        /// <returns>An empty <see cref="Index"/>.</returns>
        public Index GetEmpty()
        {
            throw new NotImplementedException();
        }
    }
    
    /// <summary>
    /// Abstract base class for all persistent collections.
    /// Provides index management hooks and a concurrency-friendly update entry point.
    /// </summary>
    public abstract class DbCollection : Atom
    {
        /// <summary>
        /// Gets the logical item count of the collection.
        /// </summary>
        public int Count { get; } = 0;

        internal DbDictionary<Index>? Indexes { get; set; }

        /// <summary>
        /// Adds or replaces an index for a given attribute.
        /// </summary>
        /// <param name="attributeName">The name of the attribute to index.</param>
        /// <param name="newIndex">The index to add.</param>
        /// <returns>A new collection with the index added.</returns>
        public virtual DbCollection IndexAdd(string attributeName, Index newIndex) => this;

        /// <summary>
        /// Removes an index for a given attribute.
        /// </summary>
        /// <param name="attributeName">The name of the attribute whose index should be removed.</param>
        /// <returns>A new collection with the index removed.</returns>
        public virtual DbCollection IndexRemove(string attributeName) => this;

        /// <summary>
        /// A hook for performing a concurrent update. Given a previous snapshot, returns a reconciled collection.
        /// </summary>
        /// <param name="previousDbCollection">The previous snapshot of the collection.</param>
        /// <returns>A reconciled collection, or null if the operation cannot be applied safely.</returns>
        public DbCollection? ConcurrentUpdate(DbCollection previousDbCollection) => null;
    }

    /// <summary>
    /// Represents a query plan abstraction over collections.
    /// Supports optimization, re-indexing, and execution into a materialized result.
    /// </summary>
    public abstract class QueryPlan : DbCollection
    {
        /// <summary>
        /// Applies cost-based or rule-based optimizations to the query plan.
        /// </summary>
        /// <returns>An optimized <see cref="DbCollection"/>.</returns>
        public DbCollection Optimize() => this;

        /// <summary>
        /// Rebuilds or refreshes indexes as needed for the query.
        /// </summary>
        /// <returns>A <see cref="DbCollection"/> with updated indexes.</returns>
        public DbCollection ReIndex() => this;

        /// <summary>
        /// Executes the query plan and returns a concrete collection.
        /// </summary>
        /// <returns>A <see cref="DbCollection"/> containing the results of the query, or null on failure.</returns>
        public DbCollection? Execute() => null;
    }
}
