namespace ProtoBaseNet;

using System;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// An immutable, persistent hash dictionary implemented as a balanced binary search tree (AVL-like) over integer keys.
/// </summary>
/// <typeparam name="T">The type of values in the dictionary.</typeparam>
/// <remarks>
/// Key properties:
/// - Structural sharing: Mutation methods (<see cref="SetAt"/>, <see cref="RemoveAt"/>, <see cref="Merge"/>) return new trees, reusing unchanged subtrees.
/// - Balanced height: Rotations keep operations O(log N) for lookup, insert, and remove.
/// - In-order traversal via <see cref="AsIterable"/> yields entries sorted by integer key.
/// </remarks>
public class DbHashDictionary<T> : DbCollection, IEnumerable<T>
{
    /// <summary>
    /// Gets the key of the node. Null for an empty/sentinel node.
    /// </summary>
    public int? Key { get; init; }

    /// <summary>
    /// Gets the value of the node. Only meaningful if Key is not null.
    /// </summary>
    public T? Value { get; init; }

    /// <summary>
    /// Gets the cached height of the subtree.
    /// </summary>
    public int Height { get; init; }

    /// <summary>
    /// Gets the right child (keys greater than the current key).
    /// </summary>
    public DbHashDictionary<T>? Next { get; init; }

    /// <summary>
    /// Gets the left child (keys less than the current key).
    /// </summary>
    public DbHashDictionary<T>? Previous { get; init; }

    /// <summary>
    /// Gets the cached number of nodes in the subtree.
    /// </summary>
    public new int Count { get; init; }

    /// <summary>
    /// Initializes a new instance of the <see cref="DbHashDictionary{T}"/> class.
    /// </summary>
    public DbHashDictionary(
        int? key = null,
        T? value = default,
        DbHashDictionary<T>? next = null,
        DbHashDictionary<T>? previous = null)
    {
        Key = key;
        Value = value;
        Next = next;
        Previous = previous;

        if (key is not null)
        {
            var count = 1;
            if (Previous is not null) count += Previous.Count;
            if (Next is not null) count += Next.Count;
            Count = count;
        }
        else
        {
            Count = 0;
        }

        if (key is not null)
        {
            var hL = Previous?.Height ?? 0;
            var hR = Next?.Height ?? 0;
            Height = 1 + Math.Max(hL, hR);
        }
        else
        {
            Height = 0;
        }
    }

    /// <summary>
    /// Returns an enumerable that iterates through the dictionary in key-sorted order.
    /// </summary>
    /// <returns>An enumerable of key-value pairs.</returns>
    public IEnumerable<(int key, T? value)> AsIterable()
    {
        foreach (var kv in Scan(this))
            yield return kv;

        static IEnumerable<(int, T?)> Scan(DbHashDictionary<T> node)
        {
            if (node.Previous is not null)
            {
                foreach (var x in Scan(node.Previous))
                    yield return x;
            }
            if (node.Key is int k)
                yield return (k, node.Value);
            if (node.Next is not null)
            {
                foreach (var x in Scan(node.Next))
                    yield return x;
            }
        }
    }

    /// <summary>
    /// Gets the value associated with the specified key.
    /// </summary>
    /// <param name="key">The key of the value to get.</param>
    /// <returns>The value associated with the key, or default(T) if the key is not found.</returns>
    public T? GetAt(int key)
    {
        if (Key is null) return default;

        var node = this;
        while (node is not null)
        {
            if (node.Key == key)
                return node.Value;

            node = key > node.Key ? node.Next : node.Previous;
        }
        return default;
    }

    /// <summary>
    /// Determines whether the dictionary contains the specified key.
    /// </summary>
    /// <param name="key">The key to locate.</param>
    /// <returns>True if the dictionary contains the key; otherwise, false.</returns>
    public bool Has(int key)
    {
        if (Key is null) return false;

        var node = this;
        while (node is not null)
        {
            if (node.Key == key)
                return true;

            node = key > node.Key ? node.Next : node.Previous;
        }
        return false;
    }

