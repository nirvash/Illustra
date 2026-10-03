using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using NUnit.Framework;
using Illustra.Models;

namespace Illustra.Tests.Views
{
    [TestFixture]
    public class GenerationMetadataTagPresentationTests
    {
        [Test]
        public void GenerationPrompt_UsesFilterableTagPresentation()
        {
            // WPF を起動せず、実際のビューのバインディングと操作契約を検証する。
            var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Illustra.sln")))
                directory = directory.Parent;

            Assert.That(directory, Is.Not.Null, "リポジトリのルートが見つかりません。");
            var document = XDocument.Load(Path.Combine(directory!.FullName, "src", "Views", "PropertyPanelControl.xaml"));
            XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
            XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
            var section = document.Descendants(presentation + "Expander")
                .Single(element => (string?)element.Attribute(xaml + "Name") == "GenerationMetadataSection");
            var tags = section.Descendants(presentation + "ItemsControl")
                .SingleOrDefault(element => (string?)element.Attribute("ItemsSource") == "{Binding GenerationMetadata.Tags}");

            Assert.That(tags, Is.Not.Null,
                "ComfyUI の生成プロンプトにもタグ表示が必要です（現在は本文 TextBox のみ）。");
            Assert.That(tags!.Descendants(presentation + "WrapPanel"), Is.Not.Empty);
            var textBox = tags.Descendants(presentation + "TextBox").Single();
            Assert.Multiple(() =>
            {
                Assert.That((string?)textBox.Attribute("Style"), Is.EqualTo("{StaticResource NormalTagTextBoxStyle}"));
                Assert.That((string?)textBox.Attribute("ContextMenu"), Is.EqualTo("{StaticResource FilterableTagContextMenu}"));
                Assert.That((string?)textBox.Attribute("PreviewMouseRightButtonDown"), Is.EqualTo("Tag_PreviewMouseRightButtonDown"));
                Assert.That((string?)textBox.Attribute("Loaded"), Is.EqualTo("Tag_Loaded"));
            });

            var negativeTags = section.Descendants(presentation + "ItemsControl")
                .SingleOrDefault(element => (string?)element.Attribute("ItemsSource") == "{Binding GenerationMetadata.NegativeTags}");
            Assert.That(negativeTags, Is.Not.Null, "ネガティブプロンプトにもタグ表示が必要です。");
            var negativeTextBox = negativeTags!.Descendants(presentation + "TextBox").Single();
            Assert.Multiple(() =>
            {
                Assert.That((string?)negativeTextBox.Attribute("Style"), Is.EqualTo("{StaticResource NegativeTagTextBoxStyle}"));
                Assert.That((string?)negativeTextBox.Attribute("ContextMenu"), Is.EqualTo("{StaticResource FilterableTagContextMenu}"));
                Assert.That((string?)negativeTextBox.Attribute("Loaded"), Is.EqualTo("Tag_Loaded"));
            });

            var codeBehind = File.ReadAllText(Path.Combine(directory!.FullName, "src", "Views", "PropertyPanelControl.xaml.cs"));
            Assert.That(codeBehind, Does.Contain("FindVisualChildren<TextBox>(GenerationTagsItemsControl"));
            Assert.That(codeBehind, Does.Contain("FindVisualChildren<TextBox>(GenerationNegativeTagsItemsControl"));
        }

        [Test]
        public void GenerationMetadata_ExtractsPositiveAndNegativeTagsWithoutChangingPromptText()
        {
            var metadata = new GenerationMetadata
            {
                Prompt = "soft light, blue sky, a calm natural sentence",
                NegativePrompt = "blurry, low quality"
            };

            Assert.Multiple(() =>
            {
                Assert.That(metadata.Tags, Is.EqualTo(new[] { "soft light", "blue sky", "a calm natural sentence" }));
                Assert.That(metadata.NegativeTags, Is.EqualTo(new[] { "blurry", "low quality" }));
                Assert.That(metadata.Prompt, Is.EqualTo("soft light, blue sky, a calm natural sentence"));
                Assert.That(metadata.NegativePrompt, Is.EqualTo("blurry, low quality"));
            });
        }
    }
}
