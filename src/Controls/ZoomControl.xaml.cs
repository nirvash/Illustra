// ViewportベースのZoomControlの最小構成実装（イベントハンドラ付き）
using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Xceed.Wpf.Toolkit.Core.Converters;

namespace Illustra.Controls
{
    public struct Viewport
    {
        // 表示している論理的な領域（カメラ視点）
        public Rect VisibleArea;

        // 論理空間上の画像の範囲
        public Rect ImageBounds;

        public Viewport(Rect visible, Rect image)
        {
            VisibleArea = visible;
            ImageBounds = image;
        }

    }


    public class ZoomLogic
    {
        public Viewport Viewport;
        public double ControlWidth;
        public double ControlHeight;

        public double MinZoom = 0.5;
        public double MaxZoom = 10.0;
        public double ZoomRatio = 1.0; // ズーム倍率の変化量

        private double _initialFitScale = 1.0;  // フィット状態でのスケール
        private double _initialPanX = 0.0;         // フィット状態でのパンオフセットX
        private double _initialPanY = 0.0;         // フィット状態でのパンオフセットY

        // パン操作の状態
        private Rect _startVisibleArea;

        public double Scale { get; private set; } // 実際の表示スケール
        public double PanX { get; private set; }
        public double PanY { get; private set; }

        // ドラッグ開始時の処理
        public void StartPan()
        {
            _startVisibleArea = Viewport.VisibleArea;
        }

        // ズーム倍率の制御メソッド
        public double GetZoomFactor() => _initialFitScale * Scale; // フィット状態での倍率を基準にする

        public void UpdateZoom(Point mouse, double delta)
        {
            // マウス位置の画像座標系への変換
            var imageX = Viewport.VisibleArea.X + (mouse.X / Scale);
            var imageY = Viewport.VisibleArea.Y + (mouse.Y / Scale);

            // ズーム量の決定（正: ズームイン, 負: ズームアウト）
            var oldScale = Scale; // ズーム前のスケールを保存
            Scale = Math.Max(MinZoom * _initialFitScale, Math.Min(MaxZoom * _initialFitScale, Scale * (1 + delta)));

            // 表示領域の調整（マウス位置を中心としたズーム）
            var scaleRation = Scale / oldScale;
            Viewport.VisibleArea.Width = ControlWidth / Scale;
            Viewport.VisibleArea.Height = ControlHeight / Scale;

            // マウス位置を中心にズーム
            var mouseXRatio = mouse.X / ControlWidth;
            var mouseYRatio = mouse.Y / ControlHeight;

            Viewport.VisibleArea.X = imageX - Viewport.VisibleArea.Width * mouseXRatio;
            Viewport.VisibleArea.Y = imageY - Viewport.VisibleArea.Height * mouseYRatio;

            ConstraintViewport();
            UniformTransform();
        }


        // パン操作の制御メソッド
        // deltaX, deltaY: スクリーン座標系での移動量
        // Scale で除算して論理座標系に変換し、Viewport を更新
        // パン処理：スクリーン座標系での移動量を受け取り、開始位置からの相対位置を計算
        public void UpdatePanOffset(double deltaX, double deltaY)
        {
            // 開始位置からの相対移動でパンオフセットを更新
            Viewport.VisibleArea.X = _startVisibleArea.X - deltaX / Scale;
            Viewport.VisibleArea.Y = _startVisibleArea.Y - deltaY / Scale;

            // パンの制限を適用
            ConstraintViewport();
            UniformTransform(); // パンオフセットを適用
        }

        public ZoomLogic(Viewport viewport, double controlWidth, double controlHeight)
        {
            Viewport = viewport;
            ControlWidth = controlWidth;
            ControlHeight = controlHeight;
            CalculateInitialFitScale(); // 初期フィットスケールを計算
            UniformTransform();
        }

        public void UniformTransform()
        {
            PanX = -Viewport.VisibleArea.X * Scale;
            PanY = -Viewport.VisibleArea.Y * Scale;
        }

        private double CalculateNewFitScale()
        {
            var imageAspect = Viewport.ImageBounds.Width / Viewport.ImageBounds.Height;
            var controlAspect = ControlWidth / ControlHeight;
            if (imageAspect > controlAspect)
            {
                // 画像が横長の場合
                return ControlWidth / Viewport.ImageBounds.Width;
            }
            else
            {
                // 画像が縦長の場合
                return ControlHeight / Viewport.ImageBounds.Height;
            }
        }

