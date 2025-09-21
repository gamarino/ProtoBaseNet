namespace ProtoBaseNet;

using System;
using System.Collections.Generic;

public class DbHashDictionary<T> : DbCollection
{
    public int? Key { get; init; }
    public T? Value { get; init; }
    public int Height { get; init; }
    public DbHashDictionary<T>? Next { get; init; }
    public DbHashDictionary<T>? Previous { get; init; }

    public int Count { get; init; }

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

    public DbHashDictionary<T> Merge(DbHashDictionary<T> other)
    {
        var result = this;
        foreach (var (k, v) in other.AsIterable())
            result = result.SetAt(k, v);
        return result;
    }

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
}