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

        // アニメーションを有効にするかどうか（選択中のアイテムのみTrue）
        public static readonly DependencyProperty EnableAnimationProperty =
            DependencyProperty.Register("EnableAnimation", typeof(bool), typeof(RatingStarControl),
                new PropertyMetadata(false));


        #endregion

        private static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is RatingStarControl control)
            {
                if (e.Property == RatingValueProperty)
                {
                    // RatingValueが変更された場合、StarFillとTextColorを更新
                    int rating = (int)e.NewValue;
                    if (rating > 0)
                    {
                        control.StarFill = RatingHelper.GetRatingColor(rating);
                        control.TextColor = RatingHelper.GetTextColor(rating);
                    }
                    else
                    {
                        control.StarFill = Brushes.Transparent;
                        control.TextColor = RatingHelper.GetTextColor(0);
                    }
                }
                control.UpdateVisualState();
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

        private void UpdateVisualState()
        {
            // StarPathがnullの場合は処理をスキップ
            if (StarPath == null) return;

            // レーティング値に基づいて塗りつぶし状態を設定
            StarPath.Fill = RatingValue > 0 ? StarFill : Brushes.Transparent;

            // RatingTextがnullの場合は処理をスキップ
            if (RatingText == null) return;

            // テキストの表示状態を設定
            RatingText.Visibility = RatingValue > 0 ? Visibility.Visible : Visibility.Collapsed;

            // テキストの色を設定
            RatingText.Foreground = TextColor;
            RatingText.Effect = TextEffect;
        }
    }
}
