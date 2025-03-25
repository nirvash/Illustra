# 画像表示システムにおけるパンオフセット計算の詳細解説

この文書では、画像表示システムにおけるパンオフセットの計算方法について、特にウィンドウリサイズ時の処理を中心に詳しく解説します。サンプルコードと図解を用いて、座標系の変換とパンオフセットの計算を明確にします。

## 1. 座標系の理解

パンオフセット計算で最初に理解すべきは、関連する座標系です：

### 1.1 主要な座標系

1. **画像座標系**：画像内の位置（ピクセル単位）
   - 原点 (0,0) は画像の左上
   - 単位はピクセル
   - 例: 画像の中心点は (imageWidth/2, imageHeight/2)

2. **表示座標系**：ウィンドウ/キャンバス内の位置（ピクセル単位）
   - 原点 (0,0) はウィンドウの左上
   - 単位はピクセル
   - 例: ウィンドウの中心点は (windowWidth/2, windowHeight/2)

### 1.2 座標変換の基本

画像座標系と表示座標系の間の変換には、**スケール**と**パンオフセット**が必要です：

1. **画像座標系 → 表示座標系**：
   ```javascript
   displayX = imageX * currentScale + panOffsetX
   displayY = imageY * currentScale + panOffsetY
   ```

2. **表示座標系 → 画像座標系**：
   ```javascript
   imageX = (displayX - panOffsetX) / currentScale
   imageY = (displayY - panOffsetY) / currentScale
   ```

## 2. パンオフセットの意味と計算方法

### 2.1 パンオフセットとは

**パンオフセット**は、画像の表示位置を調整するための値です：

- `panOffsetX`、`panOffsetY` は、画像の原点 (0,0) が表示座標系のどこに位置するかを表す
- 正の値は画像が右または下にシフトすることを意味する
- 負の値は画像が左または上にシフトすることを意味する

### 2.2 初期パンオフセットの計算（中央揃え）

画像を中央に配置するための初期パンオフセットの計算：

```javascript
// 画像を中央に配置するためのパンオフセット
function calculateCenterPanOffset() {
  // 画像のスケーリング後のサイズ
  const scaledImageWidth = imageWidth * currentScale;
  const scaledImageHeight = imageHeight * currentScale;
  
  // 中央揃えのためのオフセット
  const panOffsetX = (windowWidth - scaledImageWidth) / 2;
  const panOffsetY = (windowHeight - scaledImageHeight) / 2;
  
  return { panOffsetX, panOffsetY };
}
```

### 2.3 パン操作によるオフセットの更新

マウスドラッグによるパン操作時のオフセット更新：

```javascript
// マウスドラッグ開始
let isDragging = false;
let dragStartX = 0;
let dragStartY = 0;
let startPanOffsetX = 0;
let startPanOffsetY = 0;

// ドラッグ開始時
function handleMouseDown(e) {
  isDragging = true;
  dragStartX = e.clientX;
  dragStartY = e.clientY;
  startPanOffsetX = panOffsetX;
  startPanOffsetY = panOffsetY;
}

// ドラッグ中
function handleMouseMove(e) {
  if (!isDragging) return;
  
  // マウス移動量
  const deltaX = e.clientX - dragStartX;
  const deltaY = e.clientY - dragStartY;
  
  // パンオフセットの更新
  panOffsetX = startPanOffsetX + deltaX;
  panOffsetY = startPanOffsetY + deltaY;
  
  // 画面更新
  render();
}

// ドラッグ終了
function handleMouseUp() {
  isDragging = false;
}
```

## 3. ウィンドウリサイズ時のパンオフセット計算

ウィンドウリサイズ時に表示中の画像領域を維持するためのパンオフセット計算は、最も複雑で混乱しやすい部分です。以下に詳細なステップと具体的なコードを示します。

### 3.1 基準点（アンカーポイント）の維持

ウィンドウリサイズ時には、ある「基準点」（多くの場合は表示領域の中心）が画像上の同じ位置を指すように調整します：

