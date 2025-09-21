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
    
    
    public abstract class Collections : Atom
    {
        public int Count { get; } = 0;
        internal DbDictionary<Index>? Indexes { get; set; } // Dictionary in python

        public virtual Collections IndexAdd(string attributeName, Index newIndex) => this;
        public virtual Collections IndexRemove(string attributeName) => this;

        public Collections? Optimize() => null;
        public Collections? ReIndex() => null;
        public Collections? Execute(string attributeName) => null;
        
        public Collections? ConcurrentUpdate(Collections previousCollection) => null;
        
    }

}
