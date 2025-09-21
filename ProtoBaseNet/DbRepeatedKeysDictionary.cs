namespace ProtoBaseNet;

using System;
using System.Collections.Generic;

public class DbRepeatedKeysDictionary<T> : DbCollection
{
    // Bucket que permite valores repetidos mediante contador por elemento.
    // Duplicate values permitidos: cada Add incrementa la cuenta.
    public sealed class CountedSet
    {
        private readonly Dictionary<T, int> _counts = new();

        public int Count => _counts.Count;

        public bool Has(T value) => _counts.ContainsKey(value);

        public IEnumerable<T> AsIterable()
        {
            foreach (var kv in _counts)
            {
                // Devuelve cada elemento tantas veces como conteo (para emular duplicados)
                for (int i = 0; i < kv.Value; i++)
                    yield return kv.Key;
            }
        }

        public CountedSet Add(T value)
        {
            if (_counts.TryGetValue(value, out var c))
                _counts[value] = c + 1;
            else
                _counts[value] = 1;
            return this;
        }

        public CountedSet Remove(T value)
        {
            if (_counts.TryGetValue(value, out var c))
            {
                if (c <= 1) _counts.Remove(value);
                else _counts[value] = c - 1;
            }
            return this;
        }

        public bool IsEmpty() => _counts.Count == 0;
    }

    // Diccionario principal: key -> CountedSet (bucket)
    private readonly DbDictionary<CountedSet> _dict = new();

    // Log de operaciones: ('set'|'remove'|'remove_record', key, value)
    private readonly List<(string op, string key, T? value)> _opLog = new();

    // Retorna el bucket (CountedSet) existente o uno vacío si no existe
    public CountedSet GetAt(string key)
    {
        var bucket = _dict.GetAt(key);
        return bucket ?? new CountedSet();
    }

    // Inserta o actualiza el bucket de la clave agregando un valor (permite duplicados)
    public DbRepeatedKeysDictionary<T> SetAt(string key, T value)
    {
        var bucket = _dict.Has(key) ? (_dict.GetAt(key) ?? new CountedSet()) : new CountedSet();
        var previouslyPresent = bucket.Has(value);
        bucket.Add(value);
        _dict.SetAt(key, bucket);
        _opLog.Add(("set", key, value));

        // Nota: Si hubiera índices, aquí se actualizarían sólo en transición 0->1 para 'value'
        // if (indexes != null && !previouslyPresent) { add2indexes(value); }

        return this;
    }

    // Elimina toda la clave y sus valores
    public DbRepeatedKeysDictionary<T> RemoveAt(string key)
    {
        if (_dict.Has(key))
        {
            // Si hubiera índices, se eliminarían todas las ocurrencias del bucket aquí.
            // foreach (var v in _dict.GetAt(key).AsIterable()) remove_from_indexes(v);

            _dict.RemoveAt(key);
            _opLog.Add(("remove", key, default));
        }
        return this;
    }

    // Elimina una sola ocurrencia de 'record' en el bucket de la clave
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

                // Nota: Actualizar índices solamente cuando el record deja de estar presente en el bucket.
                // if (indexes != null && !bucket.Has(record)) { remove_from_indexes(record); }
            }
        }
        return this;
    }

    public bool Has(string key) => _dict.Has(key);

    public IEnumerable<(string key, T value)> AsIterable()
    {
        foreach (var kv in _dict)
        {
            var key = kv.Key as string ?? kv.Key?.ToString() ?? string.Empty;
            foreach (var v in kv.Value.AsIterable())
                yield return (key, v);
        }
    }

    // Rebase simplificado: vuelve a aplicar el op_log sobre el estado actual provisto
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
                throw new InvalidOperationException($"Operación desconocida en rebase: {op}");
        }
        return rebased;
    }
}