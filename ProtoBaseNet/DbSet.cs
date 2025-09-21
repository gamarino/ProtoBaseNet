namespace ProtoBaseNet;

using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

// Immutable, persistent-style set with transactional staging.
// Key ideas:
// - Structural sharing: Add/RemoveAt return new DbSet<T> instances, reusing existing dictionaries.
// - Two-phase storage: _newObjects holds staged (ephemeral) elements; Save() promotes them to _content.
// - Stable hashing: membership is determined by a deterministic hash computed from the element,
//   ensuring consistent identity across sessions without forcing persistence of Atoms.
// - Index hooks: _indexes exists as a placeholder for future index maintenance (0->1 and 1->0 transitions).
public class DbSet<T> : DbCollection
{
    // Persisted content (hash -> element).
    private DbDictionary<T> _content = new();
    // Staged, not-yet-persisted elements (hash -> element).
    private DbDictionary<T> _newObjects = new();

    // Optional index dictionary (name -> index). Kept generic for future integration.
    private DbDictionary<object>? _indexes = new();

    // Total number of elements considering both persisted and staged views.
    public int Count => (_content?.Count ?? 0) + (_newObjects?.Count ?? 0);

    public DbSet() { }

    private DbSet(DbDictionary<T> content, DbDictionary<T> newObjects, DbDictionary<object>? indexes)
    {
        _content = content;
        _newObjects = newObjects;
        _indexes = indexes;
    }

    // Deterministic hash function inspired by the template’s _hash_of:
    // - Strings: SHA-256 of UTF-8 bytes
    // - Bool/numbers: SHA-256 of a typed string "<type>:<value>"
    // - Other: SHA-256 of "<TypeName>:<ToString()>"
    // Fallback to GetHashCode() if any step fails.
    private static int StableHash(object? key)
    {
        if (key is null) return 0;

        try
        {
            if (key is string s)
            {
                using var sha = SHA256.Create();
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(s));
                return BitConverter.ToInt32(bytes, 0);
            }

            if (key is bool b)
            {
                using var sha = SHA256.Create();
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes($"bool:{(b ? 1 : 0)}"));
                return BitConverter.ToInt32(bytes, 0);
            }

            if (key is sbyte or byte or short or ushort or int or uint or long or ulong or nint or nuint or float or double or decimal)
            {
                using var sha = SHA256.Create();
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes($"{key.GetType().Name}:{key}"));
                return BitConverter.ToInt32(bytes, 0);
            }

            using (var sha2 = SHA256.Create())
            {
                var repr = $"{key.GetType().Name}:{key}";
                var bytes = sha2.ComputeHash(Encoding.UTF8.GetBytes(repr));
                return BitConverter.ToInt32(bytes, 0);
            }
        }
        catch
        {
            return key.GetHashCode();
        }
    }

    // Membership check across staged and persisted sets.
    public bool Has(T key)
    {
        var h = StableHash(key!);
        return _newObjects.Has(h) || _content.Has(h);
    }

    // Adds an element if not already present. Returns a new set instance.
    // Index update semantics (if implemented) should occur on 0 -> 1 transition.
    public DbSet<T> Add(T key)
    {
        if (Has(key)) return this;

        var h = StableHash(key!);
        var newNew = new DbDictionary<T>().ExtendFrom(_newObjects).SetAt(h, key);
        var newIndexes = _indexes; // Hook for index updates on first appearance
        return new DbSet<T>(_content, newNew, newIndexes);
    }

    // Removes an element if present. Returns a new set instance.
    // Index update semantics (if implemented) should occur on 1 -> 0 transition.
    public DbSet<T> RemoveAt(T key)
    {
        var h = StableHash(key!);
        if (!Has(key)) return this;

        var newNew = _newObjects;
        var newCont = _content;

        if (_newObjects.Has(h))
        {
            newNew = _newObjects.RemoveAt(h);
        }
        else
        {
            newCont = _content.RemoveAt(h);
        }

        var newIndexes = _indexes; // Hook for index updates on last removal
        return new DbSet<T>(newCont, newNew, newIndexes);
    }

    // Promotes staged elements to persisted content.
    // In a full Atom-backed implementation, this would also Save underlying structures.
    public void Save()
    {
        // Promote all ephemeral elements to persisted content.
        foreach (var (k, v) in _newObjects.AsIterable())
        {
            _content = _content.SetAt(k, v);
        }
        // Clear staging.
        _newObjects = new DbDictionary<T>();
        // Persist content/indexes if required by infrastructure (placeholder).
    }

    // Iterates elements: staged first, then persisted.
    // If duplicates occur across views (shouldn’t under normal use), both will be yielded.
    public IEnumerable<T> AsIterable()
    {
        foreach (var (_, v) in _newObjects.AsIterable())
            yield return v;
        foreach (var (_, v) in _content.AsIterable())
            yield return v;
    }

    // Set union (functional): adds all elements from 'other'.
    public DbSet<T> Union(DbSet<T> other)
    {
        var result = this;
        foreach (var item in other.AsIterable())
            result = result.Add(item);
        return result;
    }

    // Set intersection (functional): elements present in both sets.
    public DbSet<T> Intersection(DbSet<T> other)
    {
        var result = new DbSet<T>();
        foreach (var item in AsIterable())
        {
            if (other.Has(item))
                result = result.Add(item);
        }
        return result;
    }

    // Set difference (functional): elements in this set but not in 'other'.
    public DbSet<T> Difference(DbSet<T> other)
    {
        var result = new DbSet<T>();
        foreach (var item in AsIterable())
        {
            if (!other.Has(item))
                result = result.Add(item);
        }
        return result;
    }
}

// Minimal utility extensions over DbDictionary<T> used by DbSet<T> to support staging and iteration.
// Note: These rely on the existing DbDictionary<T> API for SetAt/RemoveAt/GetAt/iteration.
file static class DbDictionaryExtensions
{
    // Copies entries from 'source' into 'target', returning the new dictionary (functional style).
    public static DbDictionary<T> ExtendFrom<T>(this DbDictionary<T> target, DbDictionary<T> source)
    {
        foreach (var (k, v) in source.AsIterable())
            target = target.SetAt(k, v);
        return target;
    }

    // Iterates pairs (int keyHash, T value). DbDictionary<T> exposes object keys; we coerce to int when possible.
    public static IEnumerable<(int key, T value)> AsIterable<T>(this DbDictionary<T> dict)
    {
        foreach (var kv in dict)
        {
            var keyObj = kv.Key;
            int key = keyObj is int i ? i : (keyObj is IConvertible ? Convert.ToInt32(keyObj) : keyObj.GetHashCode());
            yield return (key, kv.Value);
        }
    }

    // Convenience wrappers using int keys for this set’s hash-based dictionary.
    public static bool Has<T>(this DbDictionary<T> dict, int key)
        => dict.GetAt(key) is not null;

    public static DbDictionary<T> RemoveAt<T>(this DbDictionary<T> dict, int key)
        => dict.RemoveAt(key);

    public static DbDictionary<T> SetAt<T>(this DbDictionary<T> dict, int key, T value)
        => dict.SetAt(key, value);
}