```javascript
function handleResize(newWidth, newHeight) {
  // ステップ1: リサイズ前の基準点の画像座標を計算
  // （表示領域の中心を基準点とする場合）
  const oldCenterDisplayX = windowWidth / 2;
  const oldCenterDisplayY = windowHeight / 2;
  
  // 表示座標系から画像座標系への変換
  const centerImageX = (oldCenterDisplayX - panOffsetX) / currentScale;
  const centerImageY = (oldCenterDisplayY - panOffsetY) / currentScale;
  
  // ステップ2: ウィンドウサイズの更新
  windowWidth = newWidth;
  windowHeight = newHeight;
  
  // ステップ3: 新しいフィットスケールと表示スケールの計算
  const oldScale = currentScale;
  initialFitScale = Math.min(
    windowWidth / imageWidth,
    windowHeight / imageHeight
  );
  currentScale = initialFitScale * zoomFactor;
  
  // ステップ4: 新しいウィンドウサイズでの基準点（中心）の表示座標
  const newCenterDisplayX = windowWidth / 2;
  const newCenterDisplayY = windowHeight / 2;
  
  // ステップ5: 同じ画像上の点が中心に来るようにパンオフセットを計算
  // この式が最も重要で混乱しやすい部分
  panOffsetX = newCenterDisplayX - (centerImageX * currentScale);
  panOffsetY = newCenterDisplayY - (centerImageY * currentScale);
  
  // ステップ6: 画面更新
  render();
}
```

### 3.2 パンオフセット計算のロジック解説

上記コードのステップ5のパンオフセット計算を詳細に解説します：

```javascript
panOffsetX = newCenterDisplayX - (centerImageX * currentScale);
panOffsetY = newCenterDisplayY - (centerImageY * currentScale);
```

このロジックを理解するために、座標変換の式を使います：

1. 私たちが維持したいのは、画像上の点 (centerImageX, centerImageY) が新しいウィンドウの中心 (newCenterDisplayX, newCenterDisplayY) に表示されることです。

2. 表示座標系と画像座標系の変換式を思い出すと：
   ```
   displayX = imageX * currentScale + panOffsetX
   ```

3. これを panOffsetX について解くと：
   ```
   panOffsetX = displayX - (imageX * currentScale)
   ```

4. つまり：
   - displayX に newCenterDisplayX を代入
   - imageX に centerImageX を代入
   - 新しい currentScale を使用

結果として、新しいパンオフセットが計算されます。

### 3.3 具体的な数値例

数値例を通して理解を深めましょう：

**初期状態**:
- 画像サイズ: 1000×1000 ピクセル
- ウィンドウサイズ: 500×500 ピクセル
- 初期フィットスケール: 0.5（500 / 1000）
- ズーム倍率: 1.0
- 表示スケール: 0.5（0.5 × 1.0）
- パンオフセット: (0, 0)（中央配置の場合は実際には (250, 250)）

**ユーザー操作**:
- ズーム: ズーム倍率を2.0に変更（表示スケール: 1.0）
- パン: 右に100ピクセル、下に50ピクセル移動
- 現在のパンオフセット: (350, 300)

**ウィンドウリサイズ**:
- 新しいウィンドウサイズ: 800×400 ピクセル

**計算ステップ**:
1. 現在の表示中心の画像座標を計算:
   ```
   centerImageX = (500/2 - 350) / 1.0 = -100
   centerImageY = (500/2 - 300) / 1.0 = -50
   ```

2. 新しいフィットスケールを計算:
   ```
   初期フィットスケール = min(800/1000, 400/1000) = 0.4
   ```

3. 新しい表示スケールを計算:
   ```
   表示スケール = 0.4 × 2.0 = 0.8
   ```

4. 新しいパンオフセットを計算:
   ```
   panOffsetX = 800/2 - (-100 × 0.8) = 400 + 80 = 480
   panOffsetY = 400/2 - (-50 × 0.8) = 200 + 40 = 240
   ```

このようにして、リサイズ後も画像上の同じ点が表示中心に来るようパンオフセットが調整されます。

## 4. ズーム操作との連携

### 4.1 マウス位置を中心としたズーム

ズーム操作時には、マウスポインタの位置を中心として拡大・縮小するのが自然です：

