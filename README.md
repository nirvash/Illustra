# Illustra - Fast Image Viewer

[日本語](#概要) | [English](#overview)

## 概要

Illustra は、高速で使いやすい Windows 用画像ビューアです。

### スクリーンショット

![Screenshot](docs/screenshot.png)

### 主な機能

- 🚀 高速なサムネイル表示（仮想化スクロール対応）
- 🤖 AIを活用した高速開発
- 📂 フォルダツリーによる簡単なナビゲーション
- 🖼️ カスタマイズ可能なサムネイルサイズ
- ⌨️ キーボードによる快適な操作
- 🔄 スムーズな画像切り替え
- ⭐ 画像レーティングシステム（1-5 星）とフィルタリング機能
- 🔄 日付/ファイル名によるソート機能
- 🎯 設定の永続化対応
- 📝 Stable Diffusion のプロンプト表示対応

### 開発チーム

- nirvash
- ChatGPT 4.0
- Claude 3.7 Sonnet

### 動作環境

- Windows 10/11
- .NET 9.0 以降
- 推奨: SSD ストレージ

### インストール

1. [Releases](../../releases) から最新版の `Illustra.zip` をダウンロード
2. お好みの場所に展開
3. `Illustra.exe` を実行

### 使い方

1. 左側のフォルダツリーから画像があるフォルダを選択
2. フォルダツリーで右クリックし、「お気に入りに追加」を選択
3. サムネイルをクリックまたはキーボードで選択
3. Enter キーまたはダブルクリックで画像を表示
4. 画像表示中は左右キーまたはマウスホイールで前後の画像に移動
5. 画像にレーティングを付けるにはプロパティパネルの星マークボタンをクリック
6. 特定のレーティングでフィルターするにはフィルターボタンをクリック
7. ソート順を変更するには画面上部のソートボタンをクリック
8. F11 キーでフルスクリーン切り替え
9. Esc キーで画像ビューを閉じる

### キーボードショートカット

| キー     | 動作                             |
| -------- | -------------------------------- |
| ←/→/↑/↓  | サムネイル選択の移動             |
| Home/End | 先頭/末尾のサムネイルへ移動      |
| Enter    | 選択中の画像を表示               |
| 1-5      | 選択中の画像にレーティングを設定 |
| Ctrl+A   | 全選択                           |
| Delete   | ファイルを削除                   |
| F11      | フルスクリーン切り替え           |
| Esc      | 画像ビューを閉じる               |
| S        | スライドショーの開始/停止        |
| +/-      | スライドショーの間隔を増減       |

※ キーボードショートカットはカスタマイズ可能です。設定 → キーボードショートカット設定から変更できます。
以下の機能も設定から任意のキーに割り当てられます：

- レーティングでのフィルタリング（★1-5）
- レーティングフィルタの解除

### ファイル操作

- ドラッグ＆ドロップでのファイル移動
- Ctrl を押しながらのドラッグ＆ドロップでファイルコピー
- Ctrl+A で全てのサムネイルを選択

### 注意事項

レーティング情報はファイルパスに紐づけてデータベースで管理しています。リネームなどでレーティング情報が失われることがあるため、永続的な画像の振り分けなどはフォルダにファイルを移動して分類することをおすすめします。

### 対応言語

- 日本語
- 英語 (English)

言語は設定メニューから切り替え可能です。

## 免責事項

このソフトウェアは現状のまま提供され、明示または黙示を問わず、いかなる種類の保証もありません。使用に伴うリスクはすべてユーザーが負うものとします。

---

## Overview

Illustra is a fast and user-friendly image viewer for Windows. It implements virtualized thumbnail display and efficient memory management to smoothly browse large numbers of images.

### Screenshot

![Screenshot](docs/screenshot.png)

### Key Features

- 🚀 Fast thumbnail display with virtualized scrolling
- 🤖 AI-powered rapid development
- 📂 Easy navigation with folder tree
- 🖼️ Customizable thumbnail size
- ⌨️ Comfortable keyboard operation
- 🔄 Smooth image switching
- ⭐ Image rating system (1-5 stars) with filtering capabilities
- 🔄 Sorting by date or filename
- 🎯 Persistent settings support
- 📝 Stable Diffusion prompt display support

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
2. Right-click on a folder in the folder tree and select "Add to Favorites"
3. Click or use keyboard to select thumbnails
4. Press Enter or double-click to view images
5. Use left/right keys or mouse wheel to navigate between images
6. Assign ratings to images by clicking the star buttons in the property panel
7. Filter by specific rating using the filter buttons
8. Change sort order using the sort buttons at the top
9. Press F11 to toggle fullscreen
10. Press Esc to close image view

### Keyboard Shortcuts

| Key      | Action                        |
| -------- | ----------------------------- |
| ←/→/↑/↓  | Move thumbnail selection      |
| Home/End | Move to first/last thumbnail  |
| Enter    | Display selected image        |
| 1-5      | Set rating for selected image |
| Ctrl+A   | Select all thumbnails         |
| Delete   | Delete file                   |
| F11      | Toggle fullscreen             |
| Esc      | Close image view              |
| S        | Toggle slideshow              |
| +/-      | Adjust slideshow interval     |

Note: Keyboard shortcuts are customizable. You can modify them in Settings → Keyboard Shortcuts.
The following features can also be assigned to any key:

- Rating filters (★1-5)
- Clear rating filter

### File Operations

- Select all thumbnails with Ctrl+A
- Move files with drag & drop
- Copy files with drag & drop while holding Ctrl

### Important Note

Rating information is managed in a database linked to file paths. Rating data may be lost when files are renamed. For permanent image classification, it is recommended to organize files by moving them into folders rather than relying solely on ratings.

### Supported Languages

- Japanese
- English

Language can be changed from the settings menu.

## Development

### Development Team

- nirvash
- ChatGPT 4.0
- Claude 3.7 Sonnet

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

#### Image Processing & UI

- [SkiaSharp](https://github.com/mono/SkiaSharp) - Fast image processing
  - License: MIT
  - Used for: Image processing and thumbnail generation
- [gong-wpf-dragdrop](https://github.com/punker76/gong-wpf-dragdrop) - Drag & Drop functionality
  - License: BSD 3-Clause
  - Used for: File drag & drop operations
- [VirtualizingWrapPanel](https://github.com/sbaeumlisberger/VirtualizingWrapPanel) - Virtualized thumbnail display
  - License: MIT
  - Used for: Virtualized thumbnail list display

#### Application Framework

- [Prism](https://github.com/PrismLibrary/Prism) - MVVM framework
  - License: MIT
  - Used for: Implementation of MVVM architecture
- [Microsoft.Extensions.DependencyInjection](https://github.com/dotnet/runtime) - DI container
  - License: MIT
  - Used for: Dependency injection

#### Data Management

- [Newtonsoft.Json](https://www.newtonsoft.com/json) - JSON framework
  - License: MIT
  - Used for: Settings file serialization/deserialization
- [SQLite-net](https://github.com/praeclarum/sqlite-net) - SQLite ORM
  - License: MIT
  - Used for: Rating data persistence

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## Author

- nirvash

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.
