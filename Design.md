# Illustra 実装設計

## 基本的な画像ビューア機能

### 1. 画像の読み込み

- 画像ファイルを選択し、メモリに読み込む。
- 対応フォーマット: JPEG, PNG, BMP, GIF, WebP
- 将来的に mp4 をサポート予定

### 2. 画像の表示

- imgui-rs を使用して画像をウィンドウに表示する。
- 画像のサイズに合わせてウィンドウのサイズを調整する。

### 3. 画像のズーム・パン操作

- マウスホイールで画像のズームイン・ズームアウトを実装。
- クリック＆ドラッグで画像のパン操作を実装。

### 4. Exif 情報の表示

- 画像の Exif 情報を読み込み、別ウィンドウに表示する。
- Exif 情報の編集は行わない。

### 5. プロンプト情報の表示

- 画像に関連するプロンプト情報を表示する。
- プロンプト情報はテキストファイルなどから読み込む。

## 使用ライブラリ

- imgui-rs + imgui-wgpu-rs + imgui-winit-support: UI フレームワークとレンダリング
- image: 基本的な画像処理（JPEG, PNG, BMP, GIF）
- webp: WebP 形式のサポート
- kamadak-exif: Exif 情報の読み込み
- winit: ウィンドウ作成と入力処理
- wgpu: グラフィックス API
- rfd: ファイル選択ダイアログ

## ディレクトリ構成

```
src/
├── main.rs               - エントリーポイント
├── app.rs                - アプリケーションの初期化と実行
├── app_core/             - コアロジック
│   ├── mod.rs            - app_coreモジュールの定義と公開インターフェース
│   ├── image/            - 画像処理
│   │   ├── mod.rs        - imageモジュールの定義とサブモジュールの公開
│   │   ├── loader.rs     - 画像読み込み
│   │   └── cache.rs      - 画像キャッシュ
│   ├── metadata/         - メタデータ処理
│   │   ├── mod.rs        - metadataモジュールの定義
│   │   ├── exif.rs       - Exif解析
│   │   └── prompt.rs     - プロンプト情報
│   └── state.rs          - アプリケーション状態
├── ui/                   - UI関連
│   ├── mod.rs            - uiモジュールの定義とUIコンポーネントの公開
│   ├── context.rs        - ImGuiコンテキスト管理
│   ├── windows/          - 各ウィンドウ
│   │   ├── mod.rs        - windowsモジュールの定義
│   │   ├── main_view.rs  - メインビューウィンドウ
│   │   ├── exif_view.rs  - Exif情報ウィンドウ
│   │   └── prompt_view.rs- プロンプト情報ウィンドウ
│   └── widgets/          - 再利用可能なUI部品
│       ├── mod.rs        - widgetsモジュールの定義
│       └── zoom_control.rs- ズームコントロール
└── utils/                - ユーティリティ
    ├── mod.rs            - utilsモジュールの定義と公開関数
    ├── file.rs           - ファイル操作
    └── error.rs          - エラー定義
```

## テスト構成

```
tests/
├── integration/              - 統合テスト
│   ├── image_loading_test.rs - 画像読み込みの統合テスト
│   └── ui_test.rs            - UI機能の統合テスト
├── unit/                     - 単体テスト
│   ├── app_core/             - コアロジックのテスト
│   │   ├── image_test.rs     - 画像処理のテスト
│   │   └── metadata_test.rs  - メタデータ処理のテスト
│   └── utils_test.rs         - ユーティリティのテスト
└── test_utils/               - テスト用ユーティリティ
    ├── mod.rs                - テストユーティリティの定義
    ├── mock_loader.rs        - モックローダー
    └── test_images.rs        - テスト用画像データ
```
