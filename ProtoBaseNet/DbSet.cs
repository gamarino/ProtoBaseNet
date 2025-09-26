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
    private DbHashDictionary<HashCollisionNode<T>> Content { get; set; }
    private HashSet<T> _temporaryContent { get; set; }
    private List<Func<DbSet<T>, DbSet<T>>> _oplog;

    /// <summary>
    /// Gets the total number of elements in the set, including staged and persisted elements.
    /// </summary>
    public int Count => (Content?.Count ?? 0);

    /// <summary>
    /// Initializes a new instance of the <see cref="DbSet{T}"/> class.
    /// </summary>
    public DbSet(IEnumerable<T> initialItems) : this()
    {
        var newSet = this;
        foreach (var item in initialItems)
        {
            newSet = newSet.Add(item);
        }

        Content = newSet.Content;
        _temporaryContent = newSet._temporaryContent;
        _oplog = newSet._oplog;
    }

    public DbSet() : this(null, null, null, null, null, null)
    {
    }

    private DbSet(
        DbHashDictionary<HashCollisionNode<T>>? content = null, 
        HashSet<T>? temporaryContent = null,
        DbDictionary<Index>? indexes = null,
        Guid? stableId = null,
        ObjectTransaction? transaction = null,
        List<Func<DbSet<T>, DbSet<T>>>? oplog = null) : base(stableId, indexes, transaction)
    {
        if (content != null)
            Content = content;
        else
            Content = new();

        if (temporaryContent != null)
            _temporaryContent = temporaryContent;
        else
            _temporaryContent = new HashSet<T>();
        
        _oplog = oplog ?? new List<Func<DbSet<T>, DbSet<T>>>();
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

        if (Content.Has(h))
        {
            var node = Content.GetAt(h);
            while (node != null)
            {
                if (node.Value.Equals(key))
                    return true;
                node = node.Next;
            }
        }

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

        var newOplog = new List<Func<DbSet<T>, DbSet<T>>>(_oplog) { (s) => s.AddImpl(key) };

        return new DbSet<T>(Content, _temporaryContent, Indexes, StableId, Transaction, newOplog).AddImpl(key);
    }
    
    private DbSet<T> AddImpl(T key)
    {
        var newIndexes = ObjectAddToIndexes(key);
        
        if (key is Atom && (key as Atom).Transaction == null)
        {
            if (_temporaryContent.Contains(key))
                return this;
            var newTemporaryContent = new HashSet<T>(_temporaryContent);
            newTemporaryContent.Add(key);
            return new DbSet<T>(Content, newTemporaryContent, newIndexes, StableId, Transaction, _oplog);
        }

        var h = StableHash(key);
        var newContent = Content;

        if (Content.Has(h))
        {
            var node = Content.GetAt(h);
            var newNode = new HashCollisionNode<T>(key, node);
            newContent = Content.SetAt(h, newNode);
        }
        else
        {
            var newNode = new HashCollisionNode<T>(key);
            newContent = Content.SetAt(h, newNode);
        }

        return new DbSet<T>(newContent, _temporaryContent, newIndexes, StableId, Transaction, _oplog);
    }

    /// <summary>
    /// Removes an element from the set.
    /// </summary>
    /// <param name="key">The element to remove.</param>
    /// <returns>A new set with the element removed, or the same set if the element is not present.</returns>
    public DbSet<T> RemoveAt(T key)
    {
        if (!Has(key)) return this;

        var newOplog = new List<Func<DbSet<T>, DbSet<T>>>(_oplog) { (s) => s.RemoveAtImpl(key) };

        return new DbSet<T>(Content, _temporaryContent, Indexes, StableId, Transaction, newOplog).RemoveAtImpl(key);
    }
    
    private DbSet<T> RemoveAtImpl(T key)
    {
        var h = StableHash(key!);
        
        var newIndexes = ObjectRemoveFromIndexes(key);
        
        if (key is Atom && (key as Atom).Transaction == null)
        {
            if (!_temporaryContent.Contains(key))
                return this;
            var newTemporaryContent = new HashSet<T>(_temporaryContent);
            newTemporaryContent.Remove(key);
            return new DbSet<T>(Content, newTemporaryContent, newIndexes, StableId, Transaction, _oplog);
        }

        var newContent = Content;

        if (newContent.Has(h))
        {
            var node = newContent.GetAt(h);
            var newFirstNode = RemoveFromChain(node, key);
            if (newFirstNode == null)
                newContent = newContent.RemoveAt(h);
            else
                newContent = newContent.SetAt(h, newFirstNode);
            
            return new DbSet<T>(newContent, _temporaryContent, newIndexes, StableId, Transaction, _oplog);
        }
        else
            return this;
    }

    private HashCollisionNode<T>? RemoveFromChain(HashCollisionNode<T> firstNode, T key)
    {
        if (firstNode.Value.Equals(key))
            return firstNode.Next;

        var currentNode = firstNode;
        while (currentNode.Next != null)
        {
            if (currentNode.Next.Value.Equals(key))
            {
                currentNode = new HashCollisionNode<T>(currentNode.Value, currentNode.Next.Next);
                return firstNode;
            }
            currentNode = currentNode.Next;
        }

        return firstNode;
    }

    public DbSet<T> ConcurrentUpdate(DbSet<T> currentRoot)
    {
        var newSet = currentRoot;
        foreach (var op in _oplog)
        {
            newSet = op(newSet);
        }
        return newSet;
    }
    
    /// <summary>
    /// Promotes staged elements to the persisted content.
    /// </summary>
    public override void Save(ObjectTransaction? transaction = null)
    {
        if (AtomPointer == null)
        {
            foreach (var item in _temporaryContent)
            {
                if (item is Atom valueAtom)
                    valueAtom.Save();

                var h = StableHash(item);
                if (Content.Has(h))
                {
                    var node = Content.GetAt(h);
                    var newNode = new HashCollisionNode<T>(item, node);
                    Content = Content.SetAt(h, newNode);
                }
                else
                {
                    var newNode = new HashCollisionNode<T>(item);
                    Content = Content.SetAt(h, newNode);
                }
            }

            _temporaryContent.Clear();
            
            base.Save(transaction);
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
        foreach (var node in Content)
        {
            var currentNode = node;
            while (currentNode != null)
            {
                yield return currentNode.Value;
                currentNode = currentNode.Next;
            }
        }

        foreach (var item in _temporaryContent)
            yield return item;
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