        private void CalculateInitialFitScale()
        {
            var imageAspect = Viewport.ImageBounds.Width / Viewport.ImageBounds.Height;
            var controlAspect = ControlWidth / ControlHeight;
            if (imageAspect > controlAspect)
            {
                // 画像が横長の場合
                Scale = ControlWidth / Viewport.ImageBounds.Width;
                Viewport.VisibleArea.Width = Viewport.ImageBounds.Width;
                Viewport.VisibleArea.Height = ControlHeight / Scale;
                Viewport.VisibleArea.X = 0;
                Viewport.VisibleArea.Y = (Viewport.ImageBounds.Height - Viewport.VisibleArea.Height) / 2;
            }
            else
            {
                // 画像が縦長の場合
                Scale = ControlHeight / Viewport.ImageBounds.Height;
                Viewport.VisibleArea.Height = Viewport.ImageBounds.Height;
                Viewport.VisibleArea.Width = ControlWidth / Scale;
                Viewport.VisibleArea.Y = 0;
                Viewport.VisibleArea.X = (Viewport.ImageBounds.Width - Viewport.VisibleArea.Width) / 2;
            }
            _initialFitScale = Scale; // フィット状態でのスケールを保存
        }

        private void CenterImage()
        {
            PanX = (ControlWidth - Viewport.ImageBounds.Width * Scale) / 2;
            PanY = (ControlHeight - Viewport.ImageBounds.Height * Scale) / 2;
            _initialPanX = PanX;
            _initialPanY = PanY;
        }

        public void LogViewportState(string label = "")
        {
            var visible = Viewport.VisibleArea;
            var imageBounds = Viewport.ImageBounds;

            Console.WriteLine($"[Viewport] {label}");
            Console.WriteLine($"  VisibleArea: X={visible.X:0.00}, Y={visible.Y:0.00}, W={visible.Width:0.00}, H={visible.Height:0.00}");
            Console.WriteLine($"  ImageBounds: X={imageBounds.X}, Y={imageBounds.Y}, W={imageBounds.Width}, H={imageBounds.Height}");
            Console.WriteLine($"  Scale: {Scale:0.000}");
            Console.WriteLine($"  PanX: {PanX:0.00}, PanY: {PanY:0.00}");
            Console.WriteLine($"  ControlSize: {ControlWidth:0.00} x {ControlHeight:0.00}");
        }


        internal void Resize(double newContainerWidth, double newContainerHeight)
        {
            // 現在のズーム状態を保存（初期フィットスケールに対する相対的なズーム率）
            var currentScale = Scale;

            var imageAspect = Viewport.ImageBounds.Width / Viewport.ImageBounds.Height;
            var oldConrolAspect = ControlWidth / ControlHeight;

            double oldFitScale = 0.0;
            if (imageAspect > oldConrolAspect)
            {
                oldFitScale = ControlWidth / Viewport.ImageBounds.Width;
            }
            else
            {
                oldFitScale = ControlHeight / Viewport.ImageBounds.Height;
            }

            var zoomRatio = currentScale / oldFitScale; // フィット状態での倍率を基準にする

            // 現在の表示中心点を画像座標系で記録
            var centerX = Viewport.VisibleArea.X + Viewport.VisibleArea.Width / 2;
            var centerY = Viewport.VisibleArea.Y + Viewport.VisibleArea.Height / 2;

            // 中心点の相対位置（0～1の範囲で正規化）
            var relativeCenterX = centerX / Viewport.ImageBounds.Width;
            var relativeCenterY = centerY / Viewport.ImageBounds.Height;

            // ウィンドウサイズを更新
            ControlWidth = newContainerWidth;
            ControlHeight = newContainerHeight;

            CalculateInitialFitScale(); // 新しいフィットスケールを計算

            var newFitScale = Scale;

            // 以前のズーム倍率を再適用
            Scale = newFitScale * zoomRatio;

            // 新しいウィンドウサイズに合わせて表示領域を更新
            Viewport.VisibleArea.Width = ControlWidth / Scale;
            Viewport.VisibleArea.Height = ControlHeight / Scale;

            // 新しいウィンドウサイズに合わせて表示領域の位置を更新
            var newCenterX = relativeCenterX * Viewport.ImageBounds.Width;
            var newCenterY = relativeCenterY * Viewport.ImageBounds.Height;

            // 新しい表示領域の位置を計算
            Viewport.VisibleArea.X = newCenterX - Viewport.VisibleArea.Width / 2;
            Viewport.VisibleArea.Y = newCenterY - Viewport.VisibleArea.Height / 2;

            ConstraintViewport();
            UniformTransform();
        }

