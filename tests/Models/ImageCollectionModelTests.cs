using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Illustra.Models;
using NUnit.Framework;

namespace Illustra.Tests.Models
{
    [TestFixture]
    public class ImageCollectionModelTests
    {
        [Test]
        public void Constructor_InitializesProperties()
        {
            // Arrange & Act
            var model = new ImageCollectionModel();

            // Assert
            Assert.NotNull(model.Items);
            Assert.IsEmpty(model.Items);
            Assert.IsNull(model.CurrentFolderPath);
        }

        [Test]
        public async Task LoadImagesFromFolderAsync_WithInvalidPath_ReturnsZero()
        {
            // Arrange
            var model = new ImageCollectionModel();

            // Act
            var result = await model.LoadImagesFromFolderAsync("invalid_path");

            // Assert
            Assert.AreEqual(0, result);
            Assert.IsEmpty(model.Items);
        }

        [Test]
        public async Task FilterByRatingAsync_WithZeroRating_ReturnsAllItems()
        {
            // Arrange
            var model = new ImageCollectionModel();
            var fileNode1 = new FileNodeModel("test1.jpg") { Rating = 3 };
            var fileNode2 = new FileNodeModel("test2.jpg") { Rating = 5 };
            model.Items.Add(fileNode1);
            model.Items.Add(fileNode2);

            // Act
            var result = await model.FilterByRatingAsync(0);

            // Assert
            Assert.AreEqual(2, result.Count);
            Assert.Contains(fileNode1, result);
            Assert.Contains(fileNode2, result);
        }

        [Test]
        public async Task FilterByRatingAsync_WithRating_ReturnsFilteredItems()
        {
            // Arrange
            var model = new ImageCollectionModel();
            var fileNode1 = new FileNodeModel("test1.jpg") { Rating = 3 };
            var fileNode2 = new FileNodeModel("test2.jpg") { Rating = 5 };
            model.Items.Add(fileNode1);
            model.Items.Add(fileNode2);

            // Act
            var result = await model.FilterByRatingAsync(4);

            // Assert
            Assert.AreEqual(1, result.Count);
            Assert.Contains(fileNode2, result);
            Assert.IsFalse(result.Contains(fileNode1));
        }

        [Test]
        public async Task SortItemsAsync_ByFileName_SortsCorrectly()
        {
            // Arrange
            var model = new ImageCollectionModel();
            var fileNode1 = new FileNodeModel("b.jpg");
            var fileNode2 = new FileNodeModel("a.jpg");
            model.Items.Add(fileNode1);
            model.Items.Add(fileNode2);

            // Act - Sort by name ascending
            var result = await model.SortItemsAsync(false, true);

            // Assert
            Assert.AreEqual(2, result.Count);
            Assert.AreEqual(fileNode2, result[0]); // a.jpg should be first
            Assert.AreEqual(fileNode1, result[1]); // b.jpg should be second

            // Act - Sort by name descending
            result = await model.SortItemsAsync(false, false);

            // Assert
            Assert.AreEqual(2, result.Count);
            Assert.AreEqual(fileNode1, result[0]); // b.jpg should be first
            Assert.AreEqual(fileNode2, result[1]); // a.jpg should be second
        }

        [Test]
        public async Task SortItemsAsync_ByDate_SortsCorrectly()
        {
            // Arrange
            var model = new ImageCollectionModel();
            var fileNode1 = new FileNodeModel("test1.jpg")
            {
                LastModified = DateTime.Now.AddDays(-1)
            };
            var fileNode2 = new FileNodeModel("test2.jpg")
            {
                LastModified = DateTime.Now
            };
            model.Items.Add(fileNode1);
            model.Items.Add(fileNode2);

            // Act - Sort by date ascending
            var result = await model.SortItemsAsync(true, true);

            // Assert
            Assert.AreEqual(2, result.Count);
            Assert.AreEqual(fileNode1, result[0]); // Older file should be first
            Assert.AreEqual(fileNode2, result[1]); // Newer file should be second

            // Act - Sort by date descending
            result = await model.SortItemsAsync(true, false);

            // Assert
            Assert.AreEqual(2, result.Count);
            Assert.AreEqual(fileNode2, result[0]); // Newer file should be first
            Assert.AreEqual(fileNode1, result[1]); // Older file should be second
        }

        [Test]
        public async Task FilterByRatingAsync_WithCancellation_CancelsOperation()
        {
            // Arrange
            var model = new ImageCollectionModel();
            for (int i = 0; i < 100; i++)
            {
                model.Items.Add(new FileNodeModel($"test{i}.jpg") { Rating = i % 5 });
            }

            var cts = new CancellationTokenSource();
            cts.Cancel(); // Cancel immediately

            // Act & Assert
            var result = await model.FilterByRatingAsync(3, cts.Token);
            Assert.IsEmpty(result); // Operation should be cancelled before processing any items
        }
    }
}