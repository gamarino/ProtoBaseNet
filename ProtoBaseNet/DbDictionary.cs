namespace ProtoBaseNet;

public class DbDictionary<T> : DbCollection
{
    public DbDictionary<T> SetAt(string key, T value)
    {
        return this;
    }
}