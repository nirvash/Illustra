# BooleanToVisibilityConverter 修正計画

## 問題
- `ThumbnailListControl.xaml`で`BooleanToVisibilityConverter`という名前でコンバーターを参照
- `App.xaml`では同じコンバーターが`BoolToVisibilityConverter`という名前で定義
- この名前の不一致によりXAMLパース時にリソースが見つからないエラーが発生

## 解決方法
1. App.xamlのコンバーター定義を修正
   ```xaml
   <!-- 変更前 -->
   <BooleanToVisibilityConverter x:Key="BoolToVisibilityConverter"/>

   <!-- 変更後 -->
   <BooleanToVisibilityConverter x:Key="BooleanToVisibilityConverter"/>
   ```

## 実装計画
1. App.xamlの修正
   - コンバーターのキー名を`BoolToVisibilityConverter`から`BooleanToVisibilityConverter`に変更
   - この変更により、ThumbnailListControlからの参照と一致

## 影響範囲
- App.xamlで定義されたコンバーターを使用している他のビューがある可能性
- 実装時に他のファイルでの使用箇所も確認が必要

## 実装ステップ
1. Codeモードに切り替え
2. App.xamlのコンバーター定義を修正
3. 他のファイルでの影響を確認
4. 必要に応じて追加の修正を実施
