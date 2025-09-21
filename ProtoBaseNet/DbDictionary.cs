namespace ProtoBaseNet;

using System;
using System.Collections;
using System.Collections.Generic;

// Ordered, durable dictionary for Atom-backed storage.
// Key ordering is deterministic across heterogeneous key types via OrderKey normalization.
// Internals use a sorted List with binary search; operations are O(log N) for lookup/insert/remove.
public class DbDictionary<T> : DbCollection, IEnumerable<KeyValuePair<object, T>>
{
    private const string KeyNullMessage = "Key no puede ser null.";
    private const string TypeBool = "bool";
    private const string TypeNumber = "number";
    private const string TypeString = "str";
    private const string TypeBytes = "bytes";

    // Stable item wrapper with total ordering for storage and search.
    // CompareTo implements a two-stage comparison:
    // 1) type-group (bool/number/str/bytes)
    // 2) normalized payload comparison (IComparable, byte[] lexicographic, or string fallback)
    private sealed class DictionaryItem : IComparable<DictionaryItem>
    {
        public object Key { get; private set; }
        public T Value { get; private set; }

        /*START_USER_CODE*/public DictionaryItem(object key, T value)
        {
            ArgumentNullException.ThrowIfNull(key, KeyNullMessage);
            Key = key;
            Value = value;
        }
        /*END_USER_CODE*/

        // Probe item used for binary search without allocating a real value payload.
        public static DictionaryItem CreateProbe(object key)
        {
            ArgumentNullException.ThrowIfNull(key, KeyNullMessage);
            return new DictionaryItem(key, default!);
        }

        public int CompareTo(DictionaryItem? other)
        {
            if (other is null) return 1;
            var (g1, n1) = OrderKey(Key);
            var (g2, n2) = OrderKey(other.Key);

            var gcmp = string.CompareOrdinal(g1, g2);
            if (gcmp != 0) return gcmp;

            // Normalized payload comparison
            // Fast path: direct IComparable if possible
            if (n1 is IComparable c1 && n2 is not null)
            {
                try
                {
                    var cmp = c1.CompareTo(n2);
                    if (cmp != 0) return cmp;
                }
                catch
                {
                    // Fall through to alternate strategies
                }
            }

            // Byte array lexicographic ordering
            if (n1 is byte[] a1 && n2 is byte[] a2)
            {
                var len = Math.Min(a1.Length, a2.Length);
                for (int i = 0; i < len; i++)
                {
                    int d = a1[i].CompareTo(a2[i]);
                    if (d != 0) return d;
                }
                return a1.Length.CompareTo(a2.Length);
            }

            // Final fallback: ordinal string comparison of normalized values
            var s1 = n1?.ToString() ?? string.Empty;
            var s2 = n2?.ToString() ?? string.Empty;
            return string.CompareOrdinal(s1, s2);
        }
    }

    // Internal sorted storage and an operation log (optional auditing/diagnostics).
    private readonly List<DictionaryItem> _content = new();
    private readonly List<(string op, object key, T? value)> _opLog = new();

    // Number of entries (unique keys).
    public int Count => _content.Count;
    /*START_USER_CODE*/
    // Produces a deterministic ordering key as (group, normalized-value).
    // Groups guarantee cross-type ordering; normalization enables consistent comparisons within a group.
    private static (string group, object? norm) OrderKey(object key)
    {
        ArgumentNullException.ThrowIfNull(key, KeyNullMessage);

        switch (key)
        {
            case bool b:
                // false < true
                return (TypeBool, b);
            case sbyte or byte or short or ushort or int or uint or long or ulong or nint or nuint:
                // Prefer decimal for wide, precise integer ordering; fall back to double for ranges decimal can't cover.
                try
                {
                    var dec = Convert.ToDecimal(key);
                    return (TypeNumber, dec);
                }
                catch
                {
                    var dbl = Convert.ToDouble(key);
                    return (TypeNumber, dbl);
                }
            case float or double or decimal:
                // Normalize decimals as decimal; floating types as double.
                if (key is decimal m) return (TypeNumber, m);
                return (TypeNumber, Convert.ToDouble(key));
            case string s:
                return (TypeString, s);
            case ReadOnlyMemory<byte> rom:
                return (TypeBytes, rom.ToArray());
            case Memory<byte> mem:
                return (TypeBytes, mem.ToArray());
            case byte[] bytes:
                return (TypeBytes, bytes);
            default:
                // Fallback: stable string representation for unknown types.
                return (TypeString, key.ToString() ?? string.Empty);
        }
    }
    /*END_USER_CODE*/