    private int BalanceFactor()
    {
        if (Key is null) return 0;
        var hL = Previous?.Height ?? 0;
        var hR = Next?.Height ?? 0;
        return hR - hL;
    }

    private DbHashDictionary<T> RightRotation()
    {
        if (Previous is null) return this;

        var newRight = new DbHashDictionary<T>(
            key: Key,
            value: Value,
            previous: Previous.Next,
            next: Next
        );

        return new DbHashDictionary<T>(
            key: Previous.Key,
            value: Previous.Value,
            previous: Previous.Previous,
            next: newRight
        );
    }

    private DbHashDictionary<T> LeftRotation()
    {
        if (Next is null) return this;

        var newLeft = new DbHashDictionary<T>(
            key: Key,
            value: Value,
            previous: Previous,
            next: Next.Previous
        );

        return new DbHashDictionary<T>(
            key: Next.Key,
            value: Next.Value,
            previous: newLeft,
            next: Next.Next
        );
    }

    private DbHashDictionary<T> Rebalance()
    {
        var node = this;

        while (node.Previous is not null && !(node.Previous.BalanceFactor() is >= -1 and <= 1))
        {
            node = new DbHashDictionary<T>(
                key: node.Key,
                value: node.Value,
                previous: node.Previous.Rebalance(),
                next: node.Next
            );
        }

        while (node.Next is not null && !(node.Next.BalanceFactor() is >= -1 and <= 1))
        {
            node = new DbHashDictionary<T>(
                key: node.Key,
                value: node.Value,
                previous: node.Previous,
                next: node.Next.Rebalance()
            );
        }

        var balance = node.BalanceFactor();

        if (balance < -1)
        {
            if (node.Previous is not null && node.Previous.BalanceFactor() > 0)
            {
                node = new DbHashDictionary<T>(
                    key: node.Key,
                    value: node.Value,
                    previous: node.Previous.LeftRotation(),
                    next: node.Next
                );
            }
            return node.RightRotation();
        }

        if (balance > 1)
        {
            if (node.Next is not null && node.Next.BalanceFactor() < 0)
            {
                node = new DbHashDictionary<T>(
                    key: node.Key,
                    value: node.Value,
                    previous: node.Previous,
                    next: node.Next.RightRotation()
                );
            }
            return node.LeftRotation();
        }

        return node;
    }

    /// <summary>
    /// Returns a new dictionary with the specified key and value set.
    /// If the key already exists, its value is replaced.
    /// </summary>
    /// <param name="key">The key of the element to set.</param>
    /// <param name="value">The value to associate with the key.</param>
    /// <returns>A new, rebalanced dictionary with the key-value pair set.</returns>
    public DbHashDictionary<T> SetAt(int key, T? value)
    {
        if (Key is null)
        {
            return new DbHashDictionary<T>(
                key: key,
                value: value
            );
        }

        var cmp = key - Key.Value;
        DbHashDictionary<T> newNode;

        if (cmp > 0)
        {
            if (Next is not null)
            {
                newNode = new DbHashDictionary<T>(
                    key: Key,
                    value: Value,
                    previous: Previous,
                    next: Next.SetAt(key, value)
                );
            }
            else
            {
                newNode = new DbHashDictionary<T>(
                    key: Key,
                    value: Value,
                    previous: Previous,
                    next: new DbHashDictionary<T>(key: key, value: value)
                );
            }
        }
        else if (cmp < 0)
        {
            if (Previous is not null)
            {
                newNode = new DbHashDictionary<T>(
                    key: Key,
                    value: Value,
                    previous: Previous.SetAt(key, value),
                    next: Next
                );
            }
            else
            {
                newNode = new DbHashDictionary<T>(
                    key: Key,
                    value: Value,
                    previous: new DbHashDictionary<T>(key: key, value: value),
                    next: Next
                );
            }
        }
        else
        {
            newNode = new DbHashDictionary<T>(
                key: Key,
                value: value,
                previous: Previous,
                next: Next
            );
        }

        return newNode.Rebalance();
    }

