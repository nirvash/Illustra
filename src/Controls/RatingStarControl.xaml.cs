using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

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

        // 星を塗りつぶすかどうか
        public static readonly DependencyProperty IsFilledProperty =
            DependencyProperty.Register("IsFilled", typeof(bool), typeof(RatingStarControl),
                new PropertyMetadata(true, OnVisualPropertyChanged));

        public bool IsFilled
        {
            get { return (bool)GetValue(IsFilledProperty); }
            set { SetValue(IsFilledProperty, value); }
        }

        // 星の塗りつぶし色
        public static readonly DependencyProperty StarFillProperty =
            DependencyProperty.Register("StarFill", typeof(Brush), typeof(RatingStarControl),
                new PropertyMetadata(Brushes.Gold, OnVisualPropertyChanged));

        public Brush StarFill
        {
            get { return (Brush)GetValue(StarFillProperty); }
            set { SetValue(StarFillProperty, value); }
        }

        // 星の輪郭線の色
        public static readonly DependencyProperty StrokeColorProperty =
            DependencyProperty.Register("StrokeColor", typeof(Brush), typeof(RatingStarControl),
                new PropertyMetadata(Brushes.DarkGoldenrod, OnVisualPropertyChanged));

        public Brush StrokeColor
        {
            get { return (Brush)GetValue(StrokeColorProperty); }
            set { SetValue(StrokeColorProperty, value); }
        }

        // 数字テキストの色
        public static readonly DependencyProperty TextColorProperty =
            DependencyProperty.Register("TextColor", typeof(Brush), typeof(RatingStarControl),
                new PropertyMetadata(Brushes.White, OnVisualPropertyChanged));

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

        #endregion

        private static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is RatingStarControl control)
            {
                control.UpdateVisualState();
            }
        }

        private void UpdateVisualState()
        {
            // StarPathがnullの場合は処理をスキップ
            if (StarPath == null) return;

            // 塗りつぶし状態に応じて色を設定
            StarPath.Fill = IsFilled ? StarFill : Brushes.Transparent;

            // RatingTextがnullの場合は処理をスキップ
            if (RatingText == null) return;

            // テキストの表示状態を設定
            RatingText.Visibility = RatingValue > 0 && IsFilled ? Visibility.Visible : Visibility.Collapsed;

            // テキストの色を設定
            RatingText.Foreground = TextColor;
            RatingText.Effect = TextEffect;
        }
    }
}
