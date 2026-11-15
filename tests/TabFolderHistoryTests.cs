using System.Text.Json;
using Illustra.Models;
using NUnit.Framework;

namespace Illustra.Tests
{
    public class TabFolderHistoryTests
    {
        [Test]
        public void Navigation_IsIndependentPerTab_AndNewVisitDiscardsForwardBranch()
        {
            var mcp = new TabState { IsMcpTab = true };
            var other = new TabState { FolderPath = @"C:\other" };
            mcp.RecordFolder(@"C:\first");
            mcp.FolderPath = @"C:\first";
            mcp.RecordFolder(@"C:\second");
            mcp.FolderPath = @"C:\second";
            mcp.RecordFolder(@"C:\third");
            mcp.FolderPath = @"C:\third";

            Assert.That(mcp.MoveFolderHistory(-1), Is.EqualTo(@"C:\second"));
            mcp.FolderPath = @"C:\second";
            Assert.That(mcp.MoveFolderHistory(-1), Is.EqualTo(@"C:\first"));
            mcp.FolderPath = @"C:\first";
            Assert.That(mcp.MoveFolderHistory(-1), Is.Null);
            Assert.That(mcp.MoveFolderHistory(1), Is.EqualTo(@"C:\second"));
            mcp.FolderPath = @"C:\second";
            mcp.RecordFolder(@"C:\new");
            mcp.FolderPath = @"C:\new";

            Assert.That(mcp.CanGoForward, Is.False);
            Assert.That(mcp.FolderHistory, Is.EqualTo(new[] { @"C:\first", @"C:\second", @"C:\new" }));
            Assert.That(other.CanGoBack, Is.False);
            Assert.That(other.FolderPath, Is.EqualTo(@"C:\other"));
        }

        [Test]
        public void SameFolderDoesNotAddEntry_AndHistorySurvivesSettingsSerialization()
        {
            var state = new TabState { FolderPath = @"C:\first" };
            state.RecordFolder(@"c:\FIRST");
            state.RecordFolder(@"C:\second");
            state.FolderPath = @"C:\second";
            state.RecordFolder(@"c:\SECOND");
            var restored = JsonSerializer.Deserialize<TabState>(JsonSerializer.Serialize(state));

            Assert.That(restored!.FolderHistory, Is.EqualTo(new[] { @"C:\first", @"C:\second" }));
            Assert.That(restored.MoveFolderHistory(-1), Is.EqualTo(@"C:\first"));
            Assert.That(restored.CanGoForward, Is.True);
        }
        [Test]
        public void HistoryLimit_DropsOldestAndKeepsNavigationIndex()
        {
            var state = new TabState();
            for (int i = 0; i <= TabState.MaxFolderHistory; i++)
            {
                state.RecordFolder($@"C:\folder{i}");
                state.FolderPath = $@"C:\folder{i}";
            }

            Assert.That(state.FolderHistory.Count, Is.EqualTo(TabState.MaxFolderHistory));
            Assert.That(state.FolderHistory[0], Is.EqualTo(@"C:\folder1"));
            Assert.That(state.FolderHistoryIndex, Is.EqualTo(TabState.MaxFolderHistory - 1));
            for (int i = 0; i < TabState.MaxFolderHistory - 1; i++)
            {
                var path = state.MoveFolderHistory(-1);
                state.FolderPath = path;
            }
            Assert.That(state.FolderPath, Is.EqualTo(@"C:\folder1"));
            Assert.That(state.MoveFolderHistory(-1), Is.Null);
            Assert.That(state.MoveFolderHistory(1), Is.EqualTo(@"C:\folder2"));
        }

        [Test]
        public void RestoredOversizedHistory_TrimsWithoutChangingCurrentFolder()
        {
            var state = new TabState
            {
                FolderHistory = new System.Collections.Generic.List<string>(),
                FolderHistoryIndex = 101,
                FolderPath = @"C:\folder101"
            };
            for (int i = 0; i < 105; i++)
                state.FolderHistory.Add($@"C:\folder{i}");

            var restored = JsonSerializer.Deserialize<TabState>(JsonSerializer.Serialize(state))!;
            restored.TrimFolderHistory();

            Assert.That(restored.FolderHistory.Count, Is.EqualTo(TabState.MaxFolderHistory));
            Assert.That(restored.FolderHistory[0], Is.EqualTo(@"C:\folder5"));
            Assert.That(restored.FolderHistoryIndex, Is.EqualTo(96));
            Assert.That(restored.FolderHistory[restored.FolderHistoryIndex], Is.EqualTo(restored.FolderPath));
            Assert.That(restored.MoveFolderHistory(1), Is.EqualTo(@"C:\folder102"));
        }

        [Test]
        public void TrimmingRestoredHistory_PreservesCurrentFolderNearOldestEnd()
        {
            var state = new TabState { FolderHistoryIndex = 2, FolderPath = @"C:\folder2" };
            for (int i = 0; i < 105; i++)
                state.FolderHistory.Add($@"C:\folder{i}");

            state.TrimFolderHistory();

            Assert.That(state.FolderHistory.Count, Is.EqualTo(TabState.MaxFolderHistory));
            Assert.That(state.FolderHistoryIndex, Is.Zero);
            Assert.That(state.FolderHistory[0], Is.EqualTo(state.FolderPath));
            Assert.That(state.FolderHistory[^1], Is.EqualTo(@"C:\folder101"));
            Assert.That(state.MoveFolderHistory(1), Is.EqualTo(@"C:\folder3"));
        }

    }
}
