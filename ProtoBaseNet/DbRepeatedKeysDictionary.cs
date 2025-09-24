namespace ProtoBaseNet;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

/// <summary>
/// An immutable dictionary-like collection that allows multiple, repeated values for a single key.
/// All mutation methods return a new instance of the dictionary.
/// No indexes are maintained
/// Even the collection is inmutable on storage, during a transaction a transient log is maintained
/// in order to recreate operations in the case another transaction changes the base root for this collection
/// At commit time, if a collision is detected, all operations are replayed
/// </summary>
/// <typeparam name="T">The type of values in the dictionary. Must be a non-nullable type.</typeparam>
public class DbRepeatedKeysDictionary<T> : DbCollection, IEnumerable<KeyValuePair<string, T>> where T : notnull
{
    // The persistent dictionary of string keys to CountedSet buckets.
    internal readonly DbDictionary<DbSet<T>> Values;

    // The transient operation log for handling concurrent updates. Not persisted.
    private readonly ImmutableList<(string op, string key, T? value)> _opLog;

    public DbRepeatedKeysDictionary(
        DbDictionary<DbSet<T>>? values = null,
        ImmutableList<(string op, string key, T? value)>? opLog = null,
        Guid? stableId = null,
        DbDictionary<Index>? indexes = null,
        ObjectTransaction? transaction = null, 
        AtomPointer? atomPointer = null) : base(stableId, indexes, transaction, atomPointer)
    {
        Values = values ?? new DbDictionary<DbSet<T>>();
        _opLog = opLog ?? ImmutableList<(string op, string key, T? value)>.Empty;
    }

    public DbSet<T> GetAt(string key)
    {
        DbSet<T> result = new DbSet<T>([]);

        if (Values.Has(key))
        {
            foreach (var item in Values.GetAt(key))
            {
                result = result.Add(item);
            }
        }

        return result;
    }

    public DbRepeatedKeysDictionary<T> SetAt(string key, T value)
    {
        var newValues = Values;
        var newOpLog = _opLog;
        
        if (Values.Has(key))
            newValues = newValues.SetAt(key, newValues.GetAt(key).Add(value));
        else
            newValues = newValues.SetAt(key, new DbSet<T>([value]));

        newOpLog = newOpLog.Add(("set", key, value));
        return new DbRepeatedKeysDictionary<T>(newValues, newOpLog);
    }

    public DbRepeatedKeysDictionary<T> RemoveAt(string key)
    {
        var newValues = Values;
        var newOpLog = _opLog;

        newValues = newValues.RemoveAt(key);

        newOpLog.Add(("remove", key, default));
        return new DbRepeatedKeysDictionary<T>(newValues, newOpLog);
    }


    public DbRepeatedKeysDictionary<T> RemoveRecord(string key, T value)
    {
        var newValues = Values;
        var newOpLog = _opLog;
        
        if (Values.Has(key))
            newValues = newValues.SetAt(key, newValues.GetAt(key).RemoveAt(value));

        newOpLog.Add(("remove_record", key, value));
        return new DbRepeatedKeysDictionary<T>(newValues, newOpLog);
    }

    public bool Has(string key) => Values.Has(key);

    public new DbRepeatedKeysDictionary<T> ConcurrentUpdate(DbCollection previousDbCollection)
    {
        var rebased = (DbRepeatedKeysDictionary<T>)previousDbCollection;
        foreach (var (op, key, value) in _opLog)
        {
            rebased = op switch
            {
                "set" when value is not null => rebased.SetAt(key, value),
                "remove" => rebased.RemoveAt(key),
                "remove_record" when value is not null => rebased.RemoveRecord(key, value),
                _ => throw new InvalidOperationException($"Unknown rebase operation: {op}")
            };
        }
        return rebased;
    }

    public override void Save()
    {
        if (AtomPointer != null) return;

        Values.Save();
        
        // Now that all children are persisted, save this object.
        base.Save();
    }

    public IEnumerator<KeyValuePair<string, T>> GetEnumerator()
    {
        foreach (var item in Values)
            foreach (var instance in item.Value)
                yield return new KeyValuePair<string, T>(item.Key, instance);
    }
    
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
