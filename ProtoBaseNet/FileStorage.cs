using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;

namespace ProtoBaseNet
{
    public class FileStorage : SharedStorage, IDisposable
    {
        private const int RootSize = 1024;
        private const int HeaderGap = 512;
        private readonly string _filePath;
        private readonly int _pageSize;
        private readonly int _cacheDepth;
        private readonly object _fileLock = new();
        private readonly object _rootLock = new();

        private AtomPointer _currentRoot;
        private Timer _rootFlushTimer;
        private DateTime _lastRootUpdateTime;
        private bool _rootDirty;

        private readonly ConcurrentDictionary<int, byte[]> _pageCache;
        private readonly ConcurrentQueue<int> _pageCacheLru;

        private readonly byte[] _currentPage;
        private int _currentPageNumber;
        private int _currentPageOffset;

        private readonly ConcurrentQueue<(int pageNumber, byte[] data)> _writeQueue;
        private readonly Thread _writeThread;
        private bool _disposing;

        public FileStorage(string filePath, int pageSize = 1024 * 1024, int cacheDepth = 10)
        {
            _filePath = filePath;
            _pageSize = pageSize;
            _cacheDepth = cacheDepth;

            _pageCache = new ConcurrentDictionary<int, byte[]>();
            _pageCacheLru = new ConcurrentQueue<int>();

            _writeQueue = new ConcurrentQueue<(int, byte[])>();

            if (File.Exists(_filePath))
            {
                using (var stream = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    // ... (la lógica para leer el _currentRoot no cambia) ...
                    if (stream.Length > RootSize)
                    {
                        var rootBytes = new byte[RootSize];
                        stream.Read(rootBytes, 0, RootSize);
                        var content = Encoding.UTF8.GetString(rootBytes).TrimEnd('\0');
                        if (!string.IsNullOrEmpty(content))
                        {
                            var parts = content.Split(',');
                            _currentRoot = new AtomPointer(Guid.Parse(parts[0]), int.Parse(parts[1]));
                        }
                        else
                        {
                            _currentRoot = new AtomPointer(Guid.Empty, 0);
                        }
                    }
                    else
                    {
                        _currentRoot = new AtomPointer(Guid.Empty, 0);
                    }

                    // --- LÓGICA DE ARRANQUE MODIFICADA ---
                    var fileLength = stream.Length;
                    if (fileLength <= _pageSize)
                    {
                        // El archivo solo tiene la página del root, así que empezamos en la página 1.
                        _currentPageNumber = (HeaderGap + pageSize) / pageSize;
                    }
                    else
                    {
                        // El índice de la última página con datos es (fileLength - 1) / pageSize.
                        // La nueva página actual es la siguiente.
                        var lastPageIndex = (int)((fileLength - 1) / _pageSize);
                        _currentPageNumber = lastPageIndex + 1;
                    }

                    // Siempre empezamos al inicio de una página nueva.
                    _currentPageOffset = 0;
                }
            }
            else
            {
                // La lógica para un archivo nuevo ya era correcta al empezar en la página 1.
                _currentRoot = new AtomPointer(Guid.Empty, 0);
                _currentPageNumber = (HeaderGap + pageSize) / pageSize;
                _currentPageOffset = 0;
                FlushRootInternal();
            }
            
            _currentPage = new byte[_pageSize];

            _writeThread = new Thread(ProcessWriteQueue) { IsBackground = true };
            _writeThread.Start();

            _rootFlushTimer = new Timer(FlushRoot, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
            _lastRootUpdateTime = DateTime.UtcNow;
        }

        public override Task<AtomPointer> PushAtom(Atom atom)
        {
            var json = JsonSerializer.Serialize(atom);
            var bytes = Encoding.UTF8.GetBytes(json);
            return PushBytes(bytes);
        }

        public override Task<Atom> GetAtom(AtomPointer pointer)
        {
            var bytes = GetBytes(pointer).Result;
            var json = Encoding.UTF8.GetString(bytes);
            var atom = JsonSerializer.Deserialize<Atom>(json);
            return Task.FromResult(atom!);
        }

        /// <summary>
        /// Writes a byte array to the storage.
        /// The data is prefixed with an 8-byte length, allowing atoms to span multiple pages.
        /// </summary>
        /// <param name="bytes">The byte array to write.</param>
        /// <returns>A pointer to the start of the written data (at the position of the length prefix).</returns>
        public override Task<AtomPointer> PushBytes(byte[] bytes)
        {
            lock (_fileLock)
            {
                var pageNumber = _currentPageNumber;
                var pageOffset = _currentPageOffset;
                var pointer = new AtomPointer(Guid.Empty, (int)((long)pageNumber * _pageSize + pageOffset));

                // Write the length of the atom as an 8-byte long.
                var lengthBytes = BitConverter.GetBytes((long)bytes.Length);
                WriteData(lengthBytes);

                // Write the atom's content.
                WriteData(bytes);

                return Task.FromResult(pointer);
            }
        }

        private void WriteData(byte[] data)
        {
            var remainingLength = data.Length;
            var currentBytesOffset = 0;
            while (remainingLength > 0)
            {
                var bytesToWrite = Math.Min(remainingLength, _pageSize - _currentPageOffset);
                Buffer.BlockCopy(data, currentBytesOffset, _currentPage, _currentPageOffset, bytesToWrite);
                
                remainingLength -= bytesToWrite;
                currentBytesOffset += bytesToWrite;
                _currentPageOffset += bytesToWrite;

                if (_currentPageOffset == _pageSize)
                {
                    EnqueuePageWrite();
                    _currentPageNumber++;
                    _currentPageOffset = 0;
                }
            }
        }

        private void EnqueuePageWrite()
        {
            var pageData = new byte[_pageSize];
            Buffer.BlockCopy(_currentPage, 0, pageData, 0, _pageSize);
            _writeQueue.Enqueue((_currentPageNumber, pageData));
            Array.Clear(_currentPage, 0, _pageSize);
        }

        private void ProcessWriteQueue()
        {
            while (!_disposing)
            {
                if (_writeQueue.TryDequeue(out var pageToWrite))
                {
                    lock (_fileLock)
                    {
                        using (var stream = new FileStream(_filePath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None))
                        {
                            stream.Position = (long)pageToWrite.pageNumber * _pageSize;
                            stream.Write(pageToWrite.data, 0, pageToWrite.data.Length);
                        }
                    }
                    AddToCache(pageToWrite.pageNumber, pageToWrite.data);
                }
                else
                {
                    Thread.Sleep(100);
                }
            }
        }

        /// <summary>
        /// Retrieves a byte array from the storage using the provided pointer.
        /// This method first reads an 8-byte length prefix to determine the atom's size,
        /// then reads the corresponding data, handling atoms that span multiple pages.
        /// </summary>
        /// <param name="pointer">The pointer to the atom's data.</param>
        /// <returns>The retrieved byte array.</returns>
        public override Task<byte[]> GetBytes(AtomPointer pointer)
        {
            lock (_fileLock)
            {
                // First, read the 8-byte length prefix.
                var lengthBytes = ReadData(pointer.Offset, 8);
                var length = BitConverter.ToInt64(lengthBytes, 0);

                // Now, read the actual data.
                var data = ReadData(pointer.Offset + 8, (int)length);
                return Task.FromResult(data);
            }
        }

        private byte[] ReadData(int position, int length)
        {
            var data = new byte[length];
            var remainingLength = length;
            var dataOffset = 0;

            var currentPosition = position;

            while (remainingLength > 0)
            {
                var pageNumber = currentPosition / _pageSize;
                var pageOffset = currentPosition % _pageSize;
                var page = GetPage(pageNumber);

                var bytesToRead = Math.Min(remainingLength, _pageSize - pageOffset);
                Buffer.BlockCopy(page, pageOffset, data, dataOffset, bytesToRead);

                remainingLength -= bytesToRead;
                dataOffset += bytesToRead;
                currentPosition += bytesToRead;
            }

            return data;
        }

        private byte[] GetPage(int pageNumber)
        {
            // Check if it is currentPage
            if (pageNumber == _currentPageNumber)
            {
                return _currentPage;
            }

            // Check if it is in the write queue
            foreach (var item in _writeQueue)
            {
                if (item.pageNumber == pageNumber)
                    return item.data;
            }
            
            // Check if it is in the cache
            if (_pageCache.TryGetValue(pageNumber, out var page))
            {
                return page;
            }
            
            // Last resort, read it from the file
            lock (_fileLock)
            {
                using (var stream = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    var pageData = new byte[_pageSize];
                    stream.Position = (long)pageNumber * _pageSize;
                    stream.Read(pageData, 0, _pageSize);
                    AddToCache(pageNumber, pageData);
                    return pageData;
                }
            }
        }

        private void AddToCache(int pageNumber, byte[] data)
        {
            if (_pageCache.Count >= _cacheDepth)
            {
                if (_pageCacheLru.TryDequeue(out var lruPageNumber))
                {
                    _pageCache.TryRemove(lruPageNumber, out _);
                }
            }
            _pageCache[pageNumber] = data;
            _pageCacheLru.Enqueue(pageNumber);
        }

        public override Task<IDictionary<string, object>> ReadAtom(AtomPointer atomPointer)
        {
            var bytes = GetBytes(atomPointer).Result;
            var json = Encoding.UTF8.GetString(bytes);
            return Task.FromResult<IDictionary<string, object>>(JsonSerializer.Deserialize<Dictionary<string, object>>(json)!);
        }

        public override Task<AtomPointer> WriteAtom(IDictionary<string, object> data)
        {
            var json = JsonSerializer.Serialize(data);
            var bytes = Encoding.UTF8.GetBytes(json);
            return PushBytes(bytes);
        }

        public override void SetCurrentRoot(AtomPointer pointer)
        {
            lock (_rootLock)
            {
                _currentRoot = pointer;
                _rootDirty = true;
            }
        }

        public override AtomPointer ReadCurrentRoot()
        {
            lock (_rootLock)
            {
                return _currentRoot;
            }
        }

        private void FlushRoot(object? state)
        {
            lock (_rootLock)
            {
                if ((_lastRootUpdateTime != default) || 
                    (_rootDirty && (DateTime.UtcNow - _lastRootUpdateTime).TotalSeconds > 10))
                {
                    FlushRootInternal();
                }
            }
        }

        private void FlushRootInternal()
        {
            // Ensure all pages are written before flushing the root
            while (!_writeQueue.IsEmpty)
            {
                Thread.Sleep(100);
            }
            
            lock (_fileLock)
            {
                using (var stream = new FileStream(_filePath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None))
                {
                    var content = $"{_currentRoot.TransactionId},{_currentRoot.Offset}";
                    var bytes = Encoding.UTF8.GetBytes(content);
                    var rootBytes = new byte[RootSize];
                    Buffer.BlockCopy(bytes, 0, rootBytes, 0, bytes.Length);
                    stream.Position = 0;
                    stream.Write(rootBytes, 0, RootSize);
                    _rootDirty = false;
                    _lastRootUpdateTime = DateTime.Now;
                }
            }
        }

        public override void Close()
        {
            Dispose();
        }

        public override void FlushWal()
        {
            EnqueuePageWrite();
            _currentPageNumber++;
            _currentPageOffset = 0;
        }

        public override IDisposable RootContextManager()
        {
            return new FileStorageContextManager();
        }

        public void Dispose()
        {
            FlushWal();
            _lastRootUpdateTime = default;
            FlushRootInternal();
            _disposing = true;
            _rootFlushTimer?.Dispose();
            _writeThread.Join();
        }

        private class FileStorageContextManager : IDisposable
        {
            public void Dispose()
            {
                // No action needed for file storage
            }
        }
    }
}