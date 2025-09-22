namespace ProtoBaseNet;

using System;
using System.Collections.Generic;

public class DbHashDictionary<T> : DbCollection
{
    private readonly SortedDictionary<int, T> _dictionary;

    public new int Count => _dictionary.Count;

    public DbHashDictionary()
    {
        _dictionary = new SortedDictionary<int, T>();
    }

    public DbHashDictionary(IEnumerable<KeyValuePair<int, T>> items)
    {
        _dictionary = new SortedDictionary<int, T>(items.ToDictionary(kv => kv.Key, kv => kv.Value));
    }

    private DbHashDictionary(SortedDictionary<int, T> dictionary)
    {
        _dictionary = dictionary;
    }

    public T? GetAt(int key)
    {
        _dictionary.TryGetValue(key, out var value);
        return value;
    }

    public bool Has(int key)
    {
        return _dictionary.ContainsKey(key);
    }

    public DbHashDictionary<T> SetAt(int key, T? value)
    {
        var newDictionary = new SortedDictionary<int, T>(_dictionary);
        newDictionary[key] = value!;
        return new DbHashDictionary<T>(newDictionary);
    }

    public DbHashDictionary<T> RemoveAt(int key)
    {
        var newDictionary = new SortedDictionary<int, T>(_dictionary);
        newDictionary.Remove(key);
        return new DbHashDictionary<T>(newDictionary);
    }

    public DbHashDictionary<T> Merge(DbHashDictionary<T> other)
    {
        var newDictionary = new SortedDictionary<int, T>(_dictionary);
        foreach (var (key, value) in other.AsIterable())
        {
            newDictionary[key] = value!;
        }
        return new DbHashDictionary<T>(newDictionary);
    }

    public IEnumerable<(int key, T? value)> AsIterable()
    {
        foreach (var (key, value) in _dictionary)
        {
            yield return (key, value);
        }
    }
}