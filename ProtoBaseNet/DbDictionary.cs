namespace ProtoBaseNet;

using System;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// An immutable, ordered, and durable dictionary for Atom-backed storage.
/// </summary>
/// <typeparam name="T">The type of values in the dictionary.</typeparam>
/// <remarks>
/// Key ordering is deterministic across heterogeneous key types via OrderKey normalization.
/// Internals use a sorted List with binary search, so operations are O(log N) for lookup, insert, and remove.
/// </remarks>
public class DbDictionary<T> : DbCollection, IEnumerable<KeyValuePair<object, T>>
{
    private const string KeyNullMessage = "Key cannot be null.";
    private const string TypeBool = "bool";
    private const string TypeNumber = "number";
    private const string TypeString = "str";
    private const string TypeBytes = "bytes";

    private sealed class DictionaryItem : IComparable<DictionaryItem>
    {
        public object Key { get; private set; }
        public T Value { get; private set; }

        public DictionaryItem(object key, T value)
        {
            ArgumentNullException.ThrowIfNull(key, KeyNullMessage);
            Key = key;
            Value = value;
        }

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

            if (n1 is IComparable c1 && n2 is not null)
            {
                try
                {
                    var cmp = c1.CompareTo(n2);
                    if (cmp != 0) return cmp;
                }
                catch
                {
                    // Fall through
                }
            }

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

            var s1 = n1?.ToString() ?? string.Empty;
            var s2 = n2?.ToString() ?? string.Empty;
            return string.CompareOrdinal(s1, s2);
        }
    }

    private readonly List<DictionaryItem> _content = new();
    private readonly List<(string op, object key, T? value)> _opLog = new();

    /// <summary>
    /// Gets the number of key-value pairs contained in the dictionary.
    /// </summary>
    public new int Count => _content.Count;

    private static (string group, object? norm) OrderKey(object key)
    {
        ArgumentNullException.ThrowIfNull(key, KeyNullMessage);

        switch (key)
        {
            case bool b:
                return (TypeBool, b);
            case sbyte or byte or short or ushort or int or uint or long or ulong or nint or nuint:
                try
                {
                    return (TypeNumber, Convert.ToDecimal(key));
                }
                catch
                {
                    return (TypeNumber, Convert.ToDouble(key));
                }
            case float or double or decimal:
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
                return (TypeString, key.ToString() ?? string.Empty);
        }
    }

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

            if (itemOk.group == targetOk.group && Equals(itemOk.norm, targetOk.norm) && Equals(item.Key, key))
                return center;

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

            if (cmpNorm >= 0) right = center - 1;
            else left = center + 1;
        }

        return ~left;
    }

    /// <summary>
    /// Determines whether the dictionary contains the specified key.
    /// </summary>
    /// <param name="key">The key to locate.</param>
    /// <returns>True if the dictionary contains an element with the specified key; otherwise, false.</returns>
    public bool Has(object key)
    {
        ArgumentNullException.ThrowIfNull(key, KeyNullMessage);
        return FindIndex(key) >= 0;
    }

    /// <summary>
    /// Gets the value associated with the specified key.
    /// </summary>
    /// <param name="key">The key of the value to get.</param>
    /// <returns>The value associated with the specified key, or default(T) if the key is not found.</returns>
    public T? GetAt(object key)
    {
        ArgumentNullException.ThrowIfNull(key, KeyNullMessage);
        var idx = FindIndex(key);
        if (idx >= 0) return _content[idx].Value;
        return default;
    }

    /// <summary>
    /// Returns a new dictionary with the specified key and value set.
    /// If the key already exists, its value is replaced.
    /// </summary>
    /// <param name="key">The key of the element to set.</param>
    /// <param name="value">The value to associate with the key.</param>
    /// <returns>A new dictionary with the key-value pair set.</returns>
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

    /// <summary>
    /// Returns a new dictionary with the element with the specified key removed.
    /// </summary>
    /// <param name="key">The key of the element to remove.</param>
    /// <returns>A new dictionary with the specified element removed.</returns>
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

    /// <summary>
    /// Returns an enumerable that iterates through the dictionary in sorted key order.
    /// </summary>
    /// <returns>An enumerable of key-value pairs.</returns>
    public IEnumerable<(object key, T value)> AsIterable()
    {
        foreach (var item in _content)
            yield return (item.Key, item.Value);
    }

    /// <summary>
    /// Returns an enumerator that iterates through the collection.
    /// </summary>
    /// <returns>An enumerator that can be used to iterate through the collection.</returns>
    public IEnumerator<KeyValuePair<object, T>> GetEnumerator()
    {
        foreach (var item in _content)
            yield return new KeyValuePair<object, T>(item.Key, item.Value);
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>
    /// Convenience overload to support string keys without casting at call sites.
    /// </summary>
    /// <param name="key">The string key of the element to set.</param>
    /// <param name="value">The value to associate with the key.</param>
    /// <returns>A new dictionary with the key-value pair set.</returns>
    public DbDictionary<T> SetAt(string key, T value) => SetAt((object)key, value);
}