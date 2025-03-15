using System;
using System.Collections.Generic;
using System.Linq;
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
        private ImageModel _imageModel;

        [SetUp]
        public void Setup()
        {
            _imageModel = new ImageModel();
            // テスト用のデータを追加
            _imageModel.Items.Add(new FileNodeModel("test1.jpg") { Rating = 3 });
            _imageModel.Items.Add(new FileNodeModel("test2.jpg") { Rating = 5 });
        }

        [Test]
        public void Constructor_InitializesProperties()
        {
            // Arrange & Act
            var manager = new UpdateSequenceManager(_imageModel);

            // Assert
            Assert.AreEqual(OperationState.NotStarted, manager.CurrentState);
        }

        [Test]
        public async Task InterruptWithFilterOperation_CompletesOperation()
        {
            // Arrange
            var manager = new UpdateSequenceManager(_imageModel);
            var operationCompleted = false;

            manager.StateChanged += (sender, args) =>
            {
                if (args.Type == OperationType.Filter && args.State == OperationState.Completed)
                {
                    operationCompleted = true;
                }
            };

            // Act
            manager.InterruptWithFilterOperation(4);

            // Wait for operation to complete
            await Task.Delay(500); // Give some time for the operation to complete

            // Assert
            Assert.IsTrue(operationCompleted, "Filter operation should have completed");
        }

        [Test]
        public async Task InterruptWithSortOperation_CompletesOperation()
        {
            // Arrange
            var manager = new UpdateSequenceManager(_imageModel);
            var operationCompleted = false;

            manager.StateChanged += (sender, args) =>
            {
                if (args.Type == OperationType.Sort && args.State == OperationState.Completed)
                {
                    operationCompleted = true;
                }
            };

            // Act
            manager.InterruptWithSortOperation(true, false);

            // Wait for operation to complete
            await Task.Delay(500); // Give some time for the operation to complete

            // Assert
            Assert.IsTrue(operationCompleted, "Sort operation should have completed");
        }

        [Test]
        public async Task WaitForOperationTypeCompletionAsync_ReturnsWhenOperationCompletes()
        {
            // Arrange
            var manager = new UpdateSequenceManager(_imageModel);
            var operationTask = manager.WaitForOperationTypeCompletionAsync(OperationType.Filter);

            // Act
            manager.InterruptWithFilterOperation(3);

            // Assert
            var completedTask = await Task.WhenAny(operationTask, Task.Delay(1000));
            Assert.AreEqual(operationTask, completedTask);
        }

        [Test]
        public async Task EnqueueThumbnailLoad_ProcessesImages()
        {
            // Arrange
            var manager = new UpdateSequenceManager(_imageModel);
            var operationStarted = false;
            var operationCompleted = false;

            manager.StateChanged += (sender, args) =>
            {
                if (args.Type == OperationType.ThumbnailLoad)
                {
                    if (args.State == OperationState.Running)
                    {
                        operationStarted = true;
                    }
                    else if (args.State == OperationState.Completed)
                    {
                        operationCompleted = true;
                    }
                }
            };

            // Act
            manager.EnqueueThumbnailLoad(_imageModel.Items, true);

            // Wait for operation to complete
            await Task.Delay(500); // Give some time for the operation to complete

            // Assert
            Assert.IsTrue(operationStarted, "Thumbnail load operation should have started");
            Assert.IsTrue(operationCompleted, "Thumbnail load operation should have completed");
        }

        [Test]
        public async Task MultipleOperations_ExecuteInPriorityOrder()
        {
            // Arrange
            var manager = new UpdateSequenceManager(_imageModel);
            var executionOrder = new List<OperationType>();

            manager.StateChanged += (sender, args) =>
            {
                if (args.State == OperationState.Running)
                {
                    executionOrder.Add(args.Type);
                }
            };

            // Act
            // Add low priority operation
            manager.EnqueueThumbnailLoad(_imageModel.Items, false);

            // Add high priority operation
            manager.InterruptWithFilterOperation(4);

            // Wait for operations to complete
            await Task.Delay(1000); // Give some time for the operations to complete

            // Assert
            Assert.AreEqual(OperationType.Filter, executionOrder[0]);
        }
    }
}