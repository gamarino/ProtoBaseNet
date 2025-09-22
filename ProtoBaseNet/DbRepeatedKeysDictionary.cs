namespace ProtoBaseNet;

using System;
using System.Collections.Generic;

/// <summary>
/// An immutable dictionary-like collection that allows multiple, repeated values for a single key.
/// </summary>
/// <typeparam name="T">The type of values in the dictionary.</typeparam>
/// <remarks>
/// It maintains a per-key multiset (<see cref="CountedSet"/>) to store values.
/// - <see cref="SetAt"/> inserts one occurrence of a value under a key.
/// - <see cref="RemoveRecordAt"/> removes a single occurrence of a value.
/// - <see cref="RemoveAt"/> removes the entire key and all its values.
/// </remarks>
public class DbRepeatedKeysDictionary<T> : DbCollection
{
    /// <summary>
    /// Represents a bucket that acts as a multiset for values associated with a single key.
    /// </summary>
    public sealed class CountedSet
    {
        private readonly Dictionary<T, int> _counts = new();

        /// <summary>
        /// Gets the number of distinct values stored in the bucket.
        /// </summary>
        public int Count => _counts.Count;

        /// <summary>
        /// Checks if the bucket contains a specific value.
        /// </summary>
        /// <param name="value">The value to check for.</param>
        /// <returns>True if the value is present, otherwise false.</returns>
        public bool Has(T value) => _counts.ContainsKey(value);

        /// <summary>
        /// Returns an enumerable that iterates through the values, repeating each according to its count.
        /// </summary>
        /// <returns>An enumerable for the values in the bucket.</returns>
        public IEnumerable<T> AsIterable()
        {
            foreach (var kv in _counts)
            {
                for (int i = 0; i < kv.Value; i++)
                    yield return kv.Key;
            }
        }

        /// <summary>
        /// Adds an occurrence of a value to the bucket.
        /// </summary>
        /// <param name="value">The value to add.</param>
        /// <returns>The same <see cref="CountedSet"/> instance for chaining.</returns>
        public CountedSet Add(T value)
        {
            if (_counts.TryGetValue(value, out var c))
                _counts[value] = c + 1;
            else
                _counts[value] = 1;
            return this;
        }

        /// <summary>
        /// Removes a single occurrence of a value from the bucket.
        /// </summary>
        /// <param name="value">The value to remove.</param>
        /// <returns>The same <see cref="CountedSet"/> instance for chaining.</returns>
        public CountedSet Remove(T value)
        {
            if (_counts.TryGetValue(value, out var c))
            {
                if (c <= 1) _counts.Remove(value);
                else _counts[value] = c - 1;
            }
            return this;
        }

        /// <summary>
        /// Checks if the bucket is empty.
        /// </summary>
        /// <returns>True if the bucket contains no values, otherwise false.</returns>
        public bool IsEmpty() => _counts.Count == 0;
    }

    private readonly DbDictionary<CountedSet> _dict = new();
    private readonly List<(string op, string key, T? value)> _opLog = new();

    /// <summary>
    /// Gets the bucket of values associated with the specified key.
    /// </summary>
    /// <param name="key">The key to retrieve the bucket for.</param>
    /// <returns>A <see cref="CountedSet"/> containing the values for the key. Returns an empty set if the key is not found.</returns>
    public CountedSet GetAt(string key)
    {
        var bucket = _dict.GetAt(key);
        return bucket ?? new CountedSet();
    }

    /// <summary>
    /// Returns a new dictionary with an additional occurrence of the value under the specified key.
    /// </summary>
    /// <param name="key">The key to add the value to.</param>
    /// <param name="value">The value to add.</param>
    /// <returns>A new dictionary with the value added.</returns>
    public DbRepeatedKeysDictionary<T> SetAt(string key, T value)
    {
        var bucket = _dict.Has(key) ? (_dict.GetAt(key) ?? new CountedSet()) : new CountedSet();
        var previouslyPresent = bucket.Has(value);
        bucket.Add(value);
        _dict.SetAt(key, bucket);
        _opLog.Add(("set", key, value));

        return this;
    }

    /// <summary>
    /// Returns a new dictionary with the specified key and all its associated values removed.
    /// </summary>
    /// <param name="key">The key to remove.</param>
    /// <returns>A new dictionary with the key removed.</returns>
    public DbRepeatedKeysDictionary<T> RemoveAt(string key)
    {
        if (_dict.Has(key))
        {
            _dict.RemoveAt(key);
            _opLog.Add(("remove", key, default));
        }
        return this;
    }

    /// <summary>
    /// Returns a new dictionary with a single occurrence of a value removed from the specified key's bucket.
    /// </summary>
    /// <param name="key">The key of the bucket.</param>
    /// <param name="record">The value to remove.</param>
    /// <returns>A new dictionary with the value removed.</returns>
    public DbRepeatedKeysDictionary<T> RemoveRecordAt(string key, T record)
    {
        if (_dict.Has(key))
        {
            var bucket = _dict.GetAt(key);
            if (bucket is not null && bucket.Has(record))
            {
                bucket.Remove(record);
                if (bucket.IsEmpty())
                {
                    _dict.RemoveAt(key);
                }
                else
                {
                    _dict.SetAt(key, bucket);
                }
                _opLog.Add(("remove_record", key, record));
            }
        }
        return this;
    }

    /// <summary>
    /// Determines whether the dictionary contains the specified key.
    /// </summary>
    /// <param name="key">The key to locate.</param>
    /// <returns>True if the dictionary contains the key; otherwise, false.</returns>
    public bool Has(string key) => _dict.Has(key);

    /// <summary>
    /// Returns an enumerable that iterates through all key-value pairs, repeating values according to their counts.
    /// </summary>
    /// <returns>An enumerable of key-value pairs.</returns>
    public IEnumerable<(string key, T value)> AsIterable()
    {
        foreach (var kv in _dict)
        {
            var key = kv.Key as string ?? kv.Key?.ToString() ?? string.Empty;
            foreach (var v in kv.Value.AsIterable())
                yield return (key, v);
        }
    }

    /// <summary>
    /// Replays the operation log of this dictionary on top of a provided current state.
    /// </summary>
    /// <param name="current">The base dictionary to rebase onto.</param>
    /// <returns>A new dictionary representing the rebased state.</returns>
    public DbRepeatedKeysDictionary<T> RebaseOn(DbRepeatedKeysDictionary<T> current)
    {
        var rebased = current ?? new DbRepeatedKeysDictionary<T>();
        foreach (var (op, key, value) in _opLog)
        {
            if (op == "set" && value is not null)
                rebased.SetAt(key, value);
            else if (op == "remove")
                rebased.RemoveAt(key);
            else if (op == "remove_record" && value is not null)
                rebased.RemoveRecordAt(key, value);
            else
                throw new InvalidOperationException($"Unknown rebase operation: {op}");
        }
        return rebased;
    }
}