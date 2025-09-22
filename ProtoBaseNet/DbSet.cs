namespace ProtoBaseNet;

using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

/// <summary>
/// An immutable, persistent-style set with transactional staging.
/// </summary>
/// <typeparam name="T">The type of elements in the set.</typeparam>
/// <remarks>
/// Key ideas:
/// - Structural sharing: <see cref="Add"/> and <see cref="RemoveAt"/> return new <see cref="DbSet{T}"/> instances, reusing existing dictionaries.
/// - Two-phase storage: New elements are staged and promoted to persisted content on <see cref="Save"/>.
/// - Stable hashing: Membership is determined by a deterministic hash, ensuring consistent identity.
/// </remarks>
public class DbSet<T> : DbCollection
{
    private DbDictionary<T> _content = new();
    private DbDictionary<T> _newObjects = new();
    private DbDictionary<object>? _indexes = new();

    /// <summary>
    /// Gets the total number of elements in the set, including staged and persisted elements.
    /// </summary>
    public int Count => (_content?.Count ?? 0) + (_newObjects?.Count ?? 0);

    /// <summary>
    /// Initializes a new instance of the <see cref="DbSet{T}"/> class.
    /// </summary>
    public DbSet() { }

    public DbSet(IEnumerable<T> items)
    {
        _content = new DbDictionary<T>();
        _newObjects = new DbDictionary<T>();
        _indexes = new DbDictionary<object>();

        foreach (var item in items)
        {
            var h = StableHash(item!);
            _newObjects = _newObjects.SetAt(h, item);
        }
    }

    private DbSet(DbDictionary<T> content, DbDictionary<T> newObjects, DbDictionary<object>? indexes)
    {
        _content = content;
        _newObjects = newObjects;
        _indexes = indexes;
    }

    private static int StableHash(object? key)
    {
        if (key is null) return 0;

        try
        {
            if (key is string s)
            {
                using var sha = SHA256.Create();
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(s));
                return BitConverter.ToInt32(bytes, 0);
            }

            if (key is bool b)
            {
                using var sha = SHA256.Create();
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes($"bool:{(b ? 1 : 0)}"));
                return BitConverter.ToInt32(bytes, 0);
            }

            if (key is sbyte or byte or short or ushort or int or uint or long or ulong or nint or nuint or float or double or decimal)
            {
                using var sha = SHA256.Create();
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes($"{key.GetType().Name}:{key}"));
                return BitConverter.ToInt32(bytes, 0);
            }

            using (var sha2 = SHA256.Create())
            {
                var repr = $"{key.GetType().Name}:{key}";
                var bytes = sha2.ComputeHash(Encoding.UTF8.GetBytes(repr));
                return BitConverter.ToInt32(bytes, 0);
            }
        }
        catch
        {
            return key.GetHashCode();
        }
    }

    /// <summary>
    /// Checks if the set contains the specified element.
    /// </summary>
    /// <param name="key">The element to check for.</param>
    /// <returns>True if the element is in the set, otherwise false.</returns>
    public bool Has(T key)
    {
        var h = StableHash(key!);
        return _newObjects.Has(h) || _content.Has(h);
    }

    /// <summary>
    /// Adds an element to the set.
    /// </summary>
    /// <param name="key">The element to add.</param>
    /// <returns>A new set with the element added, or the same set if the element is already present.</returns>
    public DbSet<T> Add(T key)
    {
        if (Has(key)) return this;

        var h = StableHash(key!);
        var newNew = new DbDictionary<T>().ExtendFrom(_newObjects).SetAt(h, key);
        var newIndexes = _indexes; // Hook for index updates on first appearance
        return new DbSet<T>(_content, newNew, newIndexes);
    }

    /// <summary>
    /// Removes an element from the set.
    /// </summary>
    /// <param name="key">The element to remove.</param>
    /// <returns>A new set with the element removed, or the same set if the element is not present.</returns>
    public DbSet<T> RemoveAt(T key)
    {
        var h = StableHash(key!);
        if (!Has(key)) return this;

        var newNew = _newObjects;
        var newCont = _content;

        if (_newObjects.Has(h))
        {
            newNew = _newObjects.RemoveAt(h);
        }
        else
        {
            newCont = _content.RemoveAt(h);
        }

        var newIndexes = _indexes; // Hook for index updates on last removal
        return new DbSet<T>(newCont, newNew, newIndexes);
    }

    /// <summary>
    /// Promotes staged elements to the persisted content.
    /// </summary>
    internal override void Save()
    {
        foreach (var (k, v) in _newObjects.AsIterable())
        {
            _content = _content.SetAt(k, v);
        }
        _newObjects = new DbDictionary<T>();
    }

    /// <summary>
    /// Returns an enumerable that iterates through the elements in the set.
    /// </summary>
    /// <returns>An <see cref="IEnumerable{T}"/> for the set.</returns>
    public IEnumerable<T> AsIterable()
    {
        foreach (var (_, v) in _newObjects.AsIterable())
            yield return v;
        foreach (var (_, v) in _content.AsIterable())
            yield return v;
    }

    /// <summary>
    /// Produces the union of this set and another set.
    /// </summary>
    /// <param name="other">The set to perform the union with.</param>
    /// <returns>A new set that contains all elements present in either set.</returns>
    public DbSet<T> Union(DbSet<T> other)
    {
        var result = this;
        foreach (var item in other.AsIterable())
            result = result.Add(item);
        return result;
    }

    /// <summary>
    /// Produces the intersection of this set and another set.
    /// </summary>
    /// <param name="other">The set to perform the intersection with.</param>
    /// <returns>A new set that contains only the elements present in both sets.</returns>
    public DbSet<T> Intersection(DbSet<T> other)
    {
        var result = new DbSet<T>();
        foreach (var item in AsIterable())
        {
            if (other.Has(item))
                result = result.Add(item);
        }
        return result;
    }

    /// <summary>
    /// Produces the set difference of this set and another set.
    /// </summary>
    /// <param name="other">The set to subtract.</param>
    /// <returns>A new set that contains the elements from this set that are not in the other set.</returns>
    public DbSet<T> Difference(DbSet<T> other)
    {
        var result = new DbSet<T>();
        foreach (var item in AsIterable())
        {
            if (!other.Has(item))
                result = result.Add(item);
        }
        return result;
    }
}

file static class DbDictionaryExtensions
{
    public static DbDictionary<T> ExtendFrom<T>(this DbDictionary<T> target, DbDictionary<T> source)
    {
        foreach (var (k, v) in source.AsIterable())
            target = target.SetAt(k, v);
        return target;
    }

    public static IEnumerable<(int key, T value)> AsIterable<T>(this DbDictionary<T> dict)
    {
        foreach (var kv in dict)
        {
            var keyObj = kv.Key;
            int key = keyObj is int i ? i : (keyObj is IConvertible ? Convert.ToInt32(keyObj) : keyObj.GetHashCode());
            yield return (key, kv.Value);
        }
    }

    public static bool Has<T>(this DbDictionary<T> dict, int key)
        => dict.GetAt(key) is not null;

    public static DbDictionary<T> RemoveAt<T>(this DbDictionary<T> dict, int key)
        => dict.RemoveAt(key);

    public static DbDictionary<T> SetAt<T>(this DbDictionary<T> dict, int key, T value)
        => dict.SetAt(key, value);
}