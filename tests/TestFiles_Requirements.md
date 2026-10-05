# Test Files Requirements for NovelAI Metadata Detection

## Overview
This document outlines the test files needed for comprehensive testing of NovelAI metadata detection functionality.

## Required Test File Paths and Content

### 1. PNG Files with NovelAI Metadata
Location: `tests/TestFiles/NovelAI/`

#### 1.1 Complete NovelAI Metadata Sample
**File**: `tests/TestFiles/NovelAI/complete_metadata.png`
**Required tEXt chunks**:
```
Description: A beautiful anime character with long flowing hair standing in a magical forest
Comment: Generated with NovelAI for testing purposes
Software: NovelAI
parameters: {steps:28,sampler:k_euler_ancestral,seed:1234567890,strength:0.69,noise:0.667,scale:11,uc:lowres, bad anatomy}
Source: NovelAI Diffusion Anime V3
Title: Magical Forest Scene
```

#### 1.2 Individual Key Test Files
**File**: `tests/TestFiles/NovelAI/description_only.png`
- tEXt chunk: `Description: Test description content`

**File**: `tests/TestFiles/NovelAI/comment_only.png`
- tEXt chunk: `Comment: Test comment content`

**File**: `tests/TestFiles/NovelAI/software_only.png`
- tEXt chunk: `Software: NovelAI`

**File**: `tests/TestFiles/NovelAI/parameters_only.png`
- tEXt chunk: `parameters: steps:20,sampler:euler,seed:123456`

**File**: `tests/TestFiles/NovelAI/source_only.png`
- tEXt chunk: `Source: NovelAI Diffusion`

**File**: `tests/TestFiles/NovelAI/title_only.png`
- tEXt chunk: `Title: Test Image Title`

### 2. PNG Files without NovelAI Metadata
Location: `tests/TestFiles/Clean/`

**File**: `tests/TestFiles/Clean/no_metadata.png`
- Standard PNG file with no tEXt chunks

**File**: `tests/TestFiles/Clean/stable_diffusion_only.png`
- tEXt chunk: `parameters: Stable Diffusion metadata without NovelAI keys`

### 3. Mixed Metadata Files
Location: `tests/TestFiles/Mixed/`

**File**: `tests/TestFiles/Mixed/novelai_and_sd.png`
**Required tEXt chunks**:
```
Description: NovelAI description
parameters: steps:20,sampler:euler,seed:123456 (SD format)
```

### 4. Edge Case Files
Location: `tests/TestFiles/EdgeCases/`

**File**: `tests/TestFiles/EdgeCases/empty_values.png`
**Required tEXt chunks**:
```
Description: 
Comment: 
Software: 
```

**File**: `tests/TestFiles/EdgeCases/large_metadata.png`
- tEXt chunk: `Description: [Very long text over 1000 characters...]`

### 5. Non-PNG Files for Negative Testing
Location: `tests/TestFiles/NonPNG/`

**File**: `tests/TestFiles/NonPNG/fake.png` (actually a text file)
**File**: `tests/TestFiles/NonPNG/image.jpg`
**File**: `tests/TestFiles/NonPNG/document.txt`

## Test Directory Structure
```
tests/
├── TestFiles/
│   ├── NovelAI/
│   │   ├── complete_metadata.png
│   │   ├── description_only.png
│   │   ├── comment_only.png
│   │   ├── software_only.png
│   │   ├── parameters_only.png
│   │   ├── source_only.png
│   │   └── title_only.png
│   ├── Clean/
│   │   ├── no_metadata.png
│   │   └── stable_diffusion_only.png
│   ├── Mixed/
│   │   └── novelai_and_sd.png
│   ├── EdgeCases/
│   │   ├── empty_values.png
│   │   └── large_metadata.png
│   └── NonPNG/
│       ├── fake.png
│       ├── image.jpg
│       └── document.txt
```

## Creating Test Files with PNG tEXt Chunks

### Using Python with PIL (Pillow)
```python
from PIL import Image
from PIL.PngImagePlugin import PngInfo

# Create a simple test image
img = Image.new('RGB', (100, 100), color='red')

# Add NovelAI metadata
metadata = PngInfo()
metadata.add_text("Description", "A beautiful anime character with long flowing hair")
metadata.add_text("Comment", "Generated with NovelAI for testing")
metadata.add_text("Software", "NovelAI")
metadata.add_text("parameters", "steps:28,sampler:k_euler_ancestral,seed:1234567890")
metadata.add_text("Source", "NovelAI Diffusion Anime V3")
metadata.add_text("Title", "Magical Forest Scene")

# Save with metadata
img.save("complete_metadata.png", pnginfo=metadata)
```

### Using ImageMagick Command Line
```bash
# Create base image
convert -size 100x100 xc:red base.png

# Add metadata
mogrify -set "Description" "Test description" base.png
mogrify -set "Comment" "Test comment" base.png
mogrify -set "Software" "NovelAI" base.png
```

## Expected Debug Output During Tests
When the test files are processed, the following debug output should be generated:

```
NovelAI Metadata Detected - Key: Description, Value: A beautiful anime character with long flowing hair
NovelAI Metadata Detected - Key: Comment, Value: Generated with NovelAI for testing
NovelAI Metadata Detected - Key: Software, Value: NovelAI
NovelAI Metadata Detected - Key: parameters, Value: steps:28,sampler:k_euler_ancestral,seed:1234567890
NovelAI Metadata Detected - Key: Source, Value: NovelAI Diffusion Anime V3
NovelAI Metadata Detected - Key: Title, Value: Magical Forest Scene
```

## Notes for Implementation
1. All PNG files should be valid PNG format with proper headers
2. tEXt chunks should be properly formatted according to PNG specification
3. Files should be small (100x100 pixels) to keep repository size minimal
4. Test files should be added to `.gitignore` if they become too large
5. Consider using PNG files with minimal image data but proper metadata structure