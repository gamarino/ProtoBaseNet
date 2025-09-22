namespace ProtoBaseNet;

using System;
using System.Collections.Generic;
using System.Linq;

public class DbRepeatedKeysDictionary<T> : DbCollection where T : notnull
{
    public sealed class CountedSet
    {
        private readonly Dictionary<T, int> _counts;

        public int Count => _counts.Count;

        public CountedSet()
        {
            _counts = new Dictionary<T, int>();
        }

        public CountedSet(IEnumerable<KeyValuePair<T, int>> items)
        {
            _counts = new Dictionary<T, int>(items);
        }

        private CountedSet(Dictionary<T, int> counts)
        {
            _counts = counts;
        }

        public bool Has(T value) => _counts.ContainsKey(value);

        public IEnumerable<T> AsIterable()
        {
            foreach (var kv in _counts)
            {
                for (int i = 0; i < kv.Value; i++)
                    yield return kv.Key;
            }
        }

        public CountedSet Add(T value)
        {
            var newCounts = new Dictionary<T, int>(_counts);
            if (newCounts.TryGetValue(value, out var c))
                newCounts[value] = c + 1;
            else
                newCounts[value] = 1;
            return new CountedSet(newCounts);
        }

        public CountedSet Remove(T value)
        {
            var newCounts = new Dictionary<T, int>(_counts);
            if (newCounts.TryGetValue(value, out var c))
            {
                if (c <= 1)
                    newCounts.Remove(value);
                else
                    newCounts[value] = c - 1;
            }
            return new CountedSet(newCounts);
        }

        public bool IsEmpty() => _counts.Count == 0;
    }

    private readonly DbDictionary<CountedSet> _dict;

    public DbRepeatedKeysDictionary()
    {
        _dict = new DbDictionary<CountedSet>();
    }

    private DbRepeatedKeysDictionary(DbDictionary<CountedSet> dict)
    {
        _dict = dict;
    }

    public CountedSet GetAt(string key)
    {
        return _dict.GetAt(key) ?? new CountedSet();
    }

    public DbRepeatedKeysDictionary<T> SetAt(string key, T value)
    {
        var bucket = _dict.GetAt(key) ?? new CountedSet();
        var newBucket = bucket.Add(value);
        var newDict = _dict.SetAt(key, newBucket);
        return new DbRepeatedKeysDictionary<T>(newDict);
    }

    public DbRepeatedKeysDictionary<T> RemoveAt(string key)
    {
        var newDict = _dict.RemoveAt(key);
        return new DbRepeatedKeysDictionary<T>(newDict);
    }

    public DbRepeatedKeysDictionary<T> RemoveRecordAt(string key, T record)
    {
        var bucket = _dict.GetAt(key);
        if (bucket != null && bucket.Has(record))
        {
            var newBucket = bucket.Remove(record);
            var newDict = newBucket.IsEmpty() ? _dict.RemoveAt(key) : _dict.SetAt(key, newBucket);
            return new DbRepeatedKeysDictionary<T>(newDict);
        }
        return this;
    }

    public bool Has(string key) => _dict.Has(key);

    public IEnumerable<(string key, T value)> AsIterable()
    {
        foreach (var (key, bucket) in _dict.AsIterable())
        {
            foreach (var value in bucket.AsIterable())
            {
                yield return ((string)key, value);
            }
        }
    }
}