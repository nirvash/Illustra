using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Illustra.Tests.Helpers;
using Illustra.ViewModels;
using NUnit.Framework;

namespace Illustra.Tests.Helpers
{
    [TestFixture]
    public class ThumbnailMetadataCacheTests
    {
        private string _path;

        [SetUp]
        public void SetUp() => _path = Path.Combine(Path.GetTempPath(), $"illustra_filter_{Guid.NewGuid():N}.png");

        [TearDown]
        public void TearDown()
        {
            if (File.Exists(_path)) File.Delete(_path);
        }

        [Test]
        public async Task UpdateCaches_WithNovelAiMetadata_UsesPositiveTagsAndRecognizesPromptAsync()
        {
            File.WriteAllBytes(_path, TestPngBuilder.BuildPngWithTextChunks(
                ("Description", "red fox, green eyes"),
                ("Software", "NovelAI"),
                ("Comment", "{\"uc\":\"bad hands\",\"steps\":28}")));
            var viewModel = CreateUninitializedViewModel();

            await viewModel.UpdatePromptCacheAsync(_path);
            await viewModel.UpdateTagCacheAsync(_path);

            var prompts = GetField<Dictionary<string, bool>>(viewModel, "_promptCache");
            var tags = GetField<Dictionary<string, List<string>>>(viewModel, "_tagCache");
            Assert.That(prompts[_path], Is.True, "a positive NovelAI prompt must pass the prompt filter");
            Assert.That(tags[_path], Does.Contain("red fox"));
            Assert.That(tags[_path], Does.Contain("green eyes"));
            Assert.That(tags[_path], Does.Not.Contain("bad hands"), "negative tags are excluded from tag filtering");
        }

        private static ThumbnailListViewModel CreateUninitializedViewModel()
        {
            var instance = (ThumbnailListViewModel)RuntimeHelpers.GetUninitializedObject(typeof(ThumbnailListViewModel));
            SetField(instance, "_promptCache", new Dictionary<string, bool>());
            SetField(instance, "_tagCache", new Dictionary<string, List<string>>());
            return instance;
        }

        private static void SetField<T>(object instance, string name, T value) =>
            typeof(ThumbnailListViewModel).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(instance, value);

        private static T GetField<T>(object instance, string name) =>
            (T)typeof(ThumbnailListViewModel).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance)!;
    }
}