```javascript
function handleZoom(mouseX, mouseY, deltaZoom) {
  // ステップ1: ズーム前のマウス位置の画像座標を計算
  const mouseImageX = (mouseX - panOffsetX) / currentScale;
  const mouseImageY = (mouseY - panOffsetY) / currentScale;
  
  // ステップ2: ズーム倍率の更新
  const oldZoomFactor = zoomFactor;
  zoomFactor = Math.max(minZoomFactor, Math.min(maxZoomFactor, zoomFactor + deltaZoom));
  
  // ステップ3: 表示スケールの更新
  const oldScale = currentScale;
  currentScale = initialFitScale * zoomFactor;
  
  // ステップ4: マウス位置が同じ画像点を指すようにパンオフセットを調整
  panOffsetX = mouseX - (mouseImageX * currentScale);
  panOffsetY = mouseY - (mouseImageY * currentScale);
  
  // ステップ5: 画面更新
  render();
}
```

## 5. 境界制約の適用

実際のアプリケーションでは、パンの範囲を制限して画像が大きく画面外に出ないようにすることがあります：

```javascript
function constrainPanOffset() {
  // 画像の表示サイズ
  const scaledWidth = imageWidth * currentScale;
  const scaledHeight = imageHeight * currentScale;
  
  // 最小表示範囲（画像の少なくとも20%は表示する）
  const minVisibleWidth = Math.min(scaledWidth * 0.2, windowWidth * 0.5);
  const minVisibleHeight = Math.min(scaledHeight * 0.2, windowHeight * 0.5);
  
  // X軸の制約
  if (scaledWidth <= windowWidth) {
    // 画像が表示領域より小さい場合は中央に配置
    panOffsetX = (windowWidth - scaledWidth) / 2;
  } else {
    // 画像が表示領域より大きい場合は範囲を制限
    const maxPanX = minVisibleWidth;
    const minPanX = windowWidth - scaledWidth + minVisibleWidth;
    panOffsetX = Math.max(minPanX, Math.min(maxPanX, panOffsetX));
  }
  
  // Y軸の制約（X軸と同様）
  if (scaledHeight <= windowHeight) {
    panOffsetY = (windowHeight - scaledHeight) / 2;
  } else {
    const maxPanY = minVisibleHeight;
    const minPanY = windowHeight - scaledHeight + minVisibleHeight;
    panOffsetY = Math.max(minPanY, Math.min(maxPanY, panOffsetY));
  }
}
```

## 6. 完全な実装例

以下に、パンとズーム操作を含む完全な実装例を示します：

