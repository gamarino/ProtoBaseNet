
using NUnit.Framework;
using ProtoBaseNet;
using System.IO;
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
            _storage = new FileStorage(_filePath);
        }

        [TearDown]
        public void TearDown()
        {
            if (File.Exists(_filePath))
            {
                File.Delete(_filePath);
            }
            if (File.Exists($"{_filePath}.root"))
            {
                File.Delete($"{_filePath}.root");
            }
        }

        [Test]
        public async Task PushAndGetBytes()
        {
            var data = new byte[] { 1, 2, 3 };
            var pointer = await _storage.PushBytes(data);
            var retrievedData = await _storage.GetBytes(pointer);
            Assert.That(retrievedData, Is.EqualTo(data));
        }

        [Test]
        public void SetAndReadCurrentRoot()
        {
            var pointer = new AtomPointer();
            _storage.SetCurrentRoot(pointer);
            var currentRoot = _storage.ReadCurrentRoot();
            Assert.That(currentRoot, Is.EqualTo(pointer));
        }
    }
}
