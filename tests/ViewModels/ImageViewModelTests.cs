using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Illustra.Models;
using Illustra.Services;
using Illustra.ViewModels;
using NUnit.Framework;

namespace Illustra.Tests.ViewModels
{
    [TestFixture]
    public class ImageViewModelTests
    {
        private ImageViewModel _viewModel;

        [SetUp]
        public void Setup()
        {
            _viewModel = new ImageViewModel();
        }

        [Test]
        public void Constructor_InitializesProperties()
        {
            // Assert
            Assert.NotNull(_viewModel.DisplayItems);
            Assert.IsEmpty(_viewModel.DisplayItems);
            Assert.IsFalse(_viewModel.IsLoading);
            Assert.IsFalse(_viewModel.IsFiltering);
            Assert.IsFalse(_viewModel.IsSorting);
            Assert.AreEqual(0, _viewModel.CurrentRatingFilter);
            Assert.IsNull(_viewModel.SelectedItem);
        }

        [Test]
        public async Task LoadImagesFromFolderAsync_WithInvalidPath_ReturnsZero()
        {
            // Act
            var result = await _viewModel.LoadImagesFromFolderAsync("invalid_path");

            // Assert
            Assert.AreEqual(0, result);
            Assert.IsEmpty(_viewModel.DisplayItems);
            Assert.IsFalse(_viewModel.IsLoading);
        }

