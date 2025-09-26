using NUnit.Framework;
using ProtoBaseNet;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace MainTests
{
    [TestFixture]
    public class FileStorageTests
    {
        private FileStorage _storage;
        private string _filePath = "test.db";

        [SetUp]
        public void Setup()
        {
            if (File.Exists(_filePath))
            {
                File.Delete(_filePath);
            }
            _storage = new FileStorage(_filePath, pageSize: 1024);
        }

        [TearDown]
        public void TearDown()
        {
            _storage.Dispose();
            if (File.Exists(_filePath))
            { 
                File.Delete(_filePath);
            }
        }

        [Test]
        public async Task PushAndGetBytes_SinglePage()
        {
            var data = Encoding.UTF8.GetBytes("hello world");
            var pointer = await _storage.PushBytes(data);
            var retrievedData = await _storage.GetBytes(pointer);
            
            Assert.That(retrievedData, Is.EqualTo(data));
        }
        
        [Test]
        public async Task WriteAndReadAtom_SinglePage()
        {
            var data = new Dictionary<string, object> { { "key", "value" } };
            var pointer = await _storage.WriteAtom(data);
            var retrievedData = await _storage.ReadAtom(pointer);
            Assert.That(((JsonElement)retrievedData["key"]).GetString(), Is.EqualTo("value"));
        }

        [Test]
        public void SetAndReadCurrentRoot()
        {
            var pointer = new AtomPointer();
            _storage.SetCurrentRoot(pointer);
            var currentRoot = _storage.ReadCurrentRoot();
            Assert.That(currentRoot, Is.EqualTo(pointer));
        }
        
        [Test]
        public async Task PushAndGetBytes_MultiPage()
        {
            var data = new byte[2048];
            for (int i = 0; i < data.Length; i++)
            {
                data[i] = (byte)(i % 256);
            }

            var pointer = await _storage.PushBytes(data);
            var retrievedData = await _storage.GetBytes(pointer);
            
            Assert.That(retrievedData, Is.EqualTo(data));
        }
    }
}