using NUnit.Framework;
using ProtoBaseNet;
using System.Collections.Generic;
using System.Linq;

namespace MainTests
{
    [TestFixture]
    public class DbDictionaryTests
    {
        [Test]
        public void CreateEmptyDictionary()
        {
            var dict = new DbDictionary<string>();
            Assert.That(dict.Count, Is.EqualTo(0));
        }

        [Test]
        public void CreateDictionaryWithInitialItems()
        {
            var items = new Dictionary<string, string> { { "key1", "value1" }, { "key2", "value2" } };
            var dict = new DbDictionary<string>();
            foreach (var (key, value) in items)
                dict = dict.SetAt(key, value);
            Assert.That(dict.Count, Is.EqualTo(2));
            Assert.That(dict.GetAt("key1"), Is.EqualTo("value1"));
        }

        [Test]
        public void SetAtNewKey()
        {
            var dict = new DbDictionary<string>();
            var newDict = dict.SetAt("key", "value");

            Assert.That(dict.Count, Is.EqualTo(0)); // Original dict is immutable
            Assert.That(newDict.Count, Is.EqualTo(1));
            Assert.That(newDict.GetAt("key"), Is.EqualTo("value"));
        }

        [Test]
        public void SetAtExistingKey()
        {
            var items = new Dictionary<string, string> { { "key", "old_value" } };
            var dict = new DbDictionary<string>();
            foreach (var (key, value) in items)
                dict = dict.SetAt(key, value);
            var newDict = dict.SetAt("key", "new_value");

            Assert.That(dict.GetAt("key"), Is.EqualTo("old_value")); // Original dict is immutable
            Assert.That(newDict.GetAt("key"), Is.EqualTo("new_value"));
            Assert.That(newDict.Count, Is.EqualTo(1));
        }

        [Test]
        public void Delete()
        {
            var items = new Dictionary<string, string> { { "key1", "value1" }, { "key2", "value2" } };
            var dict = new DbDictionary<string>();
            foreach (var (key, value) in items)
                dict = dict.SetAt(key, value);

            var newDict = dict.RemoveAt("key1");

            Assert.That(dict.Count, Is.EqualTo(2)); // Original dict is immutable
            Assert.That(newDict.Count, Is.EqualTo(1));
            Assert.That(newDict.Has("key1"), Is.False);
            Assert.That(newDict.Has("key2"), Is.True);
        }

        [Test]
        public void ToDictionary()
        {
            var items = new Dictionary<string, string> { { "key1", "value1" }, { "key2", "value2" } };
            var dict = new DbDictionary<string>();
            foreach (var (key, value) in items)
                dict = dict.SetAt(key, value);

            var nativeDict = new Dictionary<string, string>();
            foreach (var (key, value) in dict)
            {
                nativeDict[(string)key] = value;
            }
            Assert.That(nativeDict, Is.EqualTo(items));
        }
    }
}
