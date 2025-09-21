namespace ProtoBaseNet
{
    public abstract class Index : Atom
    {
        public void Add2Index(object? item)
        {
            throw new NotImplementedException();    
        }
        
        public void RemoveFromIndex(object? item)
        {
            throw new NotImplementedException();
        }

        public Index GetEmpty()
        {
            throw new NotImplementedException();
        }
        
    }
    
    
    public abstract class DbCollection : Atom
    {
        public int Count { get; } = 0;
        internal DbDictionary<Index>? Indexes { get; set; } // Dictionary in python

        public virtual DbCollection IndexAdd(string attributeName, Index newIndex) => this;
        public virtual DbCollection IndexRemove(string attributeName) => this;

        public DbCollection? ConcurrentUpdate(DbCollection previousDbCollection) => null;
        
    }

    public abstract class QueryPlan : DbCollection
    {
        public DbCollection Optimize() => this;
        public DbCollection ReIndex() => this;
        public DbCollection? Execute() => null;
    }

}
