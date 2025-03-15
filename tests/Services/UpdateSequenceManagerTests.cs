using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Illustra.Models;
using Illustra.Services;
using NUnit.Framework;

namespace Illustra.Tests.Services
{
    [TestFixture]
    public class UpdateSequenceManagerTests
    {
        private ImageCollectionModel _imageCollection;
        private UpdateSequenceManager _manager;

        [SetUp]
        public void Setup()
        {
            _imageCollection = new ImageCollectionModel();
            _manager = new UpdateSequenceManager(_imageCollection);
        }

        [Test]
        public void Constructor_InitializesProperties()
        {
            // Assert
            Assert.NotNull(_manager);
            Assert.AreEqual(_imageCollection, _manager.ImageCollection);
        }

        [Test]
        public async Task ExecuteFilterAsync_CompletesOperation()
        {
            // Arrange
            bool operationCompleted = false;
            int rating = 3;

            // Act
            await _manager.ExecuteFilterAsync(rating, () => { operationCompleted = true; return Task.CompletedTask; });

            // Assert
            Assert.IsTrue(operationCompleted);
        }

        [Test]
        public async Task ExecuteSortAsync_CompletesOperation()
        {
            // Arrange
            bool operationCompleted = false;
            bool sortByDate = true;
            bool ascending = true;

            // Act
            await _manager.ExecuteSortAsync(sortByDate, ascending, () => { operationCompleted = true; return Task.CompletedTask; });

            // Assert
            Assert.IsTrue(operationCompleted);
        }

        [Test]
        public async Task ExecuteMultipleOperations_ExecutesInPriorityOrder()
        {
            // Arrange
            var operations = new List<string>();
            var completionSource = new TaskCompletionSource<bool>();

            // Act - Queue a sort operation that will wait for the completion source
            var sortTask = _manager.ExecuteSortAsync(true, true, () =>
            {
                operations.Add("Sort");
                return Task.CompletedTask;
            });

            // Queue a filter operation that should execute after the sort
            var filterTask = _manager.ExecuteFilterAsync(3, () =>
            {
                operations.Add("Filter");
                return Task.CompletedTask;
            });

            // Allow the operations to complete
            await Task.Delay(100); // Give time for operations to be queued
            completionSource.SetResult(true);

            // Wait for both operations to complete
            await Task.WhenAll(sortTask, filterTask);

            // Assert - Operations should be executed in the order they were queued
            Assert.AreEqual(2, operations.Count);
            Assert.AreEqual("Sort", operations[0]);
            Assert.AreEqual("Filter", operations[1]);
        }
    }
}