        // パンオフセットの制約適用
        private void ConstraintViewport()
        {
            var img = Viewport.ImageBounds;
            var view = Viewport.VisibleArea;

            double leftLimitX = view.X + view.Width * 0.8;   // 左端は80%ラインより右に行かない
            double rightLimitX = view.X + view.Width * 0.2;  // 右端は20%ラインより左に行かない
            double topLimitY = view.Y + view.Height * 0.8;
            double bottomLimitY = view.Y + view.Height * 0.2;

            // X方向のパン制約
            if (img.X > leftLimitX)
            {
                view.X = img.X - view.Width * 0.8;
            }
            else if (img.X + img.Width < rightLimitX)
            {
                view.X = img.X + img.Width - view.Width * 0.2;
            }

            // Y方向のパン制約
            if (img.Y > topLimitY)
            {
                view.Y = img.Y - view.Height * 0.8;
            }
            else if (img.Y + img.Height < bottomLimitY)
            {
                view.Y = img.Y + img.Height - view.Height * 0.2;
            }

            Viewport.VisibleArea = view;
        }
    }

    public partial class ZoomControl : UserControl
    {
        private ZoomLogic _zoomLogic;
        private BitmapSource _source; // 現在の画像（論理サイズに変換必要）

        private bool _userInteracted = false; // 操作があったかを記録

        // パン操作用
        private bool _isDragging;
        private bool _isDragChecking;
        private Point _dragStartMouse;  // スクリーン座標系でのドラッグ開始位置

        public ZoomControl()
        {
            InitializeComponent();
            ImageControl.Visibility = Visibility.Hidden; // 初期状態では非表示
            Loaded += ZoomControl_Loaded;
            SizeChanged += ZoomControl_SizeChanged;
            PreviewMouseWheel += ZoomControl_PreviewMouseWheel;
            PreviewMouseDown += ZoomControl_PreviewMouseDown;
            PreviewMouseMove += ZoomControl_PreviewMouseMove;
            PreviewMouseUp += ZoomControl_PreviewMouseUp;
        }

        private void ZoomControl_Loaded(object sender, RoutedEventArgs e)
        {
            if (_source == null) return;

            // ウィンドウサイズが決まってからビューポートを初期化
            Dispatcher.InvokeAsync(() =>
            {
                if (ActualWidth <= 0 || ActualHeight <= 0)
                    return;

                var viewport = CreateInitialViewport(_source);
                _zoomLogic = new ZoomLogic(viewport, ActualWidth, ActualHeight);
                ApplyTransformToView();
                ImageControl.Visibility = Visibility.Visible; // 画像を表示
                _zoomLogic.LogViewportState("ZoomControl_Loaded:");

            }, DispatcherPriority.Render);
        }

        // 初期表示で設定する Viewport（すべての座標は論理座標系で計算）
        private Viewport CreateInitialViewport(BitmapSource source)
        {
            // Step 1: DPIを考慮して画像のピクセルサイズを論理サイズに変換
            double imageWidth = source.PixelWidth * 96.0 / source.DpiX;   // px * (1/DPI) → 論理単位
            double imageHeight = source.PixelHeight * 96.0 / source.DpiY;
            var imageBounds = new Rect(0, 0, imageWidth, imageHeight);

            // Step 2: 画像の中心点を論理座標で計算
            double centerImageX = imageWidth / 2;
            double centerImageY = imageHeight / 2;

            // Step 3: コントロールに対する画像の初期フィットスケールを計算
            double scaleX = ActualWidth / imageWidth;    // コントロール幅 ÷ 論理画像幅
            double scaleY = ActualHeight / imageHeight;
            double scale = Math.Min(scaleX, scaleY);     // アスペクト比を維持

            // Step 4: フィットスケールから論理座標でのビューポートサイズを計算
            double viewportWidth = ActualWidth / scale;  // コントロールサイズを論理サイズに変換
            double viewportHeight = ActualHeight / scale;

            // Step 5: ビューポートを画像の中心に配置（論理座標）
            double viewportX = centerImageX - viewportWidth / 2;
            double viewportY = centerImageY - viewportHeight / 2;

            return new Viewport(
                new Rect(viewportX, viewportY, viewportWidth, viewportHeight),
                imageBounds
            );
        }

        private void ApplyTransformToView()
        {
            ZoomTransform.ScaleX = _zoomLogic.Scale;
            ZoomTransform.ScaleY = _zoomLogic.Scale;
            PanTransform.X = _zoomLogic.PanX;
            PanTransform.Y = _zoomLogic.PanY;
        }

        private void ZoomControl_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            Console.WriteLine($"[ZoomControl_SizeChanged] Control: {ActualWidth} x {ActualHeight}, UserInteracted={_userInteracted}");
            if (_zoomLogic == null || _source == null)
                return;

