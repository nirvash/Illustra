using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Illustra.Models;
using Illustra.Services;
using NUnit.Framework;

namespace Illustra.Tests.Services
{
    [TestFixture]
    public class OperationCacheTests
    {
        private OperationCache _cache;
        private ImageCollectionModel _imageCollection;

        [SetUp]
        public void Setup()
        {
            _imageCollection = new ImageCollectionModel();
            _cache = new OperationCache();
        }

        [Test]
        public void Constructor_InitializesProperties()
        {
            // Assert
            Assert.NotNull(_cache);
            Assert.AreEqual(0, _cache.Count);
        }

        [Test]
        public void AddOperation_IncreasesCount()
        {
            // Arrange
            var operation = new Func<Task<IList<FileNodeModel>>>(() =>
                Task.FromResult<IList<FileNodeModel>>(new List<FileNodeModel>()));

            // Act
            _cache.AddOperation("test_key", operation);

            // Assert
            Assert.AreEqual(1, _cache.Count);
        }

        [Test]
        public void GetOperation_ReturnsOperation()
        {
            // Arrange
            var operation = new Func<Task<IList<FileNodeModel>>>(() =>
                Task.FromResult<IList<FileNodeModel>>(new List<FileNodeModel>()));
            _cache.AddOperation("test_key", operation);

            // Act
            var result = _cache.GetOperation("test_key");

            // Assert
            Assert.NotNull(result);
            Assert.AreEqual(operation, result);
        }

        [Test]
        public void GetOperation_WithNonExistentKey_ReturnsNull()
        {
            // Act
            var result = _cache.GetOperation("non_existent_key");

            // Assert
            Assert.IsNull(result);
        }

        [Test]
        public void RemoveOperation_DecreasesCount()
        {
            // Arrange
            var operation = new Func<Task<IList<FileNodeModel>>>(() =>
                Task.FromResult<IList<FileNodeModel>>(new List<FileNodeModel>()));
            _cache.AddOperation("test_key", operation);

            // Act
            _cache.RemoveOperation("test_key");

            // Assert
            Assert.AreEqual(0, _cache.Count);
        }

        [Test]
        public void Clear_RemovesAllOperations()
        {
            // Arrange
            var operation1 = new Func<Task<IList<FileNodeModel>>>(() =>
                Task.FromResult<IList<FileNodeModel>>(new List<FileNodeModel>()));
            var operation2 = new Func<Task<IList<FileNodeModel>>>(() =>
                Task.FromResult<IList<FileNodeModel>>(new List<FileNodeModel>()));

            _cache.AddOperation("key1", operation1);
            _cache.AddOperation("key2", operation2);

            // Act
            _cache.Clear();

            // Assert
            Assert.AreEqual(0, _cache.Count);
            Assert.IsNull(_cache.GetOperation("key1"));
            Assert.IsNull(_cache.GetOperation("key2"));
        }

        [Test]
        public void AddOperation_ExceedingMaxSize_RemovesOldestOperation()
        {
            // Arrange - Set max size to 2
            _cache = new OperationCache(2);

            var operation1 = new Func<Task<IList<FileNodeModel>>>(() =>
                Task.FromResult<IList<FileNodeModel>>(new List<FileNodeModel>()));
            var operation2 = new Func<Task<IList<FileNodeModel>>>(() =>
                Task.FromResult<IList<FileNodeModel>>(new List<FileNodeModel>()));
            var operation3 = new Func<Task<IList<FileNodeModel>>>(() =>
                Task.FromResult<IList<FileNodeModel>>(new List<FileNodeModel>()));

            // Act
            _cache.AddOperation("key1", operation1);
            _cache.AddOperation("key2", operation2);
            _cache.AddOperation("key3", operation3); // This should remove key1

            // Assert
            Assert.AreEqual(2, _cache.Count);
            Assert.IsNull(_cache.GetOperation("key1")); // key1 should be removed
            Assert.NotNull(_cache.GetOperation("key2"));
            Assert.NotNull(_cache.GetOperation("key3"));
        }

        [Test]
        public void AddToCache_GetFromCache_ReturnsCorrectData()
        {
            // Arrange
            var cache = new OperationCache();
            var testData = "Test Data";

            // Act
            cache.AddToCache("Test", "Param1", testData);
            var result = cache.GetFromCache<string>("Test", "Param1");

            // Assert
            Assert.AreEqual(testData, result);
        }

        [Test]
        public void GetFromCache_WithNonExistentKey_ReturnsDefault()
        {
            // Arrange
            var cache = new OperationCache();

            // Act
            var result = cache.GetFromCache<string>("NonExistent", "Param");

            // Assert
            Assert.IsNull(result);
        }

