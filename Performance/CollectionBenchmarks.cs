using System.Collections.Generic;
using System.Collections.Immutable;
using BenchmarkDotNet.Attributes;
using ProtoBaseNet;

namespace Performance;

[MemoryDiagnoser]
public class CollectionBenchmarks
{
    [Params(100, 1000, 10000)]
    public int N;

    // Data for Dictionaries
    private List<KeyValuePair<DbLiteral, DbLiteral>> _dictionaryData;
    private DbDictionary _dbDictionary;
    private Dictionary<DbLiteral, DbLiteral> _dictionary;
    private ImmutableDictionary<DbLiteral, DbLiteral> _immutableDictionary;

    // Data for Lists
    private List<DbLiteral> _listData;
    private DbList _dbList;
    private List<DbLiteral> _list;
    private ImmutableList<DbLiteral> _immutableList;

    // Data for Sets
    private List<DbLiteral> _setData;
    private DbSet _dbSet;
    private HashSet<DbLiteral> _hashSet;
    private ImmutableHashSet<DbLiteral> _immutableHashSet;


    [GlobalSetup]
    public void GlobalSetup()
    {
        // Dictionary setup
        _dictionaryData = new List<KeyValuePair<DbLiteral, DbLiteral>>(N);
        for (var i = 0; i < N; i++)
        {
            _dictionaryData.Add(new KeyValuePair<DbLiteral, DbLiteral>(new DbLiteral(i), new DbLiteral(i * 2)));
        }
        _dbDictionary = DbDictionary.Create(_dictionaryData);
        _dictionary = new Dictionary<DbLiteral, DbLiteral>(_dictionaryData);
        _immutableDictionary = ImmutableDictionary.CreateRange(_dictionaryData);

        // List setup
        _listData = new List<DbLiteral>(N);
        for (var i = 0; i < N; i++)
        {
            _listData.Add(new DbLiteral(i));
        }
        _dbList = DbList.Create(_listData);
        _list = new List<DbLiteral>(_listData);
        _immutableList = ImmutableList.CreateRange(_listData);

        // Set setup
        _setData = new List<DbLiteral>(N);
        for (var i = 0; i < N; i++)
        {
            _setData.Add(new DbLiteral(i));
        }
        _dbSet = DbSet.Create(_setData);
        _hashSet = new HashSet<DbLiteral>(_setData);
        _immutableHashSet = ImmutableHashSet.CreateRange(_setData);
    }

    // ========================================================================
    // DbDictionary vs Dictionary vs ImmutableDictionary
    // ========================================================================

    // --- Add ---
    [Benchmark]
    public DbDictionary DbDictionary_Add()
    {
        var dict = DbDictionary.Empty;
        foreach (var pair in _dictionaryData)
        {
            dict = dict.Set(pair.Key, pair.Value);
        }
        return dict;
    }

    [Benchmark]
    public Dictionary<DbLiteral, DbLiteral> Dictionary_Add()
    {
        var dict = new Dictionary<DbLiteral, DbLiteral>();
        foreach (var pair in _dictionaryData)
        {
            dict[pair.Key] = pair.Value;
        }
        return dict;
    }

    [Benchmark]
    public ImmutableDictionary<DbLiteral, DbLiteral> ImmutableDictionary_Add()
    {
        var dict = ImmutableDictionary<DbLiteral, DbLiteral>.Empty;
        foreach (var pair in _dictionaryData)
        {
            dict = dict.Add(pair.Key, pair.Value);
        }
        return dict;
    }

    // --- Get ---
    [Benchmark]
    public DbLiteral DbDictionary_Get()
    {
        DbLiteral result = null;
        foreach (var pair in _dictionaryData)
        {
            result = (DbLiteral)_dbDictionary.Get(pair.Key);
        }
        return result;
    }

    [Benchmark]
    public DbLiteral Dictionary_Get()
    {
        DbLiteral result = null;
        foreach (var pair in _dictionaryData)
        {
            result = _dictionary[pair.Key];
        }
        return result;
    }

    [Benchmark]
    public DbLiteral ImmutableDictionary_Get()
    {
        DbLiteral result = null;
        foreach (var pair in _dictionaryData)
        {
            result = _immutableDictionary[pair.Key];
        }
        return result;
    }

    // --- Set (Update) ---
    [Benchmark]
    public DbDictionary DbDictionary_Set()
    {
        var dict = _dbDictionary;
        foreach (var pair in _dictionaryData)
        {
            dict = dict.Set(pair.Key, new DbLiteral(0));
        }
        return dict;
    }

    [Benchmark]
    public Dictionary<DbLiteral, DbLiteral> Dictionary_Set()
    {
        foreach (var pair in _dictionaryData)
        {
            _dictionary[pair.Key] = new DbLiteral(0);
        }
        return _dictionary;
    }

    [Benchmark]
    public ImmutableDictionary<DbLiteral, DbLiteral> ImmutableDictionary_Set()
    {
        var dict = _immutableDictionary;
        foreach (var pair in _dictionaryData)
        {
            dict = dict.SetItem(pair.Key, new DbLiteral(0));
        }
        return dict;
    }

    // --- Remove ---
    [Benchmark]
    public DbDictionary DbDictionary_Remove()
    {
        var dict = _dbDictionary;
        foreach (var pair in _dictionaryData)
        {
            dict = dict.Delete(pair.Key);
        }
        return dict;
    }

