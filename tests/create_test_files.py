#!/usr/bin/env python3
"""
Script to create test PNG files with NovelAI metadata for testing purposes.
This script creates the test files described in TestFiles_Requirements.md

Requirements:
    pip install Pillow

Usage:
    python create_test_files.py
"""

from PIL import Image
from PIL.PngImagePlugin import PngInfo
import os

def create_test_directory_structure():
    """Create the test directory structure."""
    base_dir = "TestFiles"
    directories = [
        os.path.join(base_dir, "NovelAI"),
        os.path.join(base_dir, "Clean"),
        os.path.join(base_dir, "Mixed"),
        os.path.join(base_dir, "EdgeCases"),
        os.path.join(base_dir, "NonPNG")
    ]
    
    for directory in directories:
        os.makedirs(directory, exist_ok=True)
    
    return base_dir

def create_base_image():
    """Create a simple base image."""
    return Image.new('RGB', (100, 100), color='red')

def create_complete_metadata_png(base_dir):
    """Create PNG with complete NovelAI metadata."""
    img = create_base_image()
    metadata = PngInfo()
    metadata.add_text("Description", "A beautiful anime character with long flowing hair standing in a magical forest")
    metadata.add_text("Comment", "Generated with NovelAI for testing purposes")
    metadata.add_text("Software", "NovelAI")
    metadata.add_text("parameters", "{steps:28,sampler:k_euler_ancestral,seed:1234567890,strength:0.69,noise:0.667,scale:11,uc:lowres, bad anatomy}")
    metadata.add_text("Source", "NovelAI Diffusion Anime V3")
    metadata.add_text("Title", "Magical Forest Scene")
    
    filepath = os.path.join(base_dir, "NovelAI", "complete_metadata.png")
    img.save(filepath, pnginfo=metadata)
    print(f"Created: {filepath}")

def create_individual_key_files(base_dir):
    """Create PNG files with individual NovelAI metadata keys."""
    test_data = [
        ("description_only.png", "Description", "Test description content"),
        ("comment_only.png", "Comment", "Test comment content"),
        ("software_only.png", "Software", "NovelAI"),
        ("parameters_only.png", "parameters", "steps:20,sampler:euler,seed:123456"),
        ("source_only.png", "Source", "NovelAI Diffusion"),
        ("title_only.png", "Title", "Test Image Title")
    ]
    
    for filename, key, value in test_data:
        img = create_base_image()
        metadata = PngInfo()
        metadata.add_text(key, value)
        
        filepath = os.path.join(base_dir, "NovelAI", filename)
        img.save(filepath, pnginfo=metadata)
        print(f"Created: {filepath}")

def create_clean_files(base_dir):
    """Create clean PNG files without NovelAI metadata."""
    # PNG without any metadata
    img = create_base_image()
    filepath = os.path.join(base_dir, "Clean", "no_metadata.png")
    img.save(filepath)
    print(f"Created: {filepath}")
    
    # PNG with only Stable Diffusion metadata
    img = create_base_image()
    metadata = PngInfo()
    metadata.add_text("parameters", "Stable Diffusion metadata without NovelAI keys: steps:20,sampler:ddim,cfg_scale:7.5")
    
    filepath = os.path.join(base_dir, "Clean", "stable_diffusion_only.png")
    img.save(filepath, pnginfo=metadata)
    print(f"Created: {filepath}")

def create_mixed_files(base_dir):
    """Create PNG files with mixed metadata."""
    img = create_base_image()
    metadata = PngInfo()
    metadata.add_text("Description", "NovelAI description")
    metadata.add_text("parameters", "steps:20,sampler:euler,seed:123456")  # This will be detected by both
    
    filepath = os.path.join(base_dir, "Mixed", "novelai_and_sd.png")
    img.save(filepath, pnginfo=metadata)
    print(f"Created: {filepath}")

def create_edge_case_files(base_dir):
    """Create edge case PNG files."""
    # PNG with empty values
    img = create_base_image()
    metadata = PngInfo()
    metadata.add_text("Description", "")
    metadata.add_text("Comment", "")
    metadata.add_text("Software", "")
    
    filepath = os.path.join(base_dir, "EdgeCases", "empty_values.png")
    img.save(filepath, pnginfo=metadata)
    print(f"Created: {filepath}")
    
    # PNG with large metadata
    img = create_base_image()
    metadata = PngInfo()
    large_text = "A" * 2000  # 2000 character string
    metadata.add_text("Description", large_text)
    
    filepath = os.path.join(base_dir, "EdgeCases", "large_metadata.png")
    img.save(filepath, pnginfo=metadata)
    print(f"Created: {filepath}")

def create_non_png_files(base_dir):
    """Create non-PNG files for negative testing."""
    # Fake PNG (actually text file)
    filepath = os.path.join(base_dir, "NonPNG", "fake.png")
    with open(filepath, 'w') as f:
        f.write("This is not a PNG file")
    print(f"Created: {filepath}")
    
    # JPEG file
    img = create_base_image()
    filepath = os.path.join(base_dir, "NonPNG", "image.jpg")
    img.save(filepath, "JPEG")
    print(f"Created: {filepath}")
    
    # Text file
    filepath = os.path.join(base_dir, "NonPNG", "document.txt")
    with open(filepath, 'w') as f:
        f.write("This is a text document")
    print(f"Created: {filepath}")

def main():
    """Main function to create all test files."""
    print("Creating test files for NovelAI metadata detection...")
    
    base_dir = create_test_directory_structure()
    
    create_complete_metadata_png(base_dir)
    create_individual_key_files(base_dir)
    create_clean_files(base_dir)
    create_mixed_files(base_dir)
    create_edge_case_files(base_dir)
    create_non_png_files(base_dir)
    
    print(f"\nAll test files created in {base_dir}/ directory")
    print("You can now run the NovelAI metadata detection tests.")

if __name__ == "__main__":
    main()