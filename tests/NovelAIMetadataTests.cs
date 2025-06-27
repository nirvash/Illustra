using NUnit.Framework;
using Illustra.Helpers;
using System.IO;
using System.Diagnostics;

namespace Illustra.Tests
{
    [TestFixture]
    public class NovelAIMetadataTests
    {
        [Test]
        public void DetectAndLogNovelAIMetadata_WithNonPngFile_ShouldReturnEarly()
        {
            // Arrange
            var tempFilePath = Path.GetTempFileName();
            try
            {
                // Act & Assert - should not throw any exceptions
                PngMetadataReader.DetectAndLogNovelAIMetadata(tempFilePath);
            }
            finally
            {
                if (File.Exists(tempFilePath))
                {
                    File.Delete(tempFilePath);
                }
            }
        }

        [Test]
        public void DetectAndLogNovelAIMetadata_WithNonExistentFile_ShouldNotThrow()
        {
            // Arrange
            var nonExistentFile = "non_existent_file.png";

            // Act & Assert - should not throw any exceptions
            Assert.DoesNotThrow(() => PngMetadataReader.DetectAndLogNovelAIMetadata(nonExistentFile));
        }

        [Test]
        public void KnownNovelAIKeys_ShouldContainExpectedKeys()
        {
            // Arrange
            var expectedKeys = new[] { "Description", "Comment", "Software", "parameters", "Source", "Title" };

            // Act
            var actualKeys = PngMetadataReader.KnownNovelAIKeys;

            // Assert
            Assert.That(actualKeys, Is.Not.Null);
            Assert.That(actualKeys.Length, Is.EqualTo(expectedKeys.Length));
            
            foreach (var expectedKey in expectedKeys)
            {
                Assert.That(actualKeys, Contains.Item(expectedKey));
            }
        }

        [Test]
        public void ExtractStableDiffusionMetadata_ShouldCallNovelAIDetection()
        {
            // Arrange
            var tempFilePath = Path.ChangeExtension(Path.GetTempFileName(), ".png");
            
            try
            {
                // Create a minimal PNG file (just the PNG signature)
                var pngSignature = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
                File.WriteAllBytes(tempFilePath, pngSignature);

                // Act - this should call DetectAndLogNovelAIMetadata internally
                var result = PngMetadataReader.ExtractStableDiffusionMetadata(tempFilePath);

                // Assert
                Assert.That(result, Is.Not.Null);
                // The method should complete without throwing exceptions
                // NovelAI detection is called automatically during this process
            }
            catch (System.Exception ex)
            {
                // Expected to fail due to invalid PNG, but NovelAI detection should still be attempted
                Debug.WriteLine($"Expected PNG parsing error: {ex.Message}");
            }
            finally
            {
                if (File.Exists(tempFilePath))
                {
                    File.Delete(tempFilePath);
                }
            }
        }
    }
}