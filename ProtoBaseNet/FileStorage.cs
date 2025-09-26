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

                    var fileLength = stream.Length;
                    _currentPageNumber = (int)((fileLength - RootSize) / _pageSize);
                    _currentPageOffset = (int)((fileLength - RootSize) % _pageSize);
                }
            }
            else
            {
                _currentRoot = new AtomPointer(Guid.Empty, 0);
                _currentPageNumber = 0;
                _currentPageOffset = 0;
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
                var pointer = new AtomPointer(Guid.Empty, (int)((long)pageNumber * _pageSize + pageOffset + RootSize));

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
                            stream.Position = RootSize + (long)pageToWrite.pageNumber * _pageSize;
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
            // First, read the 8-byte length prefix.
            var lengthBytes = ReadData(pointer.Offset, 8);
            var length = BitConverter.ToInt64(lengthBytes, 0);

            // Now, read the actual data.
            var data = ReadData(pointer.Offset + 8, (int)length);
            return Task.FromResult(data);
        }

        private byte[] ReadData(int position, int length)
        {
            var data = new byte[length];
            var remainingLength = length;
            var dataOffset = 0;

            var currentPosition = position;

            while (remainingLength > 0)
            {
                var pageNumber = (currentPosition - RootSize) / _pageSize;
                var pageOffset = (currentPosition - RootSize) % _pageSize;
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
            if (_pageCache.TryGetValue(pageNumber, out var page))
            {
                return page;
            }

            lock (_fileLock)
            {
                using (var stream = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    var pageData = new byte[_pageSize];
                    stream.Position = RootSize + (long)pageNumber * _pageSize;
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
                _lastRootUpdateTime = DateTime.UtcNow;
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
                if (_rootDirty && (DateTime.UtcNow - _lastRootUpdateTime).TotalSeconds > 10)
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
                }
            }
            _rootDirty = false;
        }

        public override void Close()
        {
            Dispose();
        }

        public override void FlushWal()
        {
            // No-op for this implementation
        }

        public override IDisposable RootContextManager()
        {
            return new FileStorageContextManager();
        }

        public void Dispose()
        {
            _disposing = true;
            _rootFlushTimer?.Dispose();
            _writeThread.Join();
            FlushRootInternal();
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