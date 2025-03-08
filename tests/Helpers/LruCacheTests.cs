using System;
using System.Linq;
using Illustra.Helpers;
using NUnit.Framework;

namespace Illustra.Tests.Helpers
{
    [TestFixture]
    public class LruCacheTests
    {
        [Test]
        public void Constructor_WithInvalidCapacity_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => new LruCache<string, int>(0));
            Assert.Throws<ArgumentException>(() => new LruCache<string, int>(-1));
        }

        [Test]
        public void Add_WithinCapacity_StoresItems()
        {
            var cache = new LruCache<string, int>(3);
            cache.Add("one", 1);
            cache.Add("two", 2);

            int value;
            Assert.IsTrue(cache.TryGetValue("one", out value));
            Assert.AreEqual(1, value);
            Assert.IsTrue(cache.TryGetValue("two", out value));
            Assert.AreEqual(2, value);
        }

        [Test]
        public void Add_BeyondCapacity_RemovesOldestItem()
        {
            var cache = new LruCache<string, int>(2);
            cache.Add("one", 1);
            cache.Add("two", 2);
            cache.Add("three", 3);

            int value;
            Assert.IsFalse(cache.TryGetValue("one", out value), "最も古いアイテムが削除されていません");
            Assert.IsTrue(cache.TryGetValue("two", out value));
            Assert.IsTrue(cache.TryGetValue("three", out value));
        }

        [Test]
        public void Access_UpdatesOrder()
        {
            var cache = new LruCache<string, int>(3);
            cache.Add("one", 1);
            cache.Add("two", 2);
            cache.Add("three", 3);

            // "one"にアクセスして最新にする
            int value;
            cache.TryGetValue("one", out value);

            // 新しいアイテムを追加
            cache.Add("four", 4);

            // "two"が最も古いため削除されるはず
            Assert.IsTrue(cache.TryGetValue("one", out value));
            Assert.IsFalse(cache.TryGetValue("two", out value));
            Assert.IsTrue(cache.TryGetValue("three", out value));
            Assert.IsTrue(cache.TryGetValue("four", out value));
        }

        [Test]
        public void GetKeys_ReturnsKeysInOrder()
        {
            var cache = new LruCache<string, int>(3);
            cache.Add("one", 1);
            cache.Add("two", 2);
            cache.Add("three", 3);

            // "one"にアクセス
            int value;
            cache.TryGetValue("one", out value);

            var keys = cache.GetKeys().ToList();
            Assert.AreEqual("one", keys[0]); // 最新
            Assert.AreEqual("three", keys[1]);
            Assert.AreEqual("two", keys[2]); // 最古
        }

        [Test]
        public void Clear_RemovesAllItems()
        {
            var cache = new LruCache<string, int>(3);
            cache.Add("one", 1);
            cache.Add("two", 2);
            cache.Add("three", 3);

            cache.Clear();

            int value;
            Assert.IsFalse(cache.TryGetValue("one", out value));
            Assert.IsFalse(cache.TryGetValue("two", out value));
            Assert.IsFalse(cache.TryGetValue("three", out value));
            Assert.AreEqual(0, cache.Count);
        }

        [Test]
        public void Add_ExistingKey_UpdatesValue()
        {
            var cache = new LruCache<string, int>(3);
            cache.Add("one", 1);
            cache.Add("one", 10);

            int value;
            Assert.IsTrue(cache.TryGetValue("one", out value));
            Assert.AreEqual(10, value);
            Assert.AreEqual(1, cache.Count);
        }

        [Test]
        public void Count_ReflectsNumberOfItems()
        {
            var cache = new LruCache<string, int>(3);
            Assert.AreEqual(0, cache.Count);

            cache.Add("one", 1);
            Assert.AreEqual(1, cache.Count);

            cache.Add("two", 2);
            Assert.AreEqual(2, cache.Count);

            cache.Add("three", 3);
            Assert.AreEqual(3, cache.Count);

            cache.Add("four", 4); // これにより"one"が削除される
            Assert.AreEqual(3, cache.Count);
        }
    }
}
