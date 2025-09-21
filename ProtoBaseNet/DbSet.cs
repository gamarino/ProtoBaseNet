namespace ProtoBaseNet;

using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

public class DbSet<T> : DbCollection
{
    // Contenido persistente (hash -> elemento)
    private DbDictionary<T> _content = new();
    // Objetos nuevos efímeros (hash -> elemento)
    private DbDictionary<T> _newObjects = new();

    // Diccionario de índices opcional (nombre -> índice). Mantener como objeto genérico.
    private DbDictionary<object>? _indexes = new();

    public int Count => (_content?.Count ?? 0) + (_newObjects?.Count ?? 0);

    public DbSet() { }

    private DbSet(DbDictionary<T> content, DbDictionary<T> newObjects, DbDictionary<object>? indexes)
    {
        _content = content;
        _newObjects = newObjects;
        _indexes = indexes;
    }

    // Hash estable inspirado en _hash_of del template
    private static int StableHash(object? key)
    {
        if (key is null) return 0;

        try
        {
            // Si el objeto expone un puntero/identidad persistente con hash, úsalo
            // Nota: Placeholder; en infra real, se consultaría AtomPointer.hash()
            // Si no, mantener comportamiento estable
            if (key is string s)
            {
                // SHA-256 de string
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

            // Fallback: SHA-256 de "<Tipo>:<ToString()>"
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

    public bool Has(T key)
    {
        var h = StableHash(key!);
        return _newObjects.Has(h) || _content.Has(h);
    }

    public DbSet<T> Add(T key)
    {
        // Evita agregar duplicados
        if (Has(key)) return this;

        var h = StableHash(key!);
        var newNew = new DbDictionary<T>().ExtendFrom(_newObjects).SetAt(h, key);
        var newIndexes = _indexes; // En una implementación real, actualizar índices 0->1
        return new DbSet<T>(_content, newNew, newIndexes);
    }

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

        var newIndexes = _indexes; // En una implementación real, actualizar índices al salir del conjunto
        return new DbSet<T>(newCont, newNew, newIndexes);
    }

    // Promueve _newObjects a content en guardado (persistencia)
    public void Save()
    {
        // Promoción: todos los elementos efímeros pasan a persistentes
        foreach (var (k, v) in _newObjects.AsIterable())
        {
            _content = _content.SetAt(k, v);
        }
        // Vaciar staging
        _newObjects = new DbDictionary<T>();
        // Guardado real del contenido/índices si aplica
        // (placeholders; dependerá de la infraestructura DbCollection)
    }

    public IEnumerable<T> AsIterable()
    {
        // Primero elementos efímeros
        foreach (var (_, v) in _newObjects.AsIterable())
            yield return v;
        // Luego persistentes
        foreach (var (_, v) in _content.AsIterable())
            yield return v;
    }

    public DbSet<T> Union(DbSet<T> other)
    {
        var result = this;
        foreach (var item in other.AsIterable())
            result = result.Add(item);
        return result;
    }

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

// Extensiones utilitarias mínimas sobre DbDictionary<T> de esta solución
file static class DbDictionaryExtensions
{
    public static DbDictionary<T> ExtendFrom<T>(this DbDictionary<T> target, DbDictionary<T> source)
    {
        foreach (var (k, v) in source.AsIterable())
            target = target.SetAt(k, v);
        return target;
    }

    // Iteración de pares (keyHash, value)
    public static IEnumerable<(int key, T value)> AsIterable<T>(this DbDictionary<T> dict)
    {
        foreach (var kv in dict)
        {
            // La implementación de DbDictionary expone Key como object; aquí esperamos int (hash)
            var keyObj = kv.Key;
            int key = keyObj is int i ? i : (keyObj is IConvertible ? Convert.ToInt32(keyObj) : keyObj.GetHashCode());
            yield return (key, kv.Value);
        }
    }

    public static bool Has<T>(this DbDictionary<T> dict, int key)
        => dict.GetAt(key) is not null;

    public static DbDictionary<T> RemoveAt<T>(this DbDictionary<T> dict, int key)
    {
        // DbDictionary<T> del proyecto ya implementa RemoveAt(object)
        return dict.RemoveAt(key);
    }

    public static DbDictionary<T> SetAt<T>(this DbDictionary<T> dict, int key, T value)
        => dict.SetAt(key, value);
}