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
            var list = new List<string>(set);
            
            // Order is not guaranteed in a set, so we sort for comparison
            list.Sort();
            items.Sort();

            Assert.That(list, Is.EqualTo(items));
        }
        
        [Test]
        public void ConcurrentUpdate_ShouldReapplyOperations()
        {
            // 1. Initial state
            var initialState = new DbSet<string>(new[] { "a" });

            // 2. Transaction 1 starts and adds "b"
            var tx1 = initialState.Add("b");

            // 3. Transaction 2 starts from the *same initial state* and adds "c"
            var tx2 = initialState.Add("c");

            // 4. Transaction 1 commits, so its state is now the current state.
            var currentState = tx1;

            // 5. Transaction 2 attempts to commit. A concurrency conflict is detected.
            //    We must re-apply the operations from tx2 onto the new current state.
            var finalState = tx2.ConcurrentUpdate(currentState);

            // 6. Verify the final state
            Assert.That(finalState.Count, Is.EqualTo(3));
            Assert.That(finalState.Has("a"), Is.True);
            Assert.That(finalState.Has("b"), Is.True);
            Assert.That(finalState.Has("c"), Is.True);
        }

        private class HashCollisionObject
        {
            public string Value { get; }

            public HashCollisionObject(string value)
            {
                Value = value;
            }

            public override int GetHashCode()
            {
                return 1;
            }

            public override bool Equals(object obj)
            {
                if (obj is HashCollisionObject other)
                {
                    return Value == other.Value;
                }
                return false;
            }
        }

        [Test]
        public void HandleHashCollision()
        {
            var obj1 = new HashCollisionObject("a");
            var obj2 = new HashCollisionObject("b");

            DbSet<HashCollisionObject> newSet = new DbSet<HashCollisionObject>();
            newSet = newSet.Add(obj1);
            newSet = newSet.Add(obj2);

            Assert.That(newSet.Count, Is.EqualTo(2));
            Assert.That(newSet.Has(obj1), Is.True);
            Assert.That(newSet.Has(obj2), Is.True);

            var finalSet = newSet.RemoveAt(obj1);
            Assert.That(finalSet.Count, Is.EqualTo(1));
            Assert.That(finalSet.Has(obj1), Is.False);
            Assert.That(finalSet.Has(obj2), Is.True);
        }
    }
}
