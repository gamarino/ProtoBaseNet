namespace ProtoBaseNet
{
    public abstract class DBCollections : Atom
    {
        public int Count { get; set; } = 0;
        public object Indexes { get; set; } // Dictionary in python

        public DBCollections(object indexes = null, ObjectTransaction transaction = null,
            AtomPointer atomPointer = null)
            : base(transaction, atomPointer)
        {
            Indexes = indexes;
        }

        public virtual DBCollections IndexAdd(object item) => this;
        public virtual DBCollections IndexRemove(object item) => this;

        public void Add2Indexes(object item)
        {
        }

        public void RemoveFromIndexes(object item)
        {
        }

        public abstract QueryPlan AsQueryPlan();
    }

    public abstract class QueryPlan : Atom
    {
        public QueryPlan BasedOn { get; set; }

        protected QueryPlan(QueryPlan basedOn = null, ObjectTransaction transaction = null,
            AtomPointer atomPointer = null)
            : base(transaction, atomPointer)
        {
            BasedOn = basedOn;
        }

        public abstract DBCollections Execute();
        public abstract QueryPlan Optimize();
        public virtual int GetCardinalityEstimate() => int.MaxValue;
        public virtual float GetCostEstimate() => float.PositiveInfinity;
        public virtual int Count() => 0;
        public virtual object Explain() => new { };
    }
}