```javascript
// 画像ビューア - 完全な実装例
class ImageViewer {
  constructor(containerElement, image) {
    // 要素の参照
    this.container = containerElement;
    this.canvas = document.createElement('canvas');
    this.ctx = this.canvas.getContext('2d');
    this.container.appendChild(this.canvas);
    
    // 画像情報
    this.image = image;
    this.imageWidth = image.width;
    this.imageHeight = image.height;
    
    // ウィンドウ情報
    this.windowWidth = this.container.clientWidth;
    this.windowHeight = this.container.clientHeight;
    this.canvas.width = this.windowWidth;
    this.canvas.height = this.windowHeight;
    
    // 表示状態
    this.initialFitScale = 1;
    this.zoomFactor = 1;
    this.currentScale = 1;
    this.panOffsetX = 0;
    this.panOffsetY = 0;
    
    // ドラッグ状態
    this.isDragging = false;
    this.dragStartX = 0;
    this.dragStartY = 0;
    this.startPanOffsetX = 0;
    this.startPanOffsetY = 0;
    
    // 初期化
    this.calculateInitialFitScale();
    this.centerImage();
    this.setupEventListeners();
    this.render();
  }
  
  // 初期フィットスケールの計算
  calculateInitialFitScale() {
    this.initialFitScale = Math.min(
      this.windowWidth / this.imageWidth,
      this.windowHeight / this.imageHeight
    );
    this.currentScale = this.initialFitScale * this.zoomFactor;
  }
  
  // 画像を中央に配置
  centerImage() {
    this.panOffsetX = (this.windowWidth - this.imageWidth * this.currentScale) / 2;
    this.panOffsetY = (this.windowHeight - this.imageHeight * this.currentScale) / 2;
  }
  
  // イベントリスナーの設定
  setupEventListeners() {
    // ホイールイベント（ズーム）
    this.container.addEventListener('wheel', (e) => {
      e.preventDefault();
      const rect = this.canvas.getBoundingClientRect();
      const mouseX = e.clientX - rect.left;
      const mouseY = e.clientY - rect.top;
      
      // ズーム量（正: ズームイン, 負: ズームアウト）
      const delta = -Math.sign(e.deltaY) * 0.1;
      this.zoom(mouseX, mouseY, delta);
    });
    
    // マウスダウン（パン開始）
    this.container.addEventListener('mousedown', (e) => {
      if (e.button === 0) { // 左クリック
        this.isDragging = true;
        this.dragStartX = e.clientX;
        this.dragStartY = e.clientY;
        this.startPanOffsetX = this.panOffsetX;
        this.startPanOffsetY = this.panOffsetY;
        this.container.style.cursor = 'grabbing';
      }
    });
    
    // マウス移動（パン中）
    window.addEventListener('mousemove', (e) => {
      if (!this.isDragging) return;
      
      const deltaX = e.clientX - this.dragStartX;
      const deltaY = e.clientY - this.dragStartY;
      
      this.panOffsetX = this.startPanOffsetX + deltaX;
      this.panOffsetY = this.startPanOffsetY + deltaY;
      
      this.render();
    });
    
    // マウスアップ（パン終了）
    window.addEventListener('mouseup', () => {
      if (this.isDragging) {
        this.isDragging = false;
        this.container.style.cursor = 'grab';
      }
    });
    
    // コンテナにマウスが入った時
    this.container.addEventListener('mouseenter', () => {
      if (!this.isDragging) {
        this.container.style.cursor = 'grab';
      }
    });
    
    // コンテナからマウスが出た時
    this.container.addEventListener('mouseleave', () => {
      this.container.style.cursor = 'default';
    });
    
    // リサイズイベント
    let resizeTimeout;
    window.addEventListener('resize', () => {
      clearTimeout(resizeTimeout);
      resizeTimeout = setTimeout(() => {
        this.handleResize();
      }, 100);
    });
  }
  
  // ズーム処理
  zoom(mouseX, mouseY, deltaZoom) {
    // ズーム前のマウス位置の画像座標を計算
    const mouseImageX = (mouseX - this.panOffsetX) / this.currentScale;
    const mouseImageY = (mouseY - this.panOffsetY) / this.currentScale;
    
    // ズーム倍率の更新
    const oldZoomFactor = this.zoomFactor;
    this.zoomFactor = Math.max(0.1, Math.min(10, this.zoomFactor + deltaZoom));
    
    // 表示スケールの更新
    const oldScale = this.currentScale;
    this.currentScale = this.initialFitScale * this.zoomFactor;
    
    // マウス位置が同じ画像点を指すようにパンオフセットを調整
    this.panOffsetX = mouseX - (mouseImageX * this.currentScale);
    this.panOffsetY = mouseY - (mouseImageY * this.currentScale);
    
    // 制約の適用とレンダリング
    this.constrainPanOffset();
    this.render();
  }
  
  // ウィンドウリサイズ処理
  handleResize() {
    // リサイズ前の表示中心の画像座標を計算
    const centerDisplayX = this.windowWidth / 2;
    const centerDisplayY = this.windowHeight / 2;
    const centerImageX = (centerDisplayX - this.panOffsetX) / this.currentScale;
    const centerImageY = (centerDisplayY - this.panOffsetY) / this.currentScale;
    
    // 新しいウィンドウサイズを取得
    this.windowWidth = this.container.clientWidth;
    this.windowHeight = this.container.clientHeight;
    this.canvas.width = this.windowWidth;
    this.canvas.height = this.windowHeight;
    
    // 新しいフィットスケールと表示スケールを計算
    const oldScale = this.currentScale;
    this.calculateInitialFitScale();
    
    // 新しい中心点を計算
    const newCenterDisplayX = this.windowWidth / 2;
    const newCenterDisplayY = this.windowHeight / 2;
    
    // 同じ画像点が中心に来るようパンオフセットを調整
    this.panOffsetX = newCenterDisplayX - (centerImageX * this.currentScale);
    this.panOffsetY = newCenterDisplayY - (centerImageY * this.currentScale);
    
    // 制約の適用とレンダリング
    this.constrainPanOffset();
    this.render();
  }
  
  // パンオフセットの制約適用
  constrainPanOffset() {
    // 画像の表示サイズ
    const scaledWidth = this.imageWidth * this.currentScale;
    const scaledHeight = this.imageHeight * this.currentScale;
    
    // X軸の制約
    if (scaledWidth <= this.windowWidth) {
      // 画像が表示領域より小さい場合は中央に配置
      this.panOffsetX = (this.windowWidth - scaledWidth) / 2;
    } else {
      // 画像が表示領域より大きい場合は範囲を制限
      const maxPanX = this.windowWidth * 0.2;
      const minPanX = this.windowWidth - scaledWidth - maxPanX;
      this.panOffsetX = Math.max(minPanX, Math.min(maxPanX, this.panOffsetX));
    }
    
    // Y軸の制約（X軸と同様）
    if (scaledHeight <= this.windowHeight) {
      this.panOffsetY = (this.windowHeight - scaledHeight) / 2;
    } else {
      const maxPanY = this.windowHeight * 0.2;
      const minPanY = this.windowHeight - scaledHeight - maxPanY;
      this.panOffsetY = Math.max(minPanY, Math.min(maxPanY, this.panOffsetY));
    }
  }
  
  // 描画処理
  render() {
    // キャンバスのクリア
    this.ctx.fillStyle = '#f0f0f0';
    this.ctx.fillRect(0, 0, this.windowWidth, this.windowHeight);
    
    // 画像の描画
    this.ctx.save();
    this.ctx.translate(this.panOffsetX, this.panOffsetY);
    this.ctx.scale(this.currentScale, this.currentScale);
    this.ctx.drawImage(this.image, 0, 0);
    
    // デバッグ用：画像の境界を表示
    this.ctx.strokeStyle = 'red';
    this.ctx.lineWidth = 1 / this.currentScale;
    this.ctx.strokeRect(0, 0, this.imageWidth, this.imageHeight);
    this.ctx.restore();
    
    // ステータス情報（オプション）
    this.drawStatus();
  }
  
  // ステータス情報の表示（デバッグ用）
  drawStatus() {
    this.ctx.fillStyle = 'rgba(0, 0, 0, 0.5)';
    this.ctx.fillRect(10, this.windowHeight - 60, 300, 50);
    this.ctx.fillStyle = 'white';
    this.ctx.font = '12px Arial';
    this.ctx.fillText(`スケール: ${this.currentScale.toFixed(2)} (フィット: ${this.initialFitScale.toFixed(2)} × ズーム: ${this.zoomFactor.toFixed(2)})`, 20, this.windowHeight - 40);
    this.ctx.fillText(`パンオフセット: (${this.panOffsetX.toFixed(0)}, ${this.panOffsetY.toFixed(0)})`, 20, this.windowHeight - 20);
  }
  
  // ビューをリセット
  reset() {
    this.zoomFactor = 1;
    this.calculateInitialFitScale();
    this.centerImage();
    this.render();
  }
  
  // コンテナサイズを変更（テスト用）
  resizeContainer(width, height) {
    this.container.style.width = `${width}px`;
    this.container.style.height = `${height}px`;
    this.handleResize();
  }
}

// 使用例
// const imageViewer = new ImageViewer(document.getElementById('viewer-container'), imageElement);
```

