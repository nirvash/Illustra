using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Illustra.Helpers;

namespace Illustra.Controls
{
    /// <summary>
    /// RatingStarControl.xaml の相互作用ロジック
    /// </summary>
    public partial class RatingStarControl : UserControl
    {
        public RatingStarControl()
        {
            InitializeComponent();
            // デフォルトのテキスト効果を設定
            TextEffect = new DropShadowEffect
            {
                Color = Colors.Black,
                Direction = 320,
                ShadowDepth = 1,
                Opacity = 0.8,
                BlurRadius = 2
            };
        }

        #region DependencyProperties

        // 評価値（1-5）
        public static readonly DependencyProperty RatingValueProperty =
            DependencyProperty.Register("RatingValue", typeof(int), typeof(RatingStarControl),
                new PropertyMetadata(0, OnVisualPropertyChanged));

        public int RatingValue
        {
            get { return (int)GetValue(RatingValueProperty); }
            set { SetValue(RatingValueProperty, value); }
        }

        // 現在のレーティング値
        public static readonly DependencyProperty CurrentRatingProperty =
            DependencyProperty.Register("CurrentRating", typeof(int), typeof(RatingStarControl),
                new PropertyMetadata(0, OnVisualPropertyChanged));

        public int CurrentRating
        {
            get { return (int)GetValue(CurrentRatingProperty); }
            set { SetValue(CurrentRatingProperty, value); }
        }

        // 表示モード
        public enum RatingDisplayMode
        {
            Single,     // 単一の星として動作（CurrentRatingは無視）
            Multiple    // 複数の星の一部として動作（CurrentRatingと比較して表示状態を決定）
        }

        public static readonly DependencyProperty DisplayModeProperty =
            DependencyProperty.Register("DisplayMode", typeof(RatingDisplayMode), typeof(RatingStarControl),
                new PropertyMetadata(RatingDisplayMode.Multiple, OnVisualPropertyChanged));

        public RatingDisplayMode DisplayMode
        {
            get { return (RatingDisplayMode)GetValue(DisplayModeProperty); }
            set { SetValue(DisplayModeProperty, value); }
        }

        // 星の塗りつぶし色
        public static readonly DependencyProperty StarFillProperty =
            DependencyProperty.Register("StarFill", typeof(Brush), typeof(RatingStarControl),
                new PropertyMetadata(Brushes.Gold));

        public Brush StarFill
        {
            get { return (Brush)GetValue(StarFillProperty); }
            set { SetValue(StarFillProperty, value); }
        }

        // 星の輪郭線の色
        public static readonly DependencyProperty StrokeColorProperty =
            DependencyProperty.Register("StrokeColor", typeof(Brush), typeof(RatingStarControl),
                new PropertyMetadata(Brushes.DarkGoldenrod));

        public Brush StrokeColor
        {
            get { return (Brush)GetValue(StrokeColorProperty); }
            set { SetValue(StrokeColorProperty, value); }
        }

        // 数字テキストの色
        public static readonly DependencyProperty TextColorProperty =
            DependencyProperty.Register("TextColor", typeof(Brush), typeof(RatingStarControl),
                new PropertyMetadata(RatingHelper.GetTextColor(0)));

        public Brush TextColor
        {
            get { return (Brush)GetValue(TextColorProperty); }
            set { SetValue(TextColorProperty, value); }
        }

        // テキストの効果
        public static readonly DependencyProperty TextEffectProperty =
            DependencyProperty.Register("TextEffect", typeof(Effect), typeof(RatingStarControl),
                new PropertyMetadata(null, OnVisualPropertyChanged));

        public Effect TextEffect
        {
            get { return (Effect)GetValue(TextEffectProperty); }
            set { SetValue(TextEffectProperty, value); }
        }

        // アニメーションを有効にするかどうか（選択中のアイテムのみTrue）
        public static readonly DependencyProperty EnableAnimationProperty =
            DependencyProperty.Register("EnableAnimation", typeof(bool), typeof(RatingStarControl),
                new PropertyMetadata(false));

        public bool EnableAnimation
        {
            get { return (bool)GetValue(EnableAnimationProperty); }
            set { SetValue(EnableAnimationProperty, value); }
        }

        #endregion

        private bool _updatingVisuals;

        private static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is RatingStarControl control)
            {
                // 表示状態の更新が必要なプロパティが変更された場合のみ更新
                if (e.Property == RatingValueProperty ||
                    e.Property == CurrentRatingProperty ||
                    e.Property == DisplayModeProperty ||
                    e.Property == TextEffectProperty)
                {
                    control.UpdateVisualState();
                }
            }
        }

        private bool ShouldBeFilled()
        {
            if (RatingValue <= 0) return false;

            return DisplayMode == RatingDisplayMode.Single
                ? RatingValue > 0
                : CurrentRating >= RatingValue;
        }

        private void UpdateVisualState()
        {
            if (_updatingVisuals) return;
            _updatingVisuals = true;

            try
            {
                // StarPathがnullの場合は処理をスキップ
                if (StarPath == null) return;

                // 塗りつぶし状態の判定
                bool shouldFill = ShouldBeFilled();

                // DisplayModeに応じた色の決定
                Brush fillBrush;
                Brush textBrush;

                if (DisplayMode == RatingDisplayMode.Single)
                {
                    // 単独モードの場合
                    shouldFill = RatingValue > 0;
                    fillBrush = shouldFill ? RatingHelper.GetRatingColor(RatingValue) : Brushes.Transparent;
                    textBrush = shouldFill ? RatingHelper.GetTextColor(RatingValue) : RatingHelper.GetTextColor(0);
                }
                else
                {
                    // 複数モードの場合
                    shouldFill = CurrentRating >= RatingValue && RatingValue > 0;
                    fillBrush = shouldFill ? RatingHelper.GetRatingColor(RatingValue) : Brushes.Transparent;
                    textBrush = shouldFill ? RatingHelper.GetTextColor(RatingValue) : RatingHelper.GetTextColor(0);
                }

                // 直接Pathに適用
                StarPath.Fill = fillBrush;

                // RatingTextがnullの場合は処理をスキップ
                if (RatingText == null) return;

                // テキストの表示状態を設定
                RatingText.Visibility = RatingValue > 0 ? Visibility.Visible : Visibility.Collapsed;

                // テキストの色を直接設定
                RatingText.Foreground = textBrush;
                RatingText.Effect = TextEffect;
            }
            finally
            {
                _updatingVisuals = false;
            }
        }

        /// <summary>
        /// 星マークのアニメーションを明示的に再生します。
        /// </summary>
        public void PlayAnimation()
        {
            if (StarScale == null) return;

            // X方向のアニメーション
            var xAnimation = new DoubleAnimation
            {
                To = 1.2,
                Duration = TimeSpan.FromMilliseconds(100),
                AutoReverse = true
            };

            // Y方向のアニメーション
            var yAnimation = new DoubleAnimation
            {
                To = 1.2,
                Duration = TimeSpan.FromMilliseconds(100),
                AutoReverse = true
            };

            // アニメーションを開始
            StarScale.BeginAnimation(ScaleTransform.ScaleXProperty, xAnimation);
            StarScale.BeginAnimation(ScaleTransform.ScaleYProperty, yAnimation);
        }
    }
}
