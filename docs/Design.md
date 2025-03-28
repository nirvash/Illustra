# Illustra 設計ドキュメント

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

## Null参照警告の無効化について

プロジェクトでは、コードの開発を効率化するために、特定のNull参照関連の警告を無効化しています。これは主にC# 8.0以降の非Nullableリファレンス型機能に関連する警告です。

### 無効化している警告コード

プロジェクト設定で以下の警告を無効化しています：

| 警告コード | 説明 | 無効化理由 |
|------------|------|------------|
| CS8600 | null リテラルまたは null 値の可能性のある値を Null 非許容型に変換しています | レガシーコードとの互換性維持、および値の取得時に明示的なnullチェックを行うため |
| CS8601 | Null 参照代入の可能性があります | コントロール初期化時など、実行時には必ずnull以外になる場合があるため |
| CS8602 | null 参照の可能性があるものの逆参照です | コードフロー分析の限界により実際には常にnull以外である状況を検出できないケースへの対応 |
| CS8603 | Null 参照戻り値の可能性があります | メソッドの戻り値に関する過度な警告を避けるため |
| CS8604 | Null 参照引数の可能性があります | 既存APIの利用時にnull引数を許容するケースがあるため |
| CS8618 | null 非許容のフィールドには、コンストラクターの終了時に null 以外の値が入っていなければなりません | WPFのXAMLロードでプロパティが初期化される場合など、コンストラクタ以外で初期化されるパターンへの対応 |
| CS8625 | null リテラルを null 非許容参照型に変換できません | 条件付きのnull代入が必要なケースへの対応 |
| IDE0059 | 不要な代入（使用されない変数に関する警告） | デバッグ時やコード修正中の一時的な変数宣言への対応 |

### 警告無効化の代替策

長期的には、以下のアプローチを検討することでより安全なコードを目指せます：

1. **Null許容マーク(`?`)の適切な使用**
   - nullになり得るプロパティやフィールドには `string?` のようにnull許容として明示的に宣言する

2. **Null許容分析の活用**
   - Null許容フロー分析を活用するため、適切なnullチェックを実装する
   ```csharp
   if (value != null)
   {
       // コンパイラはここでvalueがnullでないことを認識する
       DoSomething(value);
   }
   ```

3. **Null結合演算子、Null条件演算子の活用**
   ```csharp
   // Null結合演算子
   string displayName = userName ?? "Guest";

   // Null条件演算子
   int? length = text?.Length;
   ```

4. **初期化パターンの改善**
   - `required` 修飾子（C# 11）の使用やファクトリメソッドパターンの採用

### 将来計画

将来的には、コードベースの成熟に伴い、これらの警告抑制を段階的に解除し、より型安全なコードベースを目指します。特にライブラリやAPIの境界では、型の安全性を高めるために警告に対応することが重要です。

## 使用ライブラリ

- TBD

## ディレクトリ構成

```
TBD
```

## テスト構成

```
TBD
```