    /// <summary>
    /// Returns a new dictionary with the element with the specified key removed.
    /// </summary>
    /// <param name="key">The key of the element to remove.</param>
    /// <returns>A new, rebalanced dictionary with the specified element removed.</returns>
    public DbHashDictionary<T> RemoveAt(int key)
    {
        if (Key is null) return this;

        var cmp = key - Key.Value;
        DbHashDictionary<T> newNode;

        if (cmp > 0)
        {
            if (Next is not null)
            {
                var newNext = Next.RemoveAt(key);
                newNode = new DbHashDictionary<T>(
                    key: Key,
                    value: Value,
                    previous: Previous,
                    next: (newNext is { Key: not null }) ? newNext : null
                );
            }
            else
            {
                if (Previous is not null) { }
                return Previous ?? this;
            }
        }
        else if (cmp < 0)
        {
            if (Previous is not null)
            {
                var newPrev = Previous.RemoveAt(key);
                newNode = new DbHashDictionary<T>(
                    key: Key,
                    value: Value,
                    previous: (newPrev is { Key: not null }) ? newPrev : null,
                    next: Next
                );
            }
            else
            {
                if (Next is not null) { }
                return Next ?? this;
            }
        }
        else
        {
            if (Next is not null)
            {
                var first = Next.GetFirst();
                if (first is null)
                {
                    return Previous ?? new DbHashDictionary<T>();
                }
                var (nk, nv) = first.Value;
                var newNext = Next.RemoveAt(nk);
                newNode = new DbHashDictionary<T>(
                    key: nk,
                    value: nv,
                    previous: Previous,
                    next: (newNext is { Key: not null }) ? newNext : null
                );
            }
            else if (Previous is not null)
            {
                var last = Previous.GetLast();
                if (last is null)
                {
                    return Next ?? new DbHashDictionary<T>();
                }
                var (pk, pv) = last.Value;
                var newPrev = Previous.RemoveAt(pk);
                newNode = new DbHashDictionary<T>(
                    key: pk,
                    value: pv,
                    previous: (newPrev is { Key: not null }) ? newPrev : null,
                    next: Next
                );
            }
            else
            {
                return new DbHashDictionary<T>();
            }
        }

        return newNode.Rebalance();
    }

    /// <summary>
    /// Merges another dictionary into this one.
    /// </summary>
    /// <param name="other">The dictionary to merge.</param>
    /// <returns>A new dictionary containing all key-value pairs from both dictionaries. Values from the other dictionary overwrite existing ones.</returns>
    public DbHashDictionary<T> Merge(DbHashDictionary<T> other)
    {
        var result = this;
        foreach (var (k, v) in other.AsIterable())
            result = result.SetAt(k, v);
        return result;
    }

    /// <summary>
    /// Gets the smallest (leftmost) key-value pair in the dictionary.
    /// </summary>
    /// <returns>The first key-value pair, or null if the dictionary is empty.</returns>
    public (int key, T? value)? GetFirst()
    {
        if (Key is null) return null;
        var node = this;
        while (node is not null)
        {
            if (node.Previous is null)
                return (node.Key!.Value, node.Value);
            node = node.Previous;
        }
        throw new ProtoCorruptionException("get_first traversal inconsistency");
    }

    /// <summary>
    /// Gets the largest (rightmost) key-value pair in the dictionary.
    /// </summary>
    /// <returns>The last key-value pair, or null if the dictionary is empty.</returns>
    public (int key, T? value)? GetLast()
    {
        if (Key is null) return null;
        var node = this;
        while (node is not null)
        {
            if (node.Next is null)
                return (node.Key!.Value, node.Value);
            node = node.Next;
        }
        throw new ProtoCorruptionException("get_last traversal inconsistency");
    }

    public IEnumerator<T> GetEnumerator()
    {
        if (Key is null) yield break;
        
        if (Previous is not null)
            foreach (var item in Previous)
                yield return item;

        if (Key is not null)
            yield return Value;

        if (Next is not null)
            foreach (var item in Next)
                yield return item;
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}