        [Test]
        public void InvalidateCache_RemovesEntriesForSpecificOperationType()
        {
            // Arrange
            var cache = new OperationCache();
            cache.AddToCache("Type1", "Param1", "Data1");
            cache.AddToCache("Type1", "Param2", "Data2");
            cache.AddToCache("Type2", "Param1", "Data3");

            // Act
            cache.InvalidateCache("Type1");

            // Assert
            Assert.IsNull(cache.GetFromCache<string>("Type1", "Param1"));
            Assert.IsNull(cache.GetFromCache<string>("Type1", "Param2"));
            Assert.AreEqual("Data3", cache.GetFromCache<string>("Type2", "Param1"));
        }

        [Test]
        public void ClearCache_RemovesAllEntries()
        {
            // Arrange
            var cache = new OperationCache();
            cache.AddToCache("Type1", "Param1", "Data1");
            cache.AddToCache("Type2", "Param1", "Data2");

            // Act
            cache.ClearCache();

            // Assert
            Assert.IsNull(cache.GetFromCache<string>("Type1", "Param1"));
            Assert.IsNull(cache.GetFromCache<string>("Type2", "Param1"));
        }

        [Test]
        public void UpdatePromptCache_StoresPromptInfo()
        {
            // Arrange
            var cache = new OperationCache();
            var filePath = "test.jpg";

            // Act
            cache.UpdatePromptCache(filePath, true);

            // Assert
            Assert.IsTrue(cache.PromptCache.ContainsKey(filePath));
            Assert.IsTrue(cache.PromptCache[filePath]);
        }

        [Test]
        public void UpdateTagCache_StoresTagInfo()
        {
            // Arrange
            var cache = new OperationCache();
            var filePath = "test.jpg";
            var tags = new List<string> { "tag1", "tag2" };

            // Act
            cache.UpdateTagCache(filePath, tags);

            // Assert
            Assert.IsTrue(cache.TagCache.ContainsKey(filePath));
            Assert.AreEqual(tags, cache.TagCache[filePath]);
        }

        [Test]
        public void CacheFilterResult_GetCachedFilterResult_WorksCorrectly()
        {
            // Arrange
            var cache = new OperationCache();
            var ratingFilter = 3;
            var result = new List<FileNodeModel>
            {
                new FileNodeModel("test1.jpg") { Rating = 3 },
                new FileNodeModel("test2.jpg") { Rating = 5 }
            };

            // Act
            cache.CacheFilterResult(ratingFilter, result);
            var cachedResult = cache.GetCachedFilterResult(ratingFilter);

            // Assert
            Assert.NotNull(cachedResult);
            Assert.AreEqual(2, cachedResult.Count);
            Assert.AreEqual("test1.jpg", cachedResult[0].FileName);
            Assert.AreEqual("test2.jpg", cachedResult[1].FileName);
        }

        [Test]
        public void CacheSortResult_GetCachedSortResult_WorksCorrectly()
        {
            // Arrange
            var cache = new OperationCache();
            var sortByDate = true;
            var sortAscending = false;
            var result = new List<FileNodeModel>
            {
                new FileNodeModel("test2.jpg") { LastModified = DateTime.Now },
                new FileNodeModel("test1.jpg") { LastModified = DateTime.Now.AddDays(-1) }
            };

            // Act
            cache.CacheSortResult(sortByDate, sortAscending, result);
            var cachedResult = cache.GetCachedSortResult(sortByDate, sortAscending);

            // Assert
            Assert.NotNull(cachedResult);
            Assert.AreEqual(2, cachedResult.Count);
            Assert.AreEqual("test2.jpg", cachedResult[0].FileName);
            Assert.AreEqual("test1.jpg", cachedResult[1].FileName);
        }

        [Test]
        public void MaxCacheSize_LimitsNumberOfEntries()
        {
            // Arrange
            var cache = new OperationCache(maxCacheSize: 2);

            // Act
            cache.AddToCache("Type1", "Param1", "Data1");
            cache.AddToCache("Type2", "Param1", "Data2");
            cache.AddToCache("Type3", "Param1", "Data3"); // This should evict the oldest entry

            // Assert
            Assert.IsNull(cache.GetFromCache<string>("Type1", "Param1")); // Should be evicted
            Assert.AreEqual("Data2", cache.GetFromCache<string>("Type2", "Param1"));
            Assert.AreEqual("Data3", cache.GetFromCache<string>("Type3", "Param1"));
        }
    }
}