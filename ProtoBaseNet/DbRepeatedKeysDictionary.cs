namespace ProtoBaseNet;

using System;
using System.Collections.Generic;

// Dictionary-like collection that allows repeated values per key by maintaining a
// per-key multiset (CountedSet). External semantics:
// - SetAt(key, value) inserts one occurrence of 'value' under 'key' (duplicates allowed).
// - RemoveRecordAt(key, value) removes a single occurrence; when the last occurrence is removed,
//   the value disappears from the bucket.
// - RemoveAt(key) removes the entire key and all its values.
// - AsIterable() enumerates each (key, value) pair, repeating values according to their counts.
//
// Indexing notes (not implemented here):
// - Indexes should be updated only on transitions 0->1 (first insertion) and 1->0 (last removal) for a (key,value).
public class DbRepeatedKeysDictionary<T> : DbCollection
{
    // Bucket that acts as a multiset for values associated with a single key.
    // Internally counts occurrences per value while exposing iteration that repeats
    // each value count times.
    public sealed class CountedSet
    {
        private readonly Dictionary<T, int> _counts = new();

        // Number of distinct values stored in the bucket.
        public int Count => _counts.Count;

        // Membership test for a value (ignores multiplicity).
        public bool Has(T value) => _counts.ContainsKey(value);

        // Iterates values, repeating each according to its current count.
        public IEnumerable<T> AsIterable()
        {
            foreach (var kv in _counts)
            {
                for (int i = 0; i < kv.Value; i++)
                    yield return kv.Key;
            }
        }

        // Adds a value occurrence; creates the entry on first insertion.
        public CountedSet Add(T value)
        {
            if (_counts.TryGetValue(value, out var c))
                _counts[value] = c + 1;
            else
                _counts[value] = 1;
            return this;
        }

        // Removes a single occurrence; removes the entry when the last occurrence is removed.
        public CountedSet Remove(T value)
        {
            if (_counts.TryGetValue(value, out var c))
            {
                if (c <= 1) _counts.Remove(value);
                else _counts[value] = c - 1;
            }
            return this;
        }

        // True if the bucket contains no values.
        public bool IsEmpty() => _counts.Count == 0;
    }

    // Main dictionary: key -> CountedSet bucket holding repeated values.
    private readonly DbDictionary<CountedSet> _dict = new();

    // Operation log for simple conflict resolution or replay:
    // ("set" | "remove" | "remove_record", key, value)
    private readonly List<(string op, string key, T? value)> _opLog = new();

    // Returns the existing bucket for a key or an empty one if absent (does not attach it to the dictionary).
    public CountedSet GetAt(string key)
    {
        var bucket = _dict.GetAt(key);
        return bucket ?? new CountedSet();
    }

    // Inserts a value occurrence under a key (duplicates allowed).
    // If this is the first occurrence for 'value' within the key, indexing should be updated (0->1).
    public DbRepeatedKeysDictionary<T> SetAt(string key, T value)
    {
        var bucket = _dict.Has(key) ? (_dict.GetAt(key) ?? new CountedSet()) : new CountedSet();
        var previouslyPresent = bucket.Has(value);
        bucket.Add(value);
        _dict.SetAt(key, bucket);
        _opLog.Add(("set", key, value));

        // Indexing hook example:
        // if (Indexes != null && !previouslyPresent) { Add2Indexes(value); }

        return this;
    }

    // Removes the entire key and all its associated values.
    // If indexing is present, it should remove all bucket occurrences from indexes.
    public DbRepeatedKeysDictionary<T> RemoveAt(string key)
    {
        if (_dict.Has(key))
        {
            // Indexing hook example:
            // foreach (var v in _dict.GetAt(key).AsIterable()) RemoveFromIndexes(v);

            _dict.RemoveAt(key);
            _opLog.Add(("remove", key, default));
        }
        return this;
    }

    // Removes a single occurrence of 'record' from the key's bucket.
    // If this was the last occurrence (1->0), indexing should be updated accordingly.
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

                // Indexing hook example:
                // if (Indexes != null && !bucket.Has(record)) { RemoveFromIndexes(record); }
            }
        }
        return this;
    }

    // True if the dictionary contains the key (regardless of bucket content).
    public bool Has(string key) => _dict.Has(key);

    // Enumerates all (key, value) pairs, repeating values according to their counts per key.
    public IEnumerable<(string key, T value)> AsIterable()
    {
        foreach (var kv in _dict)
        {
            var key = kv.Key as string ?? kv.Key?.ToString() ?? string.Empty;
            foreach (var v in kv.Value.AsIterable())
                yield return (key, v);
        }
    }

    // Simplified rebase: replays the operation log on top of a provided current state.
    // Useful for merging local mutations onto a fresh base state.
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