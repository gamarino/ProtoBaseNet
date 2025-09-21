namespace ProtoBaseNet;

using System;
using System.Collections.Generic;

// A multiset that preserves Set-like external semantics:
// - Iteration yields unique items (no duplicates)
// - Count reflects the number of unique items
// - TotalCount reflects total number of occurrences
// Index update semantics:
// - On first insertion (0 -> 1): update indexes (add)
// - On last removal (1 -> 0): update indexes (remove)
// - Intermediate increments/decrements do not touch indexes
public class DbCountedSet<T> : DbCollection
{
    // Unique items by a stable hash identity
    private readonly DbDictionary<T> _items;
    // Occurrence counters keyed by the same hash identity
    private readonly DbDictionary<int> _counts;
    // Staged (not yet persisted) unique items in the current transaction
    private readonly DbDictionary<T> _newObjects;
    // Staged counts for the same keys
    private readonly DbDictionary<int> _newCounts;

    // Expose number of unique items (items dictionary)
    public int UniqueCount => _items.Count;

    public DbCountedSet(
        DbDictionary<T>? items = null,
        DbDictionary<int>? counts = null,
        DbDictionary<T>? newObjects = null,
        DbDictionary<int>? newCounts = null,
        ObjectTransaction? transaction = null,
        AtomPointer? atomPointer = null,
        DbDictionary<Index>? indexes = null)
        : base(transaction, atomPointer)
    {
        _items = items ?? new DbDictionary<T>();
        _counts = counts ?? new DbDictionary<int>();
        _newObjects = newObjects ?? new DbDictionary<T>();
        _newCounts = newCounts ?? new DbDictionary<int>();
        Indexes = indexes;
    }

    // Iteration yields unique items (persisted view)
    public IEnumerable<T> AsIterable()
    {
        foreach (var (key, value) in _items.AsIterable())
            yield return value;
    }

    // Public enumerable
    public IEnumerator<T> GetEnumerator() => AsIterable().GetEnumerator();

    // Query helpers
    public bool Has(T key)
    {
        var h = HashOf(key);
        // Pending/new view has priority for membership
        if (_newCounts.Has(h)) return true;
        return _counts.Has(h);
    }

    public int GetCount(T key)
    {
        var h = HashOf(key);
        if (_counts.Has(h)) return _counts.GetAt(h) ?? 0;
        if (_newCounts.Has(h)) return _newCounts.GetAt(h) ?? 0;
        return 0;
    }

    // Total number of occurrences across all unique items (persisted only)
    public int TotalCount
    {
        get
        {
            var total = 0;
            foreach (var (k, cnt) in _counts.AsIterable())
            {
                try { total += Convert.ToInt32(cnt); } catch { /* ignore */ }
            }
            return total;
        }
    }

    // Add an occurrence of key
    public DbCountedSet<T> Add(T key)
    {
        // Defensive: avoid nesting counted sets/sets (optional, no-op if not applicable)
        if (key is DbCountedSet<T>) return this;

        var h = HashOf(key);

        // Increment persisted count if exists (no index updates)
        if (_counts.Has(h))
        {
            var current = _counts.GetAt(h) ?? 0;
            var newCounts = _counts.SetAt(h, current + 1);
            return Clone(items: _items, counts: newCounts, newObjects: _newObjects, newCounts: _newCounts);
        }

        // If staged exists, increment staged count and mirror to persisted counts view
        if (_newCounts.Has(h))
        {
            var staged = _newCounts.GetAt(h) ?? 0;
            var newNewCounts = _newCounts.SetAt(h, staged + 1);
            var persistedBase = _counts.Has(h) ? (_counts.GetAt(h) ?? 0) : 0;
            var newCountsPersisted = _counts.SetAt(h, persistedBase + 1);
            return Clone(items: _items, counts: newCountsPersisted, newObjects: _newObjects, newCounts: newNewCounts);
        }

        // First insertion: record item and count=1; update indexes for 0 -> 1 transition
        var ni = _items.SetAt(h, key);
        var nno = _newObjects.SetAt(h, key);
        var nnc = _newCounts.SetAt(h, 1);
        var ncp = _counts.SetAt(h, 1);

        var newIndexes = Indexes;
        if (newIndexes is not null)
        {
            // For each index, delegate addition; concrete Index will decide how to handle it
            foreach (var (attr, idx) in newIndexes.AsIterable())
            {
                idx.Add2Index(key);
            }
        }

        return Clone(items: ni, counts: ncp, newObjects: nno, newCounts: nnc, indexes: newIndexes);
    }

