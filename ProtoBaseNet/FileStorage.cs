
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace ProtoBaseNet
{
    public class FileStorage : SharedStorage
    {
        private readonly string _filePath;
        private readonly object _lock = new();

        public FileStorage(string filePath)
        {
            _filePath = filePath;
        }

        public override async Task<AtomPointer> PushAtom(Atom atom)
        {
            var json = System.Text.Json.JsonSerializer.Serialize(atom);
            var bytes = Encoding.UTF8.GetBytes(json);
            return await PushBytes(bytes);
        }

        public override async Task<Atom> GetAtom(AtomPointer pointer)
        {
            var bytes = await GetBytes(pointer);
            var json = Encoding.UTF8.GetString(bytes);
            var atom = System.Text.Json.JsonSerializer.Deserialize<Atom>(json);
            return atom!;
        }

        public override async Task<AtomPointer> PushBytes(byte[] bytes)
        {
            lock (_lock)
            {
                using (var stream = new FileStream(_filePath, FileMode.Append, FileAccess.Write, FileShare.None))
                using (var writer = new StreamWriter(stream, Encoding.UTF8))
                {
                    var base64 = Convert.ToBase64String(bytes);
                    writer.WriteLine(base64);
                    return new AtomPointer(Guid.Empty, (int)(writer.BaseStream.Position / writer.NewLine.Length) - 1);
                }
            }
        }

        public override async Task<byte[]> GetBytes(AtomPointer pointer)
        {
            lock (_lock)
            {
                using (var stream = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    for (int i = 0; i < pointer.Offset; i++)
                    {
                        reader.ReadLine();
                    }
                    var line = reader.ReadLine();
                    if (line == null)
                    {
                        throw new KeyNotFoundException("Invalid atom pointer");
                    }
                    return Convert.FromBase64String(line);
                }
            }
        }

        public override void SetCurrentRoot(AtomPointer pointer)
        {
            lock (_lock)
            {
                File.WriteAllText($"{_filePath}.root", $"{pointer.TransactionId},{pointer.Offset}");
            }
        }

        public override AtomPointer ReadCurrentRoot()
        {
            lock (_lock)
            {
                var rootFilePath = $"{_filePath}.root";
                if (!File.Exists(rootFilePath))
                {
                    return new AtomPointer(Guid.Empty, 0);
                }
                var content = File.ReadAllText(rootFilePath);
                var parts = content.Split(',');
                return new AtomPointer(Guid.Parse(parts[0]), int.Parse(parts[1]));
            }
        }

        public override void Close()
        {
            lock (_lock)
            {
                // No action needed for file storage
            }
        }

        public override void FlushWal()
        {
            // No-op for file storage
        }

        public override IDisposable RootContextManager()
        {
            return new FileStorageContextManager();
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
