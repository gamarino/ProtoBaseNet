using NUnit.Framework;
using ProtoBaseNet;
using System.Threading.Tasks;

namespace MainTests
{
    public class TestAtom : Atom
    {
        public TestAtom(ObjectTransaction? transaction = null, AtomPointer? atomPointer = null) : base(transaction, atomPointer)
        {
        }
    }

    [TestFixture]
    public class MemoryStorageTests
    {
        private MemoryStorage _storage;

        [SetUp]
        public void Setup()
        {
            _storage = new MemoryStorage();
        }

        [Test]
        public async Task PushAndGetAtom()
        {
            var atom = new TestAtom();
            var pointer = await _storage.PushAtom(atom);
            var retrievedAtom = await _storage.GetAtom(pointer);
            Assert.That(retrievedAtom, Is.EqualTo(atom));
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

        [Test]
        public async Task CloseClearsData()
        {
            var atom = new TestAtom();
            var pointer = await _storage.PushAtom(atom);
            
            _storage.Close();

            Assert.ThrowsAsync<System.Collections.Generic.KeyNotFoundException>(async () => await _storage.GetAtom(pointer));
        }
    }
}
