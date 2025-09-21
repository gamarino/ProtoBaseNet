namespace ProtoBaseNet;

using System;
using System.Collections;
using System.Collections.Generic;

public class DbDictionary<T> : DbCollection, IEnumerable<KeyValuePair<object, T>>
{
    private const string KeyNullMessage = "Key no puede ser null.";
    private const string TypeBool = "bool";
    private const string TypeNumber = "number";
    private const string TypeString = "str";
    private const string TypeBytes = "bytes";

    // Item durable y consistente con soporte de carga perezosa y orden determinista
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

        // Sonda para búsqueda binaria
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

            // Comparación del payload normalizado
            // IComparable directo
            if (n1 is IComparable c1 && n2 is not null)
            {
                try
                {
                    var cmp = c1.CompareTo(n2);
                    if (cmp != 0) return cmp;
                }
                catch
                {
                    // Ignorar y continuar a siguientes estrategias
                }
            }

            // byte[] lexicográfico
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

            // Fallback: comparar string ordinal
            var s1 = n1?.ToString() ?? string.Empty;
            var s2 = n2?.ToString() ?? string.Empty;
            return string.CompareOrdinal(s1, s2);
        }
    }

    private readonly List<DictionaryItem> _content = new();
    private readonly List<(string op, object key, T? value)> _opLog = new();

    public int Count => _content.Count;
    /*START_USER_CODE*/
    // Normaliza la clave a (grupo, valor normalizado) para un orden determinista
    private static (string group, object? norm) OrderKey(object key)
    {
        ArgumentNullException.ThrowIfNull(key, KeyNullMessage);

        switch (key)
        {
            case bool b:
                // usar false < true
                return (TypeBool, b);
            case sbyte or byte or short or ushort or int or uint or long or ulong or nint or nuint:
                // normalizar enteros a long (sin signo pasa por unchecked comparación vía IComparable en fallback)
                // Para orden estable y amplia capacidad, usar decimal cuando sea posible
                try
                {
                    var dec = Convert.ToDecimal(key);
                    return (TypeNumber, dec);
                }
                catch
                {
                    // Fallback a double si decimal no soporta el rango (e.g., ulong muy grande)
                    var dbl = Convert.ToDouble(key);
                    return (TypeNumber, dbl);
                }
            case float or double or decimal:
                // normalizar a decimal si es decimal; para float/double usar double
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
                // Otros tipos: usar ToString ordinal como representación estable
                return (TypeString, key.ToString() ?? string.Empty);
        }
    }
    /*END_USER_CODE*/

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

            // igualdad estricta por grupo/valor normalizado + igualdad exacta de clave
            if (itemOk.group == targetOk.group && Equals(itemOk.norm, targetOk.norm) && Equals(item.Key, key))
                return center;

            // si el item es mayor o igual por orden normalizado, nos movemos a la izquierda
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

            // mismo grupo: comparar nativos
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

        // No encontrado; left es posición de inserción
        return ~left;
    }

    public bool Has(object key)
    {
        ArgumentNullException.ThrowIfNull(key, KeyNullMessage);
        return FindIndex(key) >= 0;
    }

    public T? GetAt(object key)
    {
        ArgumentNullException.ThrowIfNull(key, KeyNullMessage);
        var idx = FindIndex(key);
        if (idx >= 0) return _content[idx].Value;
        return default;
    }

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

    public IEnumerable<(object key, T value)> AsIterable()
    {
        foreach (var item in _content)
            yield return (item.Key, item.Value);
    }

    public IEnumerator<KeyValuePair<object, T>> GetEnumerator()
    {
        foreach (var item in _content)
            yield return new KeyValuePair<object, T>(item.Key, item.Value);
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    // Compatibilidad con la firma adicional
    public DbDictionary<T> SetAt(string key, T value) => SetAt((object)key, value);
}