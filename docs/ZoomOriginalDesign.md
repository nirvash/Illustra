# ウィンドウリサイズ時のズーム処理アルゴリズム解説

このドキュメントでは、ウィンドウサイズが変更された際に画像の表示を一貫性を持って調整するアルゴリズムについて解説します。このアルゴリズムは「フィット優先アプローチ」を採用しており、ウィンドウサイズの変更時にも表示されている画像領域の一貫性を保ちます。

## 1. 基本コンセプト

### 1.1 フィット優先アプローチとは

「フィット優先アプローチ」とは、以下の特徴を持つ画像表示方法です：

1. 初期状態では画像がウィンドウ内に収まるよう自動的にスケールされる
2. ユーザーのズーム操作は、この「フィット状態」に対する相対的な倍率として扱われる
3. ウィンドウサイズが変わると、新しいウィンドウサイズに基づいて「フィット状態」のスケールが再計算される
4. ズーム倍率（ユーザー操作による）は維持されつつ、実際の表示スケールが調整される

このアプローチの利点は、ウィンドウサイズに関係なく一貫した表示を維持できることです。

### 1.2 重要な状態変数

アルゴリズムの実装には以下の状態変数が必要です：

- `imageWidth`, `imageHeight`: 画像の元サイズ
- `windowWidth`, `windowHeight`: 現在のウィンドウサイズ
- `initialFitScale`: 画像がウィンドウに収まる最大のスケール
- `zoomFactor`: ユーザーによるズーム倍率（初期値は1.0）
- `currentScale`: 実際の表示スケール（`initialFitScale * zoomFactor`）
- `panOffsetX`, `panOffsetY`: パン操作によるオフセット値

## 2. 基本アルゴリズム

### 2.1 初期フィットスケールの計算

```
// 画像がウィンドウに収まる最大スケールを計算
initialFitScale = Math.min(windowWidth / imageWidth, windowHeight / imageHeight)

// 現在のスケールを更新
currentScale = initialFitScale * zoomFactor
```

### 2.2 ズーム操作の処理

ズーム操作は一般的に特定の点（多くの場合はマウスカーソル位置）を中心に行われます：

```
// ズーム前のカーソル位置の画像上での座標を計算
imageX = (mouseX - panOffsetX) / currentScale
imageY = (mouseY - panOffsetY) / currentScale

// ズーム倍率の更新
zoomFactor = zoomFactor + delta  // delta はマウスホイールの回転量など

// 新しいスケールの計算
currentScale = initialFitScale * zoomFactor

// カーソル位置を中心としたズーム処理
panOffsetX = mouseX - imageX * currentScale
panOffsetY = mouseY - imageY * currentScale
```

### 2.3 ウィンドウリサイズの処理

ウィンドウリサイズ時には、表示中心点（または他の基準点）を維持することが重要です：

```
// リサイズ前の表示中心の画像上での座標を計算
centerImageX = (windowWidth / 2 - panOffsetX) / currentScale
centerImageY = (windowHeight / 2 - panOffsetY) / currentScale

// ウィンドウサイズの更新
windowWidth = newWindowWidth
windowHeight = newWindowHeight

// 新しいフィットスケールの計算
initialFitScale = Math.min(windowWidth / imageWidth, windowHeight / imageHeight)

// 新しいスケールの計算（ズーム倍率は維持）
currentScale = initialFitScale * zoomFactor

// 中心点を維持するためのパンオフセットの再計算
panOffsetX = windowWidth / 2 - centerImageX * currentScale
panOffsetY = windowHeight / 2 - centerImageY * currentScale
```

## 3. 実装のポイント

### 3.1 表示一貫性の維持

ウィンドウリサイズ時の表示一貫性を保つ鍵は：

1. **基準点の選択**：リサイズ前後で同じ画像上の点が同じ相対位置に表示される
2. **ズーム倍率の維持**：相対的なズームレベルを保つため、`zoomFactor`は変更しない
3. **パンオフセットの調整**：基準点が同じ位置に来るよう計算

### 3.2 境界ケースの処理

実装時には以下の境界ケースを考慮する必要があります：

1. **最小/最大ズーム制限**：極端なズームインやズームアウトを制限
   ```
   zoomFactor = Math.max(minZoom, Math.min(maxZoom, zoomFactor))
   ```

2. **画像が表示領域より小さい場合**：中央に配置する
   ```
   if (imageWidth * currentScale < windowWidth) {
     panOffsetX = (windowWidth - imageWidth * currentScale) / 2
   }
   ```