    [Benchmark]
    public Dictionary<DbLiteral, DbLiteral> Dictionary_Remove()
    {
        var dict = new Dictionary<DbLiteral, DbLiteral>(_dictionary);
        foreach (var pair in _dictionaryData)
        {
            dict.Remove(pair.Key);
        }
        return dict;
    }

    [Benchmark]
    public ImmutableDictionary<DbLiteral, DbLiteral> ImmutableDictionary_Remove()
    {
        var dict = _immutableDictionary;
        foreach (var pair in _dictionaryData)
        {
            dict = dict.Remove(pair.Key);
        }
        return dict;
    }

    // ========================================================================
    // DbList vs List vs ImmutableList
    // ========================================================================

    // --- Add ---
    [Benchmark]
    public DbList DbList_Add()
    {
        var list = DbList.Empty;
        foreach (var item in _listData)
        {
            list = list.Add(item);
        }
        return list;
    }

    [Benchmark]
    public List<DbLiteral> List_Add()
    {
        var list = new List<DbLiteral>();
        foreach (var item in _listData)
        {
            list.Add(item);
        }
        return list;
    }

    [Benchmark]
    public ImmutableList<DbLiteral> ImmutableList_Add()
    {
        var list = ImmutableList<DbLiteral>.Empty;
        foreach (var item in _listData)
        {
            list = list.Add(item);
        }
        return list;
    }

    // --- Get ---
    [Benchmark]
    public DbObject DbList_Get()
    {
        DbObject result = null;
        for (int i = 0; i < N; i++)
        {
            result = _dbList.Get(i);
        }
        return result;
    }

    [Benchmark]
    public DbLiteral List_Get()
    {
        DbLiteral result = null;
        for (int i = 0; i < N; i++)
        {
            result = _list[i];
        }
        return result;
    }

    [Benchmark]
    public DbLiteral ImmutableList_Get()
    {
        DbLiteral result = null;
        for (int i = 0; i < N; i++)
        {
            result = _immutableList[i];
        }
        return result;
    }

    // --- Set (Update) ---
    [Benchmark]
    public DbList DbList_Set()
    {
        var list = _dbList;
        for (int i = 0; i < N; i++)
        {
            list = list.Set(i, new DbLiteral(0));
        }
        return list;
    }

    [Benchmark]
    public List<DbLiteral> List_Set()
    {
        for (int i = 0; i < N; i++)
        {
            _list[i] = new DbLiteral(0);
        }
        return _list;
    }

    [Benchmark]
    public ImmutableList<DbLiteral> ImmutableList_Set()
    {
        var list = _immutableList;
        for (int i = 0; i < N; i++)
        {
            list = list.SetItem(i, new DbLiteral(0));
        }
        return list;
    }

    // --- Remove ---
    [Benchmark]
    public DbList DbList_Remove()
    {
        var list = _dbList;
        for (int i = 0; i < N; i++)
        {
            list = list.Delete(0);
        }
        return list;
    }

    [Benchmark]
    public List<DbLiteral> List_Remove()
    {
        var list = new List<DbLiteral>(_list);
        for (int i = 0; i < N; i++)
        {
            list.RemoveAt(0);
        }
        return list;
    }

    [Benchmark]
    public ImmutableList<DbLiteral> ImmutableList_Remove()
    {
        var list = _immutableList;
        for (int i = 0; i < N; i++)
        {
            list = list.RemoveAt(0);
        }
        return list;
    }

    // ========================================================================
    // DbSet vs HashSet vs ImmutableHashSet
    // ========================================================================

    // --- Add ---
    [Benchmark]
    public DbSet DbSet_Add()
    {
        var set = DbSet.Empty;
        foreach (var item in _setData)
        {
            set = set.Add(item);
        }
        return set;
    }

    [Benchmark]
    public HashSet<DbLiteral> HashSet_Add()
    {
        var set = new HashSet<DbLiteral>();
        foreach (var item in _setData)
        {
            set.Add(item);
        }
        return set;
    }

    [Benchmark]
    public ImmutableHashSet<DbLiteral> ImmutableHashSet_Add()
    {
        var set = ImmutableHashSet<DbLiteral>.Empty;
        foreach (var item in _setData)
        {
            set = set.Add(item);
        }
        return set;
    }

    // --- Contains ---
    [Benchmark]
    public bool DbSet_Contains()
    {
        bool result = false;
        foreach (var item in _setData)
        {
            result = _dbSet.Contains(item);
        }
        return result;
    }

    [Benchmark]
    public bool HashSet_Contains()
    {
        bool result = false;
        foreach (var item in _setData)
        {
            result = _hashSet.Contains(item);
        }
        return result;
    }

    [Benchmark]
    public bool ImmutableHashSet_Contains()
    {
        bool result = false;
        foreach (var item in _setData)
        {
            result = _immutableHashSet.Contains(item);
        }
        return result;
    }

    // --- Remove ---
    [Benchmark]
    public DbSet DbSet_Remove()
    {
        var set = _dbSet;
        foreach (var item in _setData)
        {
            set = set.Delete(item);
        }
        return set;
    }

    [Benchmark]
    public HashSet<DbLiteral> HashSet_Remove()
    {
        var set = new HashSet<DbLiteral>(_hashSet);
        foreach (var item in _setData)
        {
            set.Remove(item);
        }
        return set;
    }

    [Benchmark]
    public ImmutableHashSet<DbLiteral> ImmutableHashSet_Remove()
    {
        var set = _immutableHashSet;
        foreach (var item in _setData)
        {
            set = set.Remove(item);
        }
        return set;
    }
}