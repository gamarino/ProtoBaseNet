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
    private readonly DbDictionary<T> _items;
    private readonly DbDictionary<int> _counts;
    private readonly DbDictionary<T> _newObjects;
    private readonly DbDictionary<int> _newCounts;

    /// <summary>
    /// Gets the number of unique items in the set.
    /// </summary>
    public int UniqueCount => _items.Count;

    /// <summary>
    /// Initializes a new instance of the <see cref="DbCountedSet{T}"/> class.
    /// </summary>
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

    /// <summary>
    /// Returns an enumerable that iterates through the unique items in the set.
    /// </summary>
    /// <returns>An <see cref="IEnumerable{T}"/> for the unique items.</returns>
    public IEnumerable<T> AsIterable()
    {
        foreach (var (key, value) in _items.AsIterable())
            yield return value;
    }

    /// <summary>
    /// Returns an enumerator that iterates through the unique items in the set.
    /// </summary>
    /// <returns>An enumerator for the set.</returns>
    public IEnumerator<T> GetEnumerator() => AsIterable().GetEnumerator();

    /// <summary>
    /// Determines whether the set contains a specific element.
    /// </summary>
    /// <param name="key">The element to locate in the set.</param>
    /// <returns>True if the element is found in the set; otherwise, false.</returns>
    public bool Has(T key)
    {
        var h = HashOf(key);
        if (_newCounts.Has(h)) return true;
        return _counts.Has(h);
    }

    /// <summary>
    /// Gets the number of occurrences of a specific element.
    /// </summary>
    /// <param name="key">The element to count.</param>
    /// <returns>The number of times the element appears in the set.</returns>
    public int GetCount(T key)
    {
        var h = HashOf(key);
        if (_counts.Has(h)) return _counts.GetAt(h) ?? 0;
        if (_newCounts.Has(h)) return _newCounts.GetAt(h) ?? 0;
        return 0;
    }

    /// <summary>
    /// Gets the total number of occurrences of all items in the set.
    /// </summary>
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

    /// <summary>
    /// Adds an element to the set. If the element already exists, its count is incremented.
    /// </summary>
    /// <param name="key">The element to add.</param>
    /// <returns>A new counted set with the element added or its count incremented.</returns>
    public DbCountedSet<T> Add(T key)
    {
        if (key is DbCountedSet<T>) return this;

        var h = HashOf(key);

        if (_counts.Has(h))
        {
            var current = _counts.GetAt(h) ?? 0;
            var newCounts = _counts.SetAt(h, current + 1);
            return Clone(items: _items, counts: newCounts, newObjects: _newObjects, newCounts: _newCounts);
        }

        if (_newCounts.Has(h))
        {
            var staged = _newCounts.GetAt(h) ?? 0;
            var newNewCounts = _newCounts.SetAt(h, staged + 1);
            var persistedBase = _counts.Has(h) ? (_counts.GetAt(h) ?? 0) : 0;
            var newCountsPersisted = _counts.SetAt(h, persistedBase + 1);
            return Clone(items: _items, counts: newCountsPersisted, newObjects: _newObjects, newCounts: newNewCounts);
        }

        var ni = _items.SetAt(h, key);
        var nno = _newObjects.SetAt(h, key);
        var nnc = _newCounts.SetAt(h, 1);
        var ncp = _counts.SetAt(h, 1);

        var newIndexes = Indexes;
        if (newIndexes is not null)
        {
            foreach (var (attr, idx) in newIndexes.AsIterable())
            {
                idx.Add2Index(key);
            }
        }

        return Clone(items: ni, counts: ncp, newObjects: nno, newCounts: nnc, indexes: newIndexes);
    }

    /// <summary>
    /// Removes one occurrence of an element from the set.
    /// </summary>
    /// <param name="key">The element to remove.</param>
    /// <returns>A new counted set with the element removed or its count decremented.</returns>
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

        return this;
    }

    protected override void Save()
    {
        if (_saved) return;
        _saved = true;

        foreach (var (h, element) in _newObjects.AsIterable())
        {
            if (element is Atom a)
            {
                a.Transaction ??= this.Transaction;
                a.Save();
            }

            var inc = _newCounts.Has(h) ? (_newCounts.GetAt(h) ?? 0) : 0;
            var baseCnt = _counts.Has(h) ? (_counts.GetAt(h) ?? 0) : 0;

            _items.SetAt(h, element);
            _counts.SetAt(h, baseCnt + inc);
        }

        _items.Transaction ??= this.Transaction;
        (_items as Atom)?.Save();

        _counts.Transaction ??= this.Transaction;
        (_counts as Atom)?.Save();

        _saved = false;
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