using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Illustra.Tests.Views;

[TestFixture]
public class ViewerFilterShortcutTests
{
    private static string ProjectRoot()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Illustra.sln")))
            directory = directory.Parent;
        Assert.That(directory, Is.Not.Null);
        return directory!.FullName;
    }

    private static string ReadViewerSource() =>
        File.ReadAllText(Path.Combine(ProjectRoot(), "src", "Views", "ImageViewerWindow.xaml.cs"));

    private static string ReadThumbnailControlSource() =>
        File.ReadAllText(Path.Combine(ProjectRoot(), "src", "Views", "ThumbnailListControl.xaml.cs"));

    [Test]
    public void ImageViewerWindow_HandlesFilterRatingShortcuts()
    {
        var source = ReadViewerSource();
        Assert.Multiple(() =>
        {
            // フィルター解除ショートカット
            Assert.That(source, Does.Contain("shortcutHandler.IsShortcutMatch(FuncId.FilterRating0, e.Key)"));
            Assert.That(source, Does.Contain("ApplyRatingFilter(0)"));

            // フィルター切り替えショートカット
            Assert.That(source, Does.Contain("filter_rating_{i}"));
            Assert.That(source, Does.Contain("ApplyRatingFilter(i)"));

            // FilterChangedEvent の購読と解除
            Assert.That(source, Does.Contain("eventAggregator?.GetEvent<FilterChangedEvent>()?.Subscribe(OnFilterChanged"));
            Assert.That(source, Does.Contain("eventAggregator?.GetEvent<FilterChangedEvent>()?.Unsubscribe(OnFilterChanged"));

            // フィルタ変更時の画像更新メソッド
            Assert.That(source, Does.Contain("UpdateViewerImageAfterFilter"));
            Assert.That(source, Does.Contain("ApplyRatingFilterAsync"));
        });
    }

    [Test]
    public void ThumbnailListControl_ExposesApplyRatingFilterAsync_AndFallbackNavigation()
    {
        var source = ReadThumbnailControlSource();
        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("public async Task ApplyRatingFilterAsync(int rating)"));
            Assert.That(source, Does.Contain("await ApplyFilterling(rating);"));

            // GetPreviousImage と GetNextImage のフィルタ外フォールバック
            Assert.That(source, Does.Contain("var currentAllIndex = allItems.FindIndex(i => i.FullPath == currentFilePath);"));
        });
    }

    [Test]
    public void NavigationLogic_WhenCurrentItemFilteredOut_FindsCorrectFallback()
    {
        // 全アイテム
        var allItems = new List<string> { "item1.png", "item2.png", "item3.png", "item4.png", "item5.png" };
        // フィルタ後のアイテム (item2, item4 のみ)
        var filteredItems = new List<string> { "item2.png", "item4.png" };

        // 現在表示中の画像が item3 (フィルタ外) の場合
        var current = "item3.png";
        var currentAllIndex = allItems.IndexOf(current);

        // 次へ進む場合: item3 より後ろで最初のフィルタアイテム -> item4
        string? next = null;
        for (int i = currentAllIndex + 1; i < allItems.Count; i++)
        {
            if (filteredItems.Contains(allItems[i]))
            {
                next = allItems[i];
                break;
            }
        }
        Assert.That(next, Is.EqualTo("item4.png"));

        // 前に戻る場合: item3 より前で最初のフィルタアイテム -> item2
        string? prev = null;
        for (int i = currentAllIndex - 1; i >= 0; i--)
        {
            if (filteredItems.Contains(allItems[i]))
            {
                prev = allItems[i];
                break;
            }
        }
        Assert.That(prev, Is.EqualTo("item2.png"));
    }
}
