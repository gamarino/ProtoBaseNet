using System.Data.Common;

namespace ProtoBaseNet;

using System;
using System.Collections;
using System.Collections.Generic;


public class DbDictionary<T> : DbCollection, IEnumerable<KeyValuePair<string, T>>
{
    private readonly DbList<KeyValuePair<string, T>> Content;
    private readonly List<(string op, string key, T? value)> _opLog;
    
    public DbDictionary(
        DbList<KeyValuePair<string, T>>? content = null,
        List<(string op, string key, T? value)>? opLog = null,
        Guid? stableId = null,
        ObjectTransaction? transaction = null) : base(stableId: stableId, transaction: transaction)
        {
            if (content == null) 
                Content = new DbList<KeyValuePair<string, T>>(transaction: transaction);
            else 
                Content = content;
            if (opLog != null)
                _opLog = opLog;
            else
            {
                _opLog = new List<(string op, string key, T? value)>();
            }

        }

    public DbDictionary<T> SetAt(string key, T value)
    {
        int left = 0, right = Content.Count - 1;
        var opLog = _opLog;
        var newItem = new KeyValuePair<string, T> (key, value);
        
        while (left <= right)
        {
            int center = (left + right) / 2;

            var item = Content.GetAt(center);
            int cmp = key.CompareTo(item.Key);
            
            if (cmp < 0)
            {
                right = center - 1;
                continue;
            }
            if (cmp > 0)
            {
                left = center + 1;
                continue;
            }
            opLog.Add(("remove", key, Content.GetAt(center).Value));
            opLog.Add(("insert", key, value));
            return new DbDictionary<T>(
                content: Content.SetAt(center, newItem),
                opLog: opLog,
                stableId: StableId,
                transaction: Transaction);
        }

        opLog.Add(("insert", key, value));

        return new DbDictionary<T>(
            content: Content.InsertAt(left, newItem),
            opLog: opLog
        );
    }
    
    public DbDictionary<T> RemoveAt(string key)
    {
        int left = 0, right = Content.Count - 1;
        var opLog = _opLog;

        while (left <= right)
        {
            int center = (left + right) / 2;

            var item = Content.GetAt(center);
            int cmp = key.CompareTo(item.Key);
            
            if (cmp < 0)
            {
                right = center - 1;
                continue;
            }
            if (cmp > 0)
            {
                left = center + 1;
                continue;
            }
            opLog.Add(("remove", key, Content.GetAt(center).Value));
            return new DbDictionary<T>(
                content: Content.RemoveAt(center),
                opLog: opLog,
                stableId: StableId,
                transaction: Transaction);
        }

        return this;
    }

    public T? GetAt(string key)
    {
        int left = 0, right = Content.Count - 1;

        while (left <= right)
        {
            int center = (left + right) / 2;

            var item = Content.GetAt(center);
            int cmp = key.CompareTo(item.Key);
            
            if (cmp < 0)
            {
                right = center - 1;
                continue;
            }
            if (cmp > 0)
            {
                left = center + 1;
                continue;
            }

            return item.Value;
        }

        return default;
    }
    
    public bool Has(string key)
    {
        int left = 0, right = Content.Count - 1;

        while (left <= right)
        {
            int center = (left + right) / 2;

            var item = Content.GetAt(center);
            int cmp = key.CompareTo(item.Key);
            
            if (cmp < 0)
            {
                right = center - 1;
                continue;
            }
            if (cmp > 0)
            {
                left = center + 1;
                continue;
            }

            return true;
        }

        return false;
    }

    public DbDictionary<T> Merge(DbDictionary<T> other)
    {
        var newDictionary = this;
        foreach (var newItem in other)
        {
            newDictionary = newDictionary.SetAt(newItem.Key, newItem.Value);
        }

        return newDictionary;
    }

    public IEnumerator<KeyValuePair<string, T>> GetEnumerator()
    {
        foreach (var item in Content)
            yield return new KeyValuePair<string, T>(item.Key, item.Value);
    }
    
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

}

public static class KeyValuePairExtensions
{
    public static void Deconstruct<TKey, TValue>(this KeyValuePair<TKey, TValue> kvp, out TKey key, out TValue value)
    {
        key = kvp.Key;
        value = kvp.Value;
    }
}

