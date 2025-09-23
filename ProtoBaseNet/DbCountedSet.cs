namespace ProtoBaseNet;

using System;
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
public class DbCountedSet<T> : DbCollection
{
    private readonly DbDictionary<T> Values;
    private readonly DbDictionary<int> Counters;

    public int UniqueCount => Values.Count;

    public DbCountedSet()
    {
        Values = new DbDictionary<T>();
        Counters = new DbDictionary<int>();
    }

    public DbCountedSet(
        DbDictionary<T> values,
        DbDictionary<int> counters,
        Guid? collectionId = null,
        ObjectTransaction? transaction = null,
        AtomPointer? atomPointer = null,
        DbDictionary<Index>? indexes = null)
        : base(collectionId, indexes, transaction, atomPointer)
    {
        Values = values;
        Counters = counters;
        Indexes = indexes;
    }

    public IEnumerable<T> AsIterable()
    {
        foreach (var (_, value) in Values.AsIterable())
            yield return value;
    }

    public IEnumerator<T> GetEnumerator() => AsIterable().GetEnumerator();

    public bool Has(T key)
    {
        var h = HashOf(key);
        return Counters.Has(h);
    }

    public int GetCount(T key)
    {
        var h = HashOf(key);
        if (Counters.TryGetValue(h, out var count)) return count;
        return 0;
    }

    public int TotalCount
    {
        get
        {
            var total = 0;
            foreach (var (_, cnt) in Counters.AsIterable())
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

    private object HashOf(T key)
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

            if (key is string s)
            {
                using var sha = System.Security.Cryptography.SHA256.Create();
                var bytes = System.Text.Encoding.UTF8.GetBytes(s);
                var hash = sha.ComputeHash(bytes);
                return BitConverter.ToString(hash);
            }

            if (key is IFormattable || key is bool)
            {
                var typed = $"{key.GetType().Name}:{key}";
                using var sha = System.Security.Cryptography.SHA256.Create();
                var bytes = System.Text.Encoding.UTF8.GetBytes(typed);
                var hash = sha.ComputeHash(bytes);
                return BitConverter.ToString(hash);
            }

            var repr = $"{key?.GetType().Name}:{key}";
            using (var sha2 = System.Security.Cryptography.SHA256.Create())
            {
                var bytes2 = System.Text.Encoding.UTF8.GetBytes(repr);
                var hash2 = sha2.ComputeHash(bytes2);
                return BitConverter.ToString(hash2);
            }
        }
        catch
        {
            return key?.GetHashCode() ?? 0;
        }
    }
}