            // Layout 完了後のサイズで更新する
            Dispatcher.InvokeAsync(() =>
            {
                if (ActualWidth <= 0 || ActualHeight <= 0)
                    return;
                _zoomLogic.LogViewportState("SizeChange before");
                Console.WriteLine($"[SizeChanged] Control: {ActualWidth} x {ActualHeight}, UserInteracted={_userInteracted}");
                _zoomLogic.Resize(ActualWidth, ActualHeight);

                ApplyTransformToView();
                _zoomLogic.LogViewportState("SizeChage after");
            }, DispatcherPriority.Render);
        }


        public static readonly DependencyProperty SourceProperty =
            DependencyProperty.Register(
                nameof(Source),
                typeof(ImageSource),
                typeof(ZoomControl),
                new PropertyMetadata(null, OnSourceChanged));
        private static void OnSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (ZoomControl)d;

            if (e.NewValue is BitmapSource bmp)
            {
                control._source = bmp;
                control.ImageControl.Source = bmp;
                if (control.IsLoaded)
                {
                    control.ZoomControl_Loaded(null, null); // 強制再初期化
                }
            }
        }

        public ImageSource Source
        {
            get => (ImageSource)GetValue(SourceProperty);
            set => SetValue(SourceProperty, value);
        }

        public void ResetZoom()
        {
            if (_source == null) return;

            var viewport = CreateInitialViewport(_source);
            _zoomLogic = new ZoomLogic(viewport, ActualWidth, ActualHeight);
            ApplyTransformToView();
        }


        private void ZoomControl_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (_source == null || _zoomLogic == null) return;
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                _userInteracted = true; // 操作開始で固定

                // マウス位置を取得（スクリーン座標系）
                Point mouse = e.GetPosition(HitBox);

                // ホイール1回転で約10%のズーム変化
                var direction = -Math.Sign(e.Delta); // ホイールの回転方向に応じてズームイン/アウト
                double delta = direction * -0.15;

                // 新しいスケールの計算
                _zoomLogic.UpdateZoom(mouse, delta);
                ApplyTransformToView();

                e.Handled = true;
            }
        }

        private void ZoomControl_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (_source == null || _zoomLogic == null) return;
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                _userInteracted = true; // パン開始で固定
                _isDragging = false;
                _isDragChecking = true; // ドラッグチェック開始
                _dragStartMouse = e.GetPosition(HitBox);
                Mouse.OverrideCursor = null; // ウィンドウレベルのカーソル強制設定を解除

                HitBox.Cursor = Cursors.Hand;

                // パン開始位置を記録
                _zoomLogic.StartPan();
                /*
                if (e.ClickCount == 2 && !_zoomLogic.IsInitialFitScale())
                {
                    // ダブルクリックでリセット
                    _userInteracted = true; // 操作開始で固定
                    var viewport = CreateInitialViewport(_source);
                    _zoomLogic = new ZoomLogic(viewport, ActualWidth, ActualHeight);
                    ApplyTransformToView();
                    e.Handled = true;
                }
                */
            }
        }

        private void ZoomControl_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_source == null || _zoomLogic == null) return;
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                // 現在のマウス位置（スクリーン座標系）
                var current = e.GetPosition(HitBox);

                // ドラッグ開始位置からの移動量（スクリーン座標系）
                // 右にドラッグ → deltaScreen.X > 0 → 画像は右に移動
                // 左にドラッグ → deltaScreen.X < 0 → 画像は左に移動
                var deltaScreen = current - _dragStartMouse;

                if (!_isDragging && _isDragChecking &&
                    (Math.Abs(deltaScreen.X) > SystemParameters.MinimumHorizontalDragDistance ||
                     Math.Abs(deltaScreen.Y) > SystemParameters.MinimumVerticalDragDistance))
                {
                    // ドラッグ開始
                    _isDragging = true;
                    _isDragChecking = false; // ドラッグチェック終了
                    HitBox.CaptureMouse();
                    HitBox.Cursor = Cursors.Hand;
                }

                if (_isDragging)
                {
                    // ドラッグ中の処理
                    // 移動量をUpdatePanOffsetに渡す（スクリーン座標系のまま）
                    _zoomLogic.UpdatePanOffset(deltaScreen.X, deltaScreen.Y);

                    ApplyTransformToView();
                    _zoomLogic.LogViewportState("During Drags");
                    HitBox.Cursor = Cursors.Hand;

                    e.Handled = true;
                }
            }
        }

        private void ZoomControl_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDragging)
            {
                _isDragging = false;
                this.ReleaseMouseCapture();
                Mouse.OverrideCursor = null;
                HitBox.ReleaseMouseCapture();
            }
            HitBox.Cursor = Cursors.Arrow;
            _isDragChecking = false; // ドラッグチェック終了
        }
    }
}
