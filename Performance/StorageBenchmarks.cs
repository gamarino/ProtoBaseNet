
using System.IO;
using BenchmarkDotNet.Attributes;
using ProtoBaseNet;

namespace Performance;

[MemoryDiagnoser]
public class StorageBenchmarks
{
    [Params(100, 1000)]
    public int N;

    private MemoryStorage _memoryStorage;
    private FileStorage _fileStorage;
    private string _fileStoragePath;
    private DbDictionary _testObject;
    private List<long> _objectIds;

    [GlobalSetup]
    public void GlobalSetup()
    {
        // Setup FileStorage
        _fileStoragePath = Path.Combine(Path.GetTempPath(), "ProtoBaseNet_PerfTest");
        if (Directory.Exists(_fileStoragePath))
        {
            Directory.Delete(_fileStoragePath, true);
        }
        Directory.CreateDirectory(_fileStoragePath);
        _fileStorage = new FileStorage(_fileStoragePath);

        // Setup MemoryStorage
        _memoryStorage = new MemoryStorage();

        // Create a test object
        _testObject = DbDictionary.Create(new[]
        {
            new KeyValuePair<DbLiteral, DbObject>(new DbLiteral("name"), new DbLiteral("Test Object")),
            new KeyValuePair<DbLiteral, DbObject>(new DbLiteral("value"), new DbLiteral(12345)),
            new KeyValuePair<DbLiteral, DbObject>(new DbLiteral("timestamp"), new DbLiteral(DateTime.UtcNow.Ticks))
        });
        
        // Pre-populate storages for read tests
        _objectIds = new List<long>(N);
        for (int i = 0; i < N; i++)
        {
            var tx = _fileStorage.BeginTransaction();
            var newObj = tx.WriteObject(_testObject);
            tx.Commit();
            _objectIds.Add(newObj.Id);
            
            var memTx = _memoryStorage.BeginTransaction();
            memTx.WriteObject(_testObject);
            memTx.Commit();
        }
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        _fileStorage.Dispose();
        if (Directory.Exists(_fileStoragePath))
        {
            Directory.Delete(_fileStoragePath, true);
        }
    }

    // --- Transactional Write ---
    [Benchmark]
    public void MemoryStorage_Transaction()
    {
        for (int i = 0; i < N; i++)
        {
            var tx = _memoryStorage.BeginTransaction();
            var newObj = tx.WriteObject(_testObject);
            var readObj = tx.ReadObject(newObj.Id);
            tx.Commit();
        }
    }

    [Benchmark]
    public void FileStorage_Transaction()
    {
        for (int i = 0; i < N; i++)
        {
            var tx = _fileStorage.BeginTransaction();
            var newObj = tx.WriteObject(_testObject);
            var readObj = tx.ReadObject(newObj.Id);
            tx.Commit();
        }
    }

    // --- Bulk Write (Non-Transactional) ---
    [Benchmark]
    public void MemoryStorage_BulkWrite()
    {
        var storage = new MemoryStorage();
        for (int i = 0; i < N; i++)
        {
            var tx = storage.BeginTransaction();
            tx.WriteObject(_testObject);
            tx.Commit();
        }
    }
    
    [Benchmark]
    public void FileStorage_BulkWrite()
    {
        var storage = new FileStorage(_fileStoragePath, true);
        for (int i = 0; i < N; i++)
        {
            var tx = storage.BeginTransaction();
            tx.WriteObject(_testObject);
            tx.Commit();
        }
        storage.Dispose();
    }

    // --- Bulk Read ---
    [Benchmark]
    public void MemoryStorage_BulkRead()
    {
        var tx = _memoryStorage.BeginTransaction();
        foreach (var id in _objectIds)
        {
            tx.ReadObject(id);
        }
        tx.Commit();
    }

    [Benchmark]
    public void FileStorage_BulkRead()
    {
        var tx = _fileStorage.BeginTransaction();
        foreach (var id in _objectIds)
        {
            tx.ReadObject(id);
        }
        tx.Commit();
    }
}