    // Binary search by normalized key; returns index if found or bitwise complement of insertion index.
    private int FindIndex(object key)
    {
        ArgumentNullException.ThrowIfNull(key, KeyNullMessage);

        int left = 0, right = _content.Count - 1;
        var targetOk = OrderKey(key);

        while (left <= right)
        {
            int center = (left + right) / 2;
            var item = _content[center];
            var itemOk = OrderKey(item.Key);

            // Strict equality: same group, same normalized value, and exact key equality
            if (itemOk.group == targetOk.group && Equals(itemOk.norm, targetOk.norm) && Equals(item.Key, key))
                return center;

            // Navigate by group first
            int cmpGroup = string.CompareOrdinal(itemOk.group, targetOk.group);
            if (cmpGroup > 0)
            {
                right = center - 1;
                continue;
            }
            if (cmpGroup < 0)
            {
                left = center + 1;
                continue;
            }

            // Same group: compare normalized values
            int cmpNorm;
            if (itemOk.norm is IComparable c && itemOk.norm?.GetType() == targetOk.norm?.GetType())
            {
                try { cmpNorm = c.CompareTo(targetOk.norm); }
                catch { cmpNorm = string.CompareOrdinal(itemOk.norm?.ToString(), targetOk.norm?.ToString()); }
            }
            else if (itemOk.norm is byte[] a1 && targetOk.norm is byte[] a2)
            {
                var len = Math.Min(a1.Length, a2.Length);
                cmpNorm = 0;
                for (int i = 0; i < len && cmpNorm == 0; i++)
                    cmpNorm = a1[i].CompareTo(a2[i]);
                if (cmpNorm == 0) cmpNorm = a1.Length.CompareTo(a2.Length);
            }
            else
            {
                cmpNorm = string.CompareOrdinal(itemOk.norm?.ToString() ?? string.Empty, targetOk.norm?.ToString() ?? string.Empty);
            }

            // If current >= target, move left; otherwise right.
            if (cmpNorm >= 0) right = center - 1;
            else left = center + 1;
        }

        // Not found; bitwise complement yields the insertion position.
        return ~left;
    }

    // Membership check by key using binary search.
    public bool Has(object key)
    {
        ArgumentNullException.ThrowIfNull(key, KeyNullMessage);
        return FindIndex(key) >= 0;
    }

    // Retrieves value by key or default(T) if not present.
    public T? GetAt(object key)
    {
        ArgumentNullException.ThrowIfNull(key, KeyNullMessage);
        var idx = FindIndex(key);
        if (idx >= 0) return _content[idx].Value;
        return default;
    }

    // Inserts or replaces the value for the given key, preserving sorted order.
    // Records a "set" operation in the op-log for auditing.
    public DbDictionary<T> SetAt(object key, T value)
    {
        ArgumentNullException.ThrowIfNull(key, KeyNullMessage);

        var idx = FindIndex(key);
        if (idx >= 0)
        {
            _content[idx] = new DictionaryItem(key, value);
        }
        else
        {
            var ins = ~idx;
            _content.Insert(ins, new DictionaryItem(key, value));
        }

        _opLog.Add(("set", key, value));
        return this;
    }

    // Removes the entry for the given key if present.
    // Records a "remove" operation in the op-log for auditing.
    public DbDictionary<T> RemoveAt(object key)
    {
        ArgumentNullException.ThrowIfNull(key, KeyNullMessage);

        var idx = FindIndex(key);
        if (idx >= 0)
        {
            _content.RemoveAt(idx);
            _opLog.Add(("remove", key, default));
        }
        return this;
    }

    // Yields (key, value) pairs in sorted key order.
    public IEnumerable<(object key, T value)> AsIterable()
    {
        foreach (var item in _content)
            yield return (item.Key, item.Value);
    }

    // IEnumerable<KeyValuePair<object,T>> implementation for foreach and LINQ.
    public IEnumerator<KeyValuePair<object, T>> GetEnumerator()
    {
        foreach (var item in _content)
            yield return new KeyValuePair<object, T>(item.Key, item.Value);
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    // Convenience overload to support string keys without casting at call sites.
    public DbDictionary<T> SetAt(string key, T value) => SetAt((object)key, value);
}