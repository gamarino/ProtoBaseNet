namespace ProtoBaseNet;

using System;
using System.Collections.Generic;
using System.Linq;

public class DbList<T> : DbCollection
{
    private readonly List<T> _list;

    public new int Count => _list.Count;

    public DbList()
    {
        _list = new List<T>();
    }

    public DbList(IEnumerable<T> items)
    {
        _list = new List<T>(items);
    }

    private DbList(List<T> list)
    {
        _list = list;
    }

    public DbList(ObjectTransaction transaction, AtomPointer atomPointer) : base(transaction, atomPointer)
    {
        _list = new List<T>();
    }
    
    public T? GetAt(int index)
    {
        return _list[index];
    }

    public DbList<T> SetAt(int index, T? value)
    {
        var newList = new List<T>(_list);
        newList[index] = value!;
        return new DbList<T>(newList);
    }

    public DbList<T> InsertAt(int index, T? value)
    {
        var newList = new List<T>(_list);
        newList.Insert(index, value!);
        return new DbList<T>(newList);
    }

    public DbList<T> RemoveAt(int index)
    {
        var newList = new List<T>(_list);
        newList.RemoveAt(index);
        return new DbList<T>(newList);
    }

    public DbList<T> AppendLast(T? item)
    {
        return InsertAt(Count, item);
    }
    
    public IEnumerable<T?> AsIterable()
    {
        return _list;
    }
}