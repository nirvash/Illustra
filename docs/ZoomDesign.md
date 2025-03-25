## 実装計画

基本的には提供されたアルゴリズムをC#に移植し、既存のZoomControlの構造に合わせて実装します。

### 1. 状態変数の追加（ZoomLogicクラス）

```csharp
public class ZoomLogic
{
    // 既存のフィールド
    public Viewport Viewport;
    public double ControlWidth;
    public double ControlHeight;
    public double MinZoom = 0.1;
    public double MaxZoom = 10.0;

    // 新規追加
    private double _initialFitScale = 1.0;  // フィット時のスケール
    private double _zoomFactor = 1.0;       // ユーザー操作によるズーム倍率
    public double Scale { get; private set; } // 実際の表示スケール（_initialFitScale * _zoomFactor）
    public double PanX { get; private set; }  // X方向のパンオフセット
    public double PanY { get; private set; }  // Y方向のパンオフセット
}
```

### 2. 更新が必要なメソッド

1. 初期化時のフィットスケール計算：
```csharp
private void CalculateInitialFitScale()
{
    var imageRegion = Viewport.GetVisibleImageRegion();
    double scaleX = ControlWidth / imageRegion.Width;
    double scaleY = ControlHeight / imageRegion.Height;
    _initialFitScale = Math.Min(scaleX, scaleY);
    Scale = _initialFitScale * _zoomFactor;
}
```

2. ズーム処理の改善：
```csharp
public void HandleZoom(Point mousePos, double zoomDelta)
{
    // マウス位置の画像座標系での位置を計算
    double imageX = (mousePos.X - PanX) / Scale;
    double imageY = (mousePos.Y - PanY) / Scale;

    // ズーム倍率の更新（制限適用）
    _zoomFactor = Math.Max(MinZoom, Math.Min(MaxZoom, _zoomFactor + zoomDelta));

    // スケールの再計算
    Scale = _initialFitScale * _zoomFactor;

    // パンオフセットの更新（マウス位置を中心に）
    PanX = mousePos.X - imageX * Scale;
    PanY = mousePos.Y - imageY * Scale;
}
```

3. ウィンドウリサイズ処理の改善：
```csharp
public void HandleResize(double newWidth, double newHeight)
{
    // リサイズ前の中心点を画像座標系で記録
    double centerImageX = (ControlWidth / 2 - PanX) / Scale;
    double centerImageY = (ControlHeight / 2 - PanY) / Scale;

    // サイズの更新
    ControlWidth = newWidth;
    ControlHeight = newHeight;

    // フィットスケールの再計算
    CalculateInitialFitScale();

    // パンオフセットの再計算（中心点維持）
    PanX = ControlWidth / 2 - centerImageX * Scale;
    PanY = ControlHeight / 2 - centerImageY * Scale;
}
```

### 3. パン操作の制限

```csharp
private void ClampPanOffset()
{
    var imageRegion = Viewport.GetVisibleImageRegion();
    double scaledWidth = imageRegion.Width * Scale;
    double scaledHeight = imageRegion.Height * Scale;

    // 画像が表示領域より小さい場合は中央に
    if (scaledWidth < ControlWidth)
    {
        PanX = (ControlWidth - scaledWidth) / 2;
    }
    else
    {
        // 左右の制限
        PanX = Math.Min(PanX, ControlWidth / 2);
        PanX = Math.Max(PanX, ControlWidth - scaledWidth - ControlWidth / 2);
    }

    // 上下も同様
    if (scaledHeight < ControlHeight)
    {
        PanY = (ControlHeight - scaledHeight) / 2;
    }
    else
    {
        PanY = Math.Min(PanY, ControlHeight / 2);
        PanY = Math.Max(PanY, ControlHeight - scaledHeight - ControlHeight / 2);
    }
}
```

### 4. 性能最適化

1. リサイズイベントの最適化
```csharp
private DispatcherTimer _resizeTimer;

private void InitializeResizeHandler()
{
    _resizeTimer = new DispatcherTimer();
    _resizeTimer.Interval = TimeSpan.FromMilliseconds(100);
    _resizeTimer.Tick += (s, e) =>
    {
        _resizeTimer.Stop();
        HandleResize(ActualWidth, ActualHeight);
    };
}

private void ZoomControl_SizeChanged(object sender, SizeChangedEventArgs e)
{
    _resizeTimer.Stop();
    _resizeTimer.Start();
}
```

2. Transform更新の最適化
```csharp
private double _lastScale;
private double _lastPanX;
private double _lastPanY;

private void UpdateTransform()
{
    if (_lastScale != Scale || _lastPanX != PanX || _lastPanY != PanY)
    {
        ApplyTransformToView();
        _lastScale = Scale;
        _lastPanX = PanX;
        _lastPanY = PanY;
    }
}
```

### 5. 実装手順

1. ZoomLogicクラスの更新
   - 状態変数の追加
   - 新しいメソッドの実装

2. ZoomControlクラスの更新
   - イベントハンドラの修正
   - リサイズ最適化の実装

3. 既存コードの統合
   - Viewportロジックの維持
   - 既存のイベントハンドリングの活用

4. テスト
   - 初期表示の確認
   - ズーム操作の動作確認
   - リサイズ時の挙動確認
   - パン制限の確認