3. **パン範囲の制限**：画像が表示領域から大きく外れないよう制限する
   ```
   // 左端の制限
   panOffsetX = Math.min(panOffsetX, windowWidth / 2)
   
   // 右端の制限
   panOffsetX = Math.max(panOffsetX, windowWidth - imageWidth * currentScale - windowWidth / 2)
   ```

### 3.3 パフォーマンスの最適化

頻繁なリサイズイベントに対して以下の最適化が有効です：

1. **イベントの間引き**：短時間に多数のリサイズイベントが発生する場合、間引きを行う
   ```
   let resizeTimeout
   window.addEventListener('resize', function() {
     clearTimeout(resizeTimeout)
     resizeTimeout = setTimeout(handleResize, 100)
   })
   ```

2. **描画の最適化**：必要な場合のみ再描画を行う
   ```
   if (oldScale !== currentScale || oldPanX !== panOffsetX || oldPanY !== panOffsetY) {
     render()
   }
   ```

## 4. より高度な実装

### 4.1 代替基準点の使用

表示中心以外の基準点を使用する場合は、同じ原理でアルゴリズムを調整できます：

- **カーソル位置基準**：マウスカーソルの位置を基準点として使用
- **選択オブジェクト基準**：画像内の特定のオブジェクトを基準点として使用

### 4.2 アスペクト比を考慮した処理

ウィンドウのアスペクト比が大きく変わる場合は、スケールだけでなく表示位置も調整する必要があります：

```
// アスペクト比の変化
const oldAspect = oldWindowWidth / oldWindowHeight
const newAspect = windowWidth / windowHeight

// アスペクト比に応じた調整（必要に応じて）
if (Math.abs(oldAspect - newAspect) > 0.1) {
  // アスペクト比に応じた追加の調整
}
```

## 5. 実装例

以下は簡略化されたコード例です（実際の実装ではより詳細な処理が必要な場合があります）：

```javascript
// 状態管理
const state = {
  imageWidth: 1000,
  imageHeight: 1000,
  windowWidth: 500,
  windowHeight: 500,
  initialFitScale: 1,
  zoomFactor: 1,
  currentScale: 1,
  panOffsetX: 0,
  panOffsetY: 0
};

// 初期フィットスケールの計算
function calculateInitialFitScale() {
  state.initialFitScale = Math.min(
    state.windowWidth / state.imageWidth,
    state.windowHeight / state.imageHeight
  );
  state.currentScale = state.initialFitScale * state.zoomFactor;
}

// ウィンドウリサイズ処理
function handleResize(newWidth, newHeight) {
  // 中心点の画像上での座標を記録
  const centerImageX = (state.windowWidth / 2 - state.panOffsetX) / state.currentScale;
  const centerImageY = (state.windowHeight / 2 - state.panOffsetY) / state.currentScale;
  
  // ウィンドウサイズの更新
  state.windowWidth = newWidth;
  state.windowHeight = newHeight;
  
  // スケールの再計算
  calculateInitialFitScale();
  
  // パンオフセットの再計算
  state.panOffsetX = state.windowWidth / 2 - centerImageX * state.currentScale;
  state.panOffsetY = state.windowHeight / 2 - centerImageY * state.currentScale;
  
  // 画面の更新
  render();
}

// ズーム処理
function handleZoom(mouseX, mouseY, delta) {
  // ズーム前の画像上での座標
  const imageX = (mouseX - state.panOffsetX) / state.currentScale;
  const imageY = (mouseY - state.panOffsetY) / state.currentScale;
  
  // ズーム倍率の更新
  state.zoomFactor = Math.max(0.1, Math.min(10, state.zoomFactor + delta));
  
  // スケールの再計算
  state.currentScale = state.initialFitScale * state.zoomFactor;
  
  // パンオフセットの再計算
  state.panOffsetX = mouseX - imageX * state.currentScale;
  state.panOffsetY = mouseY - imageY * state.currentScale;
  
  // 画面の更新
  render();
}
```

## 6. まとめ

このアルゴリズムの主なポイントは以下の通りです：

1. ウィンドウサイズに応じた「フィットスケール」と、ユーザー操作による「ズーム倍率」を分離して管理する
2. ウィンドウリサイズ時には基準点（通常は表示中心）を維持する
3. 実際の表示スケールは「フィットスケール × ズーム倍率」で計算する
4. パンオフセットは基準点が同じ位置に来るよう再計算する

この方法により、ウィンドウサイズが変わっても、ユーザーが見ている画像の領域が一貫して維持され、自然な表示体験を提供できます。