using System.Collections.Immutable;
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
    private ImmutableDictionary<int, HashCollisionNode<T>> _temporaryContent { get; init; }
    private ImmutableList<KeyValuePair<string, T>> _oplog = ImmutableList<KeyValuePair<string, T>>.Empty;

    /// <summary>
    /// Initializes a new instance of the <see cref="DbSet{T}"/> class.
    /// </summary>
    public DbSet(IEnumerable<T> initialItems) : this()
    {
        var newSet = new DbSet<T>();
        foreach (var item in initialItems)
        {
            newSet = newSet.Add(item);
        }

        Content = newSet.Content;
        _temporaryContent = newSet._temporaryContent;
        _oplog = newSet._oplog;
        Count = newSet.Count;
    }

    public DbSet() : this(null, 0, null, null, null, null, null)
    {
    }

    private DbSet(
        DbHashDictionary<HashCollisionNode<T>>? content = null,
        int count = 0,
        ImmutableDictionary<int, HashCollisionNode<T>>? temporaryContent = null,
        DbDictionary<Index>? indexes = null,
        Guid? stableId = null,
        ObjectTransaction? transaction = null,
        ImmutableList<KeyValuePair<string, T>>? oplog = null) : base(stableId, indexes, transaction)
    {
        if (content != null)
            Content = content;
        else
            Content = new();

        Count = count;

        if (temporaryContent != null)
            _temporaryContent = temporaryContent;
        else
            _temporaryContent = ImmutableDictionary<int, HashCollisionNode<T>>.Empty;
        
        if (oplog != null)
            _oplog = oplog;
        else 
            _oplog = ImmutableList<KeyValuePair<string, T>>.Empty;
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

        if ((key is Atom) && (key as Atom).AtomPointer == null)
        {
            if (!_temporaryContent.ContainsKey(h))
                return false;

            HashCollisionNode<T>? currentNode = _temporaryContent.ElementAt(h).Value;
            while (currentNode != null && !currentNode.Value.Equals(key))
                currentNode = currentNode.Next;

            if (currentNode != null)
                return true;

            return false;
        }
        else
        {
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

            return false;
        }
    }

    /// <summary>
    /// Adds an element to the set.
    /// </summary>
    /// <param name="key">The element to add.</param>
    /// <returns>A new set with the element added, or the same set if the element is already present.</returns>
    public DbSet<T> Add(T key)
    {
        if (Has(key)) return this;

        var newContent = Content;
        var newTemporaryContent = _temporaryContent;
        HashCollisionNode<T> currentListOfValues;
        var hashValue = StableHash(key);

        if (key is Atom valueAtom && valueAtom.AtomPointer == null)
        {
            KeyValuePair<int, HashCollisionNode<T>>? currentNode = _temporaryContent.ElementAt(hashValue);
            if (currentNode != null)
            {
                currentListOfValues = currentNode.Value.Value;
                currentListOfValues = new HashCollisionNode<T>(key, currentListOfValues);
            }
            else
                currentListOfValues = new HashCollisionNode<T>(key, null);

            newTemporaryContent = newTemporaryContent.Add(hashValue, currentListOfValues);
        }
        else
        {
            currentListOfValues = Content.GetAt(hashValue);
            if (currentListOfValues != null)
                currentListOfValues = new HashCollisionNode<T>(key, currentListOfValues);
            else
                currentListOfValues = new HashCollisionNode<T>(key, null);

            newContent = newContent.SetAt(hashValue, currentListOfValues);
        }
        
        var newOplog = _oplog.Add(new KeyValuePair<string, T>("Add", key));

        return new DbSet<T>(newContent, Count + 1, _temporaryContent, Indexes, StableId, Transaction, newOplog);
    }
    

    /// <summary>
    /// Removes an element from the set.
    /// </summary>
    /// <param name="key">The element to remove.</param>
    /// <returns>A new set with the element removed, or the same set if the element is not present.</returns>
    public DbSet<T> RemoveAt(T key)
    {
        if (!Has(key)) return this;

        var newContent = Content;
        var newTemporaryContent = _temporaryContent;
        HashCollisionNode<T>? newFirstNode = null;
        var hashValue = StableHash(key);

        List<HashCollisionNode<T>> nodesToCopy = new List<HashCollisionNode<T>>();
        if ((key is Atom valueAtom) && (valueAtom.AtomPointer == null))
        {
            KeyValuePair<int, HashCollisionNode<T>>? currentElement = _temporaryContent.ElementAt(hashValue);
            HashCollisionNode<T> currentNode = currentElement.Value.Value;
            while (currentNode != null && !(currentNode.Value.Equals(key)))
            {
                nodesToCopy.Add(currentNode);
                currentNode = currentNode.Next;
            }

            if (currentNode != null)
                if (nodesToCopy.Count == 0)
                    newFirstNode = currentNode.Next;
                else
                {
                    int currentIndex = nodesToCopy.Count - 1;
                    var nextNode = currentNode.Next;
                    while (currentIndex >= 0)
                    {
                        var oldNode = nodesToCopy.ElementAt(currentIndex);
                        nextNode = new HashCollisionNode<T>(oldNode.Value, nextNode);
                        currentIndex--;
                    }

                    newFirstNode = nextNode;
                }

            if (newFirstNode != null)
                newTemporaryContent = _temporaryContent.Add(hashValue, newFirstNode);
            else
            {
                newTemporaryContent = _temporaryContent.Remove(hashValue);
            }
        }
        else
        {
            var currentNode = Content.GetAt(hashValue);
            while (currentNode != null && !(currentNode.Value.Equals(key)))
            {
                nodesToCopy.Add(currentNode);
                currentNode = currentNode.Next;
            }

            if (currentNode != null)
                if (nodesToCopy.Count == 0)
                    newFirstNode = currentNode.Next;
                else
                {
                    int currentIndex = nodesToCopy.Count - 1;
                    var nextNode = currentNode.Next;
                    while (currentIndex >= 0)
                    {
                        var oldNode = nodesToCopy.ElementAt(currentIndex);
                        nextNode = new HashCollisionNode<T>(oldNode.Value, nextNode);
                        currentIndex--;
                    }

                    newFirstNode = nextNode;
                }

            if (newFirstNode != null)
                newContent = Content.SetAt(hashValue, newFirstNode);
            else
                newContent = Content.RemoveAt(hashValue);
        }
        
        var newOplog = _oplog.Add(new KeyValuePair<string, T>("RemoveAt", key));
            
        return new DbSet<T>(
            newContent, 
            Count - 1, 
            newTemporaryContent, 
            Indexes, 
            StableId, 
            Transaction, 
            newOplog);
    }
    
    public DbSet<T> ConcurrentUpdate(DbSet<T> currentRoot)
    {
        var newSet = currentRoot;
        foreach (var op in _oplog)
        {
            if (op.Key == "Add")
                newSet = newSet.Add(op.Value);
            else if (op.Key == "Remove")
                newSet = newSet.RemoveAt(op.Value);
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
                if (item.Value is Atom valueAtom)
                    valueAtom.Save();

                var h = StableHash(item);
                if (Content.Has(h))
                {
                    var node = Content.GetAt(h);
                    var newNode = new HashCollisionNode<T>(item.Value.Value, node);
                    Content = Content.SetAt(h, newNode);
                }
                else
                {
                    var newNode = new HashCollisionNode<T>(item.Value.Value);
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
            yield return item.Value.Value;
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

