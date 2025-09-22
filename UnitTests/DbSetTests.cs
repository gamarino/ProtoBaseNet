using NUnit.Framework;
using ProtoBaseNet;
using System.Collections.Generic;
using System.Linq;

namespace MainTests
{
    [TestFixture]
    public class DbSetTests
    {
        [Test]
        public void CreateEmptySet()
        {
            var set = new DbSet<string>();
            Assert.That(set.Count, Is.EqualTo(0));
        }

        [Test]
        public void CreateSetWithInitialItems()
        {
            var items = new List<string> { "a", "b", "c" };
            var set = new DbSet<string>(items);
            Assert.That(set.Count, Is.EqualTo(3));
            Assert.That(set.Has("a"), Is.True);
        }

        [Test]
        public void AddItem()
        {
            var set = new DbSet<string>();
            var newSet = set.Add("hello");

            Assert.That(set.Count, Is.EqualTo(0)); // Original set is immutable
            Assert.That(newSet.Count, Is.EqualTo(1));
            Assert.That(newSet.Has("hello"), Is.True);
        }

        [Test]
        public void AddDuplicateItem()
        {
            var items = new List<string> { "a" };
            var set = new DbSet<string>(items);
            var newSet = set.Add("a");

            Assert.That(set.Count, Is.EqualTo(1));
            Assert.That(newSet.Count, Is.EqualTo(1));
        }

        [Test]
        public void RemoveItem()
        {
            var items = new List<string> { "a", "b" };
            var set = new DbSet<string>(items);
            var newSet = set.RemoveAt("a");

            Assert.That(set.Count, Is.EqualTo(2)); // Original set is immutable
            Assert.That(newSet.Count, Is.EqualTo(1));
            Assert.That(newSet.Has("a"), Is.False);
            Assert.That(newSet.Has("b"), Is.True);
        }

        [Test]
        public void ToList()
        {
            var items = new List<string> { "c", "a", "b" };
            var set = new DbSet<string>(items);
            var list = new List<string>(set.AsIterable());
            
            // Order is not guaranteed in a set, so we sort for comparison
            list.Sort();
            items.Sort();

            Assert.That(list, Is.EqualTo(items));
        }
    }
}
