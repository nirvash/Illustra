# Illustra - Fast Image Viewer

[日本語](#概要) | [English](#overview)

## 概要

Illustraは、高速で使いやすいWindows用画像ビューアです。大量の画像をスムーズに閲覧できるよう、仮想化されたサムネイル表示と効率的なメモリ管理を実装しています。

### 主な機能

- 🚀 高速なサムネイル表示（仮想化スクロール対応）
- 📂 フォルダツリーによる簡単なナビゲーション
- 🖼️ カスタマイズ可能なサムネイルサイズ
- ⌨️ キーボードによる快適な操作
- 🔄 スムーズな画像切り替え
- 🎯 設定の永続化対応

### 動作環境

- Windows 10/11
- .NET 9.0 以降
- 推奨: SSDストレージ

### インストール

1. [Releases](../../releases) から最新版の `Illustra.zip` をダウンロード
2. お好みの場所に展開
3. `Illustra.exe` を実行

### 使い方

1. 左側のフォルダツリーから画像があるフォルダを選択
2. サムネイルをクリックまたはキーボードで選択
3. Enter キーまたはダブルクリックで画像を表示
4. 画像表示中は左右キーまたはマウスホイールで前後の画像に移動
5. F11 キーでフルスクリーン切り替え
6. Esc キーで画像ビューを閉じる

### キーボードショートカット

| キー | 動作 |
|------|------|
| ←/→/↑/↓ | サムネイル選択の移動 |
| Home/End | 先頭/末尾のサムネイルへ移動 |
| Enter | 選択中の画像を表示 |
| F11 | フルスクリーン切り替え |
| Esc | 画像ビューを閉じる |

---

## Overview

Illustra is a fast and user-friendly image viewer for Windows. It implements virtualized thumbnail display and efficient memory management to smoothly browse large numbers of images.

### Key Features

- 🚀 Fast thumbnail display with virtualized scrolling
- 📂 Easy navigation with folder tree
- 🖼️ Customizable thumbnail size
- ⌨️ Comfortable keyboard operation
- 🔄 Smooth image switching
- 🎯 Persistent settings support

### System Requirements

- Windows 10/11
- .NET 9.0 or later
- Recommended: SSD storage

### Installation

1. Download `Illustra.zip` from [Releases](../../releases)
2. Extract to your preferred location
3. Run `Illustra.exe`

### Usage

1. Select an image folder from the folder tree on the left
2. Click or use keyboard to select thumbnails
3. Press Enter or double-click to view images
4. Use left/right keys or mouse wheel to navigate between images
5. Press F11 to toggle fullscreen
6. Press Esc to close image view

### Keyboard Shortcuts

| Key | Action |
|-----|--------|
| ←/→/↑/↓ | Move thumbnail selection |
| Home/End | Move to first/last thumbnail |
| Enter | Display selected image |
| F11 | Toggle fullscreen |
| Esc | Close image view |

## Development

### Requirements

- Visual Studio 2022 or later
- .NET 9.0 SDK

### Build

```bash
dotnet build
```

### Run

```bash
dotnet run --project src/Illustra/Illustra.csproj
```

### Dependencies

- [SkiaSharp](https://github.com/mono/SkiaSharp) - Fast image processing
- [VirtualizingWrapPanel](https://github.com/sbaeumlisberger/VirtualizingWrapPanel) - Virtualized thumbnail display
- [Newtonsoft.Json](https://www.newtonsoft.com/json) - Settings serialization

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## Author

- nirvash

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

