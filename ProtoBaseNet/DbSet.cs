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
    private DbHashDictionary<T> Content { get; set; }
    private HashSet<T> _temporaryContent { get; init; }

    /// <summary>
    /// Gets the total number of elements in the set, including staged and persisted elements.
    /// </summary>
    public int Count => (Content?.Count ?? 0);

    /// <summary>
    /// Initializes a new instance of the <see cref="DbSet{T}"/> class.
    /// </summary>
    public DbSet(IEnumerable<T> initialItems)
    {
        var newSet = new DbSet<T>();
        foreach (var item in initialItems)
        {
            newSet = newSet.Add(item);
        }

        Content = newSet.Content;
        _temporaryContent = newSet._temporaryContent;
    }

    private DbSet(
        DbHashDictionary<T>? content = null, 
        HashSet<T>? temporaryContent = null,
        DbDictionary<Index>? indexes = null,
        Guid? stableId = null,
        ObjectTransaction? transaction = null) : base(stableId, indexes, transaction)
    {
        if (content != null)
            Content = content;
        else
            Content = new();

        if (temporaryContent != null)
            _temporaryContent = temporaryContent;
        else
            _temporaryContent = new HashSet<T>();
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
        if (key is null) return false;
        var h = StableHash(key!);
        if (Content.Has(h)) return true;
        if ((key is Atom) && (key as Atom).Transaction == null)
            return _temporaryContent.Contains(key);
        return false;
    }

    /// <summary>
    /// Adds an element to the set.
    /// </summary>
    /// <param name="key">The element to add.</param>
    /// <returns>A new set with the element added, or the same set if the element is already present.</returns>
    public DbSet<T> Add(T key)
    {
        if (Has(key)) return this;
        
        if (key is Atom && (key as Atom).Transaction == null)
        {
            if (_temporaryContent.Contains(key))
                return this;
            var newTemporaryContent = _temporaryContent;
            newTemporaryContent.Add(key);
            return new DbSet<T>(Content, newTemporaryContent, Indexes, StableId, Transaction);
        }

        var h = StableHash(key);
        var newContent = Content.SetAt(h, key);
        return new DbSet<T>(newContent, _temporaryContent, Indexes, StableId, Transaction);
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
        if (key is Atom && (key as Atom).Transaction == null)
        {
            if (!_temporaryContent.Contains(key))
                return this;
            var newTemporaryContent = _temporaryContent;
            newTemporaryContent.Remove(key);
            return new DbSet<T>(Content, newTemporaryContent, Indexes, StableId, Transaction);
        }

        var newContent = Content;

        if (newContent.Has(h))
        {
            newContent = newContent.RemoveAt(h);
            var newIndexes = ObjectRemoveFromIndexes(key);
            return new DbSet<T>(newContent, _temporaryContent, newIndexes, StableId, Transaction);
        }
        else
            return this;
    }

    /// <summary>
    /// Promotes staged elements to the persisted content.
    /// </summary>
    internal override void Save()
    {
        if (AtomPointer == null)
        {
            foreach (var item in _temporaryContent)
                Content = Content.SetAt(StableHash(item), item);
            _temporaryContent.Clear();
            
            Content.Save();
            Indexes?.Save();
            base.Save();
        }
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
        foreach (var item in Content)
            yield return item;
        foreach (var item in _temporaryContent)
            yield return item;
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

