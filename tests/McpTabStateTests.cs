using System.Text.Json;
using Illustra.Models;
using NUnit.Framework;

namespace Illustra.Tests
{
    public class McpTabStateTests
    {
        [Test]
        public void TabPersistence_PreservesDedicatedIdentity()
        {
            var state = new TabState { IsMcpTab = true, FolderPath = @"C:\Pictures" };
            var restored = JsonSerializer.Deserialize<TabState>(JsonSerializer.Serialize(state));
            Assert.That(restored!.IsMcpTab, Is.True);
            Assert.That(restored.FolderPath, Is.EqualTo(state.FolderPath));
        }

        [Test]
        public void DuplicatedMcpTab_BecomesIndependentNormalTab()
        {
            var state = new TabState { IsMcpTab = true, FolderPath = @"C:\Pictures" };
            var clone = state.Clone();
            Assert.That(clone.IsMcpTab, Is.False);
            Assert.That(clone.FolderPath, Is.EqualTo(state.FolderPath));
            Assert.That(clone.FilterSettings, Is.Not.SameAs(state.FilterSettings));
        }
    }
}
