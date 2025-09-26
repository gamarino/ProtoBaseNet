using NUnit.Framework;
using ProtoBaseNet;
using System.Collections.Generic;
using System.Linq;

namespace MainTests
{
    [TestFixture]
    public class DbListTests
    {
        [Test]
        public void CreateEmptyList()
        {
            var list = new DbList<int>();
            Assert.That(list.Count, Is.EqualTo(0));
        }

        [Test]
        public void CreateListWithInitialItems()
        {
            var items = new List<int> { 1, 2, 3 };
            var list = new DbList<int>();
            foreach (var item in items)
            {
                list = list.AppendLast(item);
            }
            Assert.That(list.Count, Is.EqualTo(3));
            Assert.That(list.GetAt(0), Is.EqualTo(1));
            Assert.That(list.GetAt(1), Is.EqualTo(2));
            Assert.That(list.GetAt(2), Is.EqualTo(3));
        }

        [Test]
        public void AppendItem()
        {
            var list = new DbList<int>();
            var newList = list.AppendLast(10);

            Assert.That(list.Count, Is.EqualTo(0)); // Original list is immutable
            Assert.That(newList.Count, Is.EqualTo(1));
            Assert.That(newList.GetAt(0), Is.EqualTo(10));
        }

        [Test]
        public void SetItem()
        {
            var items = new List<int> { 1, 2, 3 };
            var list = new DbList<int>();
            foreach (var item in items)
            {
                list = list.AppendLast(item);
            }
            var newList = list.SetAt(1, 99);

            Assert.That(list.GetAt(1), Is.EqualTo(2)); // Original list is immutable
            Assert.That(newList.GetAt(1), Is.EqualTo(99));
            Assert.That(newList.Count, Is.EqualTo(3));
        }

        [Test]
        public void DeleteItem()
        {
            var items = new List<int> { 1, 2, 3 };
            var list = new DbList<int>();
            foreach (var item in items)
            {
                list = list.AppendLast(item);
            }
            var newList = list.RemoveAt(1);

            Assert.That(list.Count, Is.EqualTo(3)); // Original list is immutable
            Assert.That(newList.Count, Is.EqualTo(2));
            Assert.That(newList.GetAt(0), Is.EqualTo(1));
            Assert.That(newList.GetAt(1), Is.EqualTo(3));
        }

        [Test]
        public void ToList()
        {
            var items = new List<int> { 1, 2, 3 };
            var list = new DbList<int>();
            foreach (var item in items)
            {
                list = list.AppendLast(item);
            }
            var nativeList = list.ToList();
            Assert.That(nativeList, Is.EqualTo(items));
        }
    }
}