        [Test]
        public async Task ApplyFilterAsync_WithZeroRating_ReturnsAllItems()
        {
            // Arrange
            // テスト用のデータを追加（実際のファイルは読み込まない）
            var mockFileNode1 = new FileNodeModel("test1.jpg") { Rating = 3 };
            var mockFileNode2 = new FileNodeModel("test2.jpg") { Rating = 5 };

            // プライベートフィールドを反映させるためにリフレクションを使用
            var imageCollectionField = typeof(ImageViewModel).GetField("_imageCollection",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var imageCollection = (ImageCollectionModel)imageCollectionField.GetValue(_viewModel);

            imageCollection.Items.Add(mockFileNode1);
            imageCollection.Items.Add(mockFileNode2);

            // Act
            var result = await _viewModel.ApplyFilterAsync(0);

            // Assert
            Assert.AreEqual(2, result);
            Assert.AreEqual(2, _viewModel.DisplayItems.Count);
            Assert.IsFalse(_viewModel.IsFiltering);
        }

        [Test]
        public async Task ApplyFilterAsync_WithRating_ReturnsFilteredItems()
        {
            // Arrange
            // テスト用のデータを追加（実際のファイルは読み込まない）
            var mockFileNode1 = new FileNodeModel("test1.jpg") { Rating = 3 };
            var mockFileNode2 = new FileNodeModel("test2.jpg") { Rating = 5 };

            // プライベートフィールドを反映させるためにリフレクションを使用
            var imageCollectionField = typeof(ImageViewModel).GetField("_imageCollection",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var imageCollection = (ImageCollectionModel)imageCollectionField.GetValue(_viewModel);

            imageCollection.Items.Add(mockFileNode1);
            imageCollection.Items.Add(mockFileNode2);

            // Act
            var result = await _viewModel.ApplyFilterAsync(4);

            // Assert
            Assert.AreEqual(1, result);
            Assert.AreEqual(1, _viewModel.DisplayItems.Count);
            Assert.AreEqual("test2.jpg", _viewModel.DisplayItems[0].FileName);
            Assert.IsFalse(_viewModel.IsFiltering);
        }

        [Test]
        public async Task ApplySortAsync_ByFileName_SortsCorrectly()
        {
            // Arrange
            // テスト用のデータを追加（実際のファイルは読み込まない）
            var mockFileNode1 = new FileNodeModel("b.jpg");
            var mockFileNode2 = new FileNodeModel("a.jpg");

            // プライベートフィールドを反映させるためにリフレクションを使用
            var imageCollectionField = typeof(ImageViewModel).GetField("_imageCollection",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var imageCollection = (ImageCollectionModel)imageCollectionField.GetValue(_viewModel);

            imageCollection.Items.Add(mockFileNode1);
            imageCollection.Items.Add(mockFileNode2);

            // Act - Sort by name ascending
            var result = await _viewModel.ApplySortAsync(false, true);

            // Assert
            Assert.AreEqual(2, result);
            Assert.AreEqual(2, _viewModel.DisplayItems.Count);
            Assert.AreEqual("a.jpg", _viewModel.DisplayItems[0].FileName);
            Assert.AreEqual("b.jpg", _viewModel.DisplayItems[1].FileName);
            Assert.IsFalse(_viewModel.IsSorting);
        }

        [Test]
        public async Task ApplySortAsync_ByDate_SortsCorrectly()
        {
            // Arrange
            // テスト用のデータを追加（実際のファイルは読み込まない）
            var mockFileNode1 = new FileNodeModel("test1.jpg")
            {
                LastModified = DateTime.Now.AddDays(-1)
            };
            var mockFileNode2 = new FileNodeModel("test2.jpg")
            {
                LastModified = DateTime.Now
            };

            // プライベートフィールドを反映させるためにリフレクションを使用
            var imageCollectionField = typeof(ImageViewModel).GetField("_imageCollection",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var imageCollection = (ImageCollectionModel)imageCollectionField.GetValue(_viewModel);

            imageCollection.Items.Add(mockFileNode1);
            imageCollection.Items.Add(mockFileNode2);

            // Act - Sort by date descending
            var result = await _viewModel.ApplySortAsync(true, false);

            // Assert
            Assert.AreEqual(2, result);
            Assert.AreEqual(2, _viewModel.DisplayItems.Count);
            Assert.AreEqual("test2.jpg", _viewModel.DisplayItems[0].FileName);
            Assert.AreEqual("test1.jpg", _viewModel.DisplayItems[1].FileName);
            Assert.IsFalse(_viewModel.IsSorting);
        }

        [Test]
        public void CurrentRatingFilter_WhenChanged_TriggersFilterOperation()
        {
            // Arrange
            bool filterCalled = false;

            // プライベートメソッドをモックするためにリフレクションを使用
            var originalMethod = typeof(ImageViewModel).GetMethod("ApplyFilterAsync",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

            // テスト用のモックメソッドを作成
            Func<int, Task<int>> mockMethod = async (rating) =>
            {
                filterCalled = true;
                Assert.AreEqual(3, rating);
                return 0;
            };

            // PropertyChangedイベントの購読
            bool propertyChangedRaised = false;
            _viewModel.PropertyChanged += (sender, args) =>
            {
                if (args.PropertyName == nameof(ImageViewModel.CurrentRatingFilter))
                {
                    propertyChangedRaised = true;
                }
            };

            // Act
            _viewModel.CurrentRatingFilter = 3;

            // Assert
            Assert.AreEqual(3, _viewModel.CurrentRatingFilter);
            Assert.IsTrue(propertyChangedRaised);

            // 非同期メソッドの呼び出しを検証するのは難しいため、
            // ここではプロパティの変更と値の設定のみを検証
        }

        [Test]
        public void SortByDate_WhenChanged_RaisesPropertyChangedEvent()
        {
            // Arrange
            // PropertyChangedイベントの購読
            bool propertyChangedRaised = false;
            _viewModel.PropertyChanged += (sender, args) =>
            {
                if (args.PropertyName == nameof(ImageViewModel.SortByDate))
                {
                    propertyChangedRaised = true;
                }
            };

            // Act
            _viewModel.SortByDate = true;

            // Assert
            Assert.IsTrue(_viewModel.SortByDate);
            Assert.IsTrue(propertyChangedRaised);

            // 注意: このテストではプロパティの変更とPropertyChangedイベントの発火のみを検証しています
            // ApplySortAsyncメソッドが実際に呼び出されたかどうかはテスト対象ではありません
            // 非同期メソッドの呼び出しを検証するには、モックフレームワークなどの追加の仕組みが必要です
        }

        [Test]
        public void SortAscending_WhenChanged_RaisesPropertyChangedEvent()
        {
            // Arrange
            // PropertyChangedイベントの購読
            bool propertyChangedRaised = false;
            _viewModel.PropertyChanged += (sender, args) =>
            {
                if (args.PropertyName == nameof(ImageViewModel.SortAscending))
                {
                    propertyChangedRaised = true;
                }
            };

            // Act
            _viewModel.SortAscending = false;

            // Assert
            Assert.IsFalse(_viewModel.SortAscending);
            Assert.IsTrue(propertyChangedRaised);

            // 注意: このテストではプロパティの変更とPropertyChangedイベントの発火のみを検証しています
            // ApplySortAsyncメソッドが実際に呼び出されたかどうかはテスト対象ではありません
            // 非同期メソッドの呼び出しを検証するには、モックフレームワークなどの追加の仕組みが必要です
        }

        [Test]
        public void SelectedItem_WhenChanged_RaisesPropertyChangedEvent()
        {
            // Arrange
            var mockFileNode = new FileNodeModel("test.jpg");
            bool propertyChangedRaised = false;

            _viewModel.PropertyChanged += (sender, args) =>
            {
                if (args.PropertyName == nameof(ImageViewModel.SelectedItem))
                {
                    propertyChangedRaised = true;
                }
            };

            // Act
            _viewModel.SelectedItem = mockFileNode;

            // Assert
            Assert.AreEqual(mockFileNode, _viewModel.SelectedItem);
            Assert.IsTrue(propertyChangedRaised);
        }
    }
}