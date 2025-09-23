using System.Transactions;

namespace ProtoBaseNet;

using System;
using System.Collections;
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
public class DbSet<T> : DbCollection, IEnumerable<T>
{
    private DbHashDictionary<T> Content = new();

    /// <summary>
    /// Gets the total number of elements in the set, including staged and persisted elements.
    /// </summary>
    public int Count => (Content?.Count ?? 0);

    /// <summary>
    /// Initializes a new instance of the <see cref="DbSet{T}"/> class.
    /// </summary>
    public DbSet() { }

    public DbSet(IEnumerable<T> items)
    {
        var newSet = new DbSet<T>();
        foreach (var item in items)
            newSet = newSet.Add(item);
        
        Content = newSet.Content;
    }

    private DbSet(
        DbHashDictionary<T> content, 
        DbDictionary<Index>? indexes,
        Guid stableId,
        ObjectTransaction? transaction) : base(stableId, indexes, transaction)
    {
        Content = content;
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
        return Content.Has(h);
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
        var newContent = Content.SetAt(h, key);
        return new DbSet<T>(newContent, Indexes, StableId, Transaction);
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

        var newContent = Content;

        if (newContent.Has(h))
        {
            newContent = newContent.RemoveAt(h);
            var newIndexes = ObjectRemoveFromIndexes(key);
            return new DbSet<T>(newContent, newIndexes, StableId, Transaction);
        }
        else
            return this;
    }

    /// <summary>
    /// Promotes staged elements to the persisted content.
    /// </summary>
    internal override void Save()
    {
        Content.Save();
        Indexes?.Save();
        base.Save();
    }

    /// <summary>
    /// Produces the union of this set and another set.
    /// </summary>
    /// <param name="other">The set to perform the union with.</param>
    /// <returns>A new set that contains all elements present in either set.</returns>
    public DbSet<T> Union(DbSet<T> other)
    {
        var result = this;
        foreach (var item in other)
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
        foreach (var item in other)
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
        foreach (var item in other)
        {
            if (!other.Has(item))
                result = result.Add(item);
        }
        return result;
    }
    
    public IEnumerator<T> GetEnumerator()
    {
        return Content.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