    // Remove one occurrence of key
    public DbCountedSet<T> RemoveAt(T key)
    {
        var h = HashOf(key);

        if (_counts.Has(h))
        {
            var repetition = (_counts.GetAt(h) ?? 0) - 1;
            var newCounts = _counts;
            var newItems = _items;
            var newIndexes = Indexes;
            var newNewCounts = _newCounts;
            var newNewObjects = _newObjects;

            if (repetition > 0)
            {
                newCounts = newCounts.SetAt(h, repetition);
            }
            else
            {
                // Last removal: drop item and counts, update indexes
                newCounts = newCounts.RemoveAt(h);
                newItems = newItems.RemoveAt(h);

                if (newIndexes is not null)
                {
                    foreach (var (attr, idx) in newIndexes.AsIterable())
                        idx.RemoveFromIndex(key);
                }

                if (newNewCounts.Has(h)) newNewCounts = newNewCounts.RemoveAt(h);
                if (newNewObjects.Has(h)) newNewObjects = newNewObjects.RemoveAt(h);
            }

            return Clone(newItems, newCounts, newNewObjects, newNewCounts, newIndexes);
        }
        else if (_newCounts.Has(h))
        {
            var repetition = (_newCounts.GetAt(h) ?? 0) - 1;
            var newNewCounts = _newCounts;
            var newNewObjects = _newObjects;
            var newIndexes = Indexes;

            if (repetition > 0)
            {
                newNewCounts = newNewCounts.SetAt(h, repetition);
            }
            else
            {
                newNewCounts = newNewCounts.RemoveAt(h);
                newNewObjects = newNewObjects.RemoveAt(h);

                if (newIndexes is not null)
                {
                    foreach (var (attr, idx) in newIndexes.AsIterable())
                        idx.RemoveFromIndex(key);
                }
            }

            return Clone(_items, _counts, newNewObjects, newNewCounts, newIndexes);
        }

        // Not present: no-op
        return this;
    }

    // Persists staged items and consolidates counts.
    // Convention: call Save() when needed by transaction/Atom lifecycle.
    protected virtual void Save()
    {
        if (_saved) return;
        _saved = true;

        // Persist items staged in _newObjects with their pending counts
        foreach (var (h, element) in _newObjects.AsIterable())
        {
            if (element is Atom a)
            {
                a.Transaction ??= this.Transaction;
                a.Save();
            }

            var inc = _newCounts.Has(h) ? (_newCounts.GetAt(h) ?? 0) : 0;
            var baseCnt = _counts.Has(h) ? (_counts.GetAt(h) ?? 0) : 0;

            // Ensure the item exists and set the correct total count
            _items.SetAt(h, element);
            _counts.SetAt(h, baseCnt + inc);
        }

        // Persist dictionaries themselves if they are atoms
        _items.Transaction ??= this.Transaction;
        (_items as Atom)?.Save();

        _counts.Transaction ??= this.Transaction;
        (_counts as Atom)?.Save();

        _saved = false;
    }

    // Compute a stable integer identity for membership
    private object HashOf(T key)
    {
        try
        {
            // Atoms: prefer pointer-derived identity if persisted
            if (key is Atom atom)
            {
                var ap = atom.AtomPointer;
                if (ap is not null && ap.TransactionId != default)
                    return ap.GetHashCode();
                return atom.GetHashCode();
            }

            // Strings: deterministic hash via SHA256 of UTF8
            if (key is string s)
            {
                using var sha = System.Security.Cryptography.SHA256.Create();
                var bytes = System.Text.Encoding.UTF8.GetBytes(s);
                var hash = sha.ComputeHash(bytes);
                return BitConverter.ToString(hash);
            }

            // Numbers/bools: typed string then hash
            if (key is IFormattable || key is bool)
            {
                var typed = $"{key.GetType().Name}:{key}";
                using var sha = System.Security.Cryptography.SHA256.Create();
                var bytes = System.Text.Encoding.UTF8.GetBytes(typed);
                var hash = sha.ComputeHash(bytes);
                return BitConverter.ToString(hash);
            }

            // Fallback: type name + ToString; otherwise Object.GetHashCode
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

    // Helper to clone with structural sharing
    private DbCountedSet<T> Clone(
        DbDictionary<T>? items = null,
        DbDictionary<int>? counts = null,
        DbDictionary<T>? newObjects = null,
        DbDictionary<int>? newCounts = null,
        DbDictionary<Index>? indexes = null)
        => new DbCountedSet<T>(
            items ?? _items,
            counts ?? _counts,
            newObjects ?? _newObjects,
            newCounts ?? _newCounts,
            transaction: this.Transaction,
            atomPointer: this.AtomPointer,
            indexes: indexes ?? this.Indexes
        );
}