## 7. よくある問題とその解決策

### 7.1 パンオフセットの計算が正しくない

症状: リサイズ後に画像の位置が大きくずれる

解決策:
- 座標変換の順序を確認（スケーリング→パン）
- 基準点の計算が正しいか確認
- 変換前後で使用する座標系を明確に区別

### 7.2 リサイズ後に画像が見えなくなる

症状: リサイズ後に画像が表示領域外に出てしまう

解決策:
- パンオフセットの制約処理を実装
- 表示中心を基準点として使用
- リサイズ後に画像位置を強制的に中央に戻す選択肢を提供

### 7.3 ズームとパンの相互作用の問題

症状: ズーム操作後のパン動作が不自然

解決策:
- ズーム操作時にパンオフセットを正しく調整
- マウス位置を中心としたズーム処理の実装
- 制約処理をズーム・パン両方の後に適用

## 8. まとめ

パンオフセットの計算は、画像表示システムで最も理解しにくい部分の一つですが、以下の点を押さえることで正確に実装できます：

1. **座標系を明確に区別する**：画像座標系と表示座標系の違いを常に意識する
2. **変換順序を守る**：スケーリング→パンの順序を維持する
3. **基準点を使用する**：リサイズ時には基準点（多くの場合は表示中心）を維持する
4. **制約処理を適用する**：画像が表示範囲から大きく外れないよう制限を設ける

これらの原則に従うことで、ウィンドウリサイズ時にも一貫性のある自然な画像表示が実現できます。