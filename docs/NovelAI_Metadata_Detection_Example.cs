// Example usage demonstration of NovelAI metadata detection
// This file demonstrates how the new functionality works

/*
When a PNG file with NovelAI metadata is processed, the following will happen:

1. User calls: PngMetadataReader.ExtractStableDiffusionMetadata("image.png")

2. The method automatically calls: DetectAndLogNovelAIMetadata("image.png")

3. For each NovelAI key found in the PNG's tEXt chunks, a debug log entry is created:
   Debug.WriteLine("NovelAI Metadata Detected - Key: Description, Value: A beautiful anime character...")
   Debug.WriteLine("NovelAI Metadata Detected - Key: Software, Value: NovelAI")
   Debug.WriteLine("NovelAI Metadata Detected - Key: Comment, Value: Generated with NovelAI Diffusion")

4. The existing Stable Diffusion metadata extraction continues normally

NovelAI metadata keys that are detected:
- Description: Image description/prompt
- Comment: Comment information  
- Software: Software used for generation
- parameters: Parameter information (may overlap with Stable Diffusion)
- Source: Generation source information
- Title: Image title

The detection is completely non-intrusive and runs automatically whenever PNG metadata is extracted.
All logging uses System.Diagnostics.Debug.WriteLine() so it only appears in debug builds/output.
*/