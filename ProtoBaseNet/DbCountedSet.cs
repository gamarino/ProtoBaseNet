namespace ProtoBaseNet;

using System;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// An immutable multiset (or bag) that stores elements and their occurrence counts.
/// </summary>
/// <typeparam name="T">The type of elements in the set.</typeparam>
/// <remarks>
/// This collection preserves Set-like external semantics:
/// - Iteration yields unique items.
/// - <see cref="UniqueCount"/> reflects the number of unique items.
/// - <see cref="TotalCount"/> reflects the total number of occurrences of all items.
/// </remarks>
public class DbCountedSet<T> : DbCollection, IEnumerable<T>
{
    private readonly DbHashDictionary<T> Values;
    private readonly DbHashDictionary<int> Counters;

    public int UniqueCount => Values.Count;

    public DbCountedSet(List<T> items)
    {
        var newCountedSet = new DbCountedSet<T>();

        foreach (var item in items)
        {
            newCountedSet = newCountedSet.Add(item);       
        }

        Values = newCountedSet.Values;
        Counters = newCountedSet.Counters;
    }

    public DbCountedSet(
        DbHashDictionary<T>? values = null,
        DbHashDictionary<int>? counters = null,
        Guid? collectionId = null,
        ObjectTransaction? transaction = null,
        AtomPointer? atomPointer = null,
        DbDictionary<Index>? indexes = null)
        : base(collectionId, indexes, transaction, atomPointer)
    {
        if (values is null) 
            values = new DbHashDictionary<T>();
        else
            Values = values;
        
        if (counters is null) 
            counters = new DbHashDictionary<int>();
        else
            Counters = counters;
    }

    public IEnumerator<T> GetEnumerator()
    {
        foreach (var value in Values)
            yield return value;
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public bool Has(T key)
    {
        var h = HashOf(key);
        return Counters.Has(h);
    }

    public int GetCount(T key)
    {
        var h = HashOf(key);
        if (Counters.Has(h)) return Counters.GetAt(h);
        return 0;
    }

    public int TotalCount
    {
        get
        {
            var total = 0;
            foreach (var cnt in Counters)
            {
                try { total += Convert.ToInt32(cnt); } catch { /* ignore */ }
            }
            return total;
        }
    }

    public DbCountedSet<T> Add(T key)
    {
        var h = HashOf(key);
        var currentCount = GetCount(key);
        var newItems = Values.SetAt(h, key);
        var newCounts = Counters.SetAt(h, currentCount + 1);
        return new DbCountedSet<T>(newItems, newCounts, StableId, Transaction, AtomPointer, Indexes);
    }

    public DbCountedSet<T> RemoveAt(T key)
    {
        var h = HashOf(key);
        var currentCount = GetCount(key);
        if (currentCount == 0) return this;

        var newItems = Values;
        var newCounts = Counters;

        if (currentCount == 1)
        {
            newItems = newItems.RemoveAt(h);
            newCounts = newCounts.RemoveAt(h);
        }
        else
        {
            newCounts = newCounts.SetAt(h, currentCount - 1);
        }
        
        return new DbCountedSet<T>(newItems, newCounts, StableId, Transaction, AtomPointer, Indexes);
    }

    private int HashOf(T key)
    {
        try
        {
            if (key is Atom atom)
            {
                var ap = atom.AtomPointer;
                if (ap is not null && ap.TransactionId != default)
                    return ap.GetHashCode();
                return atom.GetHashCode();
            }
            else
            {
                return HashCode.Combine(key);
            }
        }
        catch
        {
            return key?.GetHashCode() ?? 0;
        }
    }
}