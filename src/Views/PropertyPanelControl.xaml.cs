using System;
using System.Windows;
using System.Windows.Controls;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
using Prism.Events;
using Prism.Ioc;
using Illustra.Models;
using Illustra.Events;
using Illustra.Helpers;
using System.Diagnostics;
using Illustra.Controls;
using System.Windows.Input;
using System.Collections.Generic;

namespace Illustra.Views
{
    public partial class PropertyPanelControl : UserControl
    {
        private IEventAggregator? _eventAggregator;
        private string _currentFilePath = string.Empty;
        private FileNodeModel? _currentFileNode;
        private readonly DatabaseManager _db = new();
        private List<string> _currentTagFilters = new List<string>();
        private string CONTROL_ID = "PropertyPanel";

        public static readonly DependencyProperty ImagePropertiesProperty =
            DependencyProperty.Register(
                nameof(ImageProperties),
                typeof(ImagePropertiesModel),
                typeof(PropertyPanelControl),
                new PropertyMetadata(null, OnImagePropertiesChanged));

        public ImagePropertiesModel? ImageProperties
        {
            get => (ImagePropertiesModel?)GetValue(ImagePropertiesProperty);
            set => SetValue(ImagePropertiesProperty, value);
        }

        private static void OnImagePropertiesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is PropertyPanelControl control)
            {
                control.DataContext = e.NewValue;
                control.UpdatePropertiesDisplay();
            }
        }

        public PropertyPanelControl()
        {
            // InitializeComponentはXAMLから自動生成されるメソッドで、
            // リンターエラーが表示されることがありますが、ビルド時には問題ありません
            InitializeComponent();
            ImageProperties = new ImagePropertiesModel();
            DataContext = ImageProperties;

            Loaded += PropertyPanelControl_Loaded;
            PreviewMouseDoubleClick += PropertyPanelControl_PreviewMouseDoubleClick;
        }

        private void PropertyPanelControl_PreviewMouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // プロパティパネル上でのダブルクリックイベントを処理済みとしてマークし、
            // 親コントロールへの伝播を停止
            e.Handled = true;
        }

        private void PropertyPanelControl_Loaded(object sender, RoutedEventArgs e)
        {
            _eventAggregator = ContainerLocator.Container.Resolve<IEventAggregator>();
            _eventAggregator.GetEvent<FileSelectedEvent>().Subscribe(OnFileSelected);
            _eventAggregator.GetEvent<FolderSelectedEvent>().Subscribe(OnFolderChanged);
            _eventAggregator.GetEvent<RatingChangedEvent>().Subscribe(OnRatingChanged);
            _eventAggregator.GetEvent<FilterChangedEvent>().Subscribe(OnFilterChanged, ThreadOption.UIThread, false,
                filter => filter.SourceId != CONTROL_ID); // 自分が発信したイベントは無視);

        }

        private void OnFileSelected(string filePath)
        {
            if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath) && _currentFilePath != filePath)
            {
                _currentFilePath = filePath;
                _ = LoadFilePropertiesAsync(filePath); // ToDo: プロパティが表示されていない
                _ = LoadFileNodeAsync(filePath);
                Visibility = Visibility.Visible;
            }
            else if (string.IsNullOrEmpty(filePath))
            {
                _currentFilePath = string.Empty;
                ImageProperties = new ImagePropertiesModel();
                DataContext = ImageProperties;
                Visibility = Visibility.Collapsed;
            }
        }

        private async Task LoadFilePropertiesAsync(string filePath)
        {
            try
            {
                var fileInfo = new FileInfo(filePath);
                ImageProperties = await ImagePropertiesModel.LoadFromFileAsync(filePath);
            }
            catch (Exception)
            {
                // Ignore
            }
        }

        private async Task LoadFileNodeAsync(string filePath)
        {
            try
            {
                _currentFileNode = await _db.GetFileNodeAsync(filePath);
                if (_currentFileNode != null)
                {
                    // FileNodeのRatingをImagePropertiesに反映
                    if (ImageProperties != null)
                    {
                        ImageProperties.Rating = _currentFileNode.Rating;
                    }
                    await UpdateRatingStars();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"ファイルノードの読み込み中にエラーが発生しました：{ex.Message}", "エラー",
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task UpdateRatingStars(bool updateDb = false)
        {
            // PropertiesGridはXAMLで定義されたコンポーネントで、
            // リンターエラーが表示されることがありますが、ビルド時には問題ありません
            if (_currentFileNode != null && PropertiesGrid != null)
            {
                var buttonList = FindButtonsInVisualTree(PropertiesGrid);

                foreach (var button in buttonList)
                {
                    if (button.Tag != null && int.TryParse(button.Tag.ToString(), out int rating))
                    {
                        var starControl = FindVisualChild<RatingStarControl>(button);
                        if (starControl != null)
                        {
                            // レーティングに応じて塗りつぶし状態を設定
                            starControl.IsFilled = rating <= _currentFileNode.Rating;

                            // レーティング値に応じた色を設定（空白時は透明）
                            if (rating <= _currentFileNode.Rating)
                            {
                                starControl.StarFill = RatingHelper.GetRatingColor(rating);
                                starControl.TextColor = RatingHelper.GetTextColor(rating);
                            }
                            else
                            {
                                starControl.StarFill = Brushes.Transparent;
                                starControl.TextColor = Brushes.DarkGray;
                            }
                        }
                    }
                }

                if (updateDb)
                {
                    // DBに保存
                    await _db.UpdateRatingAsync(_currentFileNode.FullPath, _currentFileNode.Rating);
                }
            }
        }

        // Visual Tree内のすべてのButtonを探す再帰関数
        private List<Button> FindButtonsInVisualTree(DependencyObject parent)
        {
            var result = new List<Button>();

            // 子要素の数を取得
            int childCount = VisualTreeHelper.GetChildrenCount(parent);

            for (int i = 0; i < childCount; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);

                // 子がButtonならリストに追加
                if (child is Button button)
                {
                    result.Add(button);
                }

                // 再帰的に子の子も検索
                result.AddRange(FindButtonsInVisualTree(child));
            }

            return result;
        }

        // ヘルパーメソッド: Visual Tree内の特定の型の子要素を検索
        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) return null;

            // Check if we're on the UI thread
            if (!parent.Dispatcher.CheckAccess())
            {
                // If not, invoke the method on the UI thread and wait for the result
                return parent.Dispatcher.Invoke(() => FindVisualChild<T>(parent));
            }

            // Now safely on UI thread
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);

                if (child is T t)
                {
                    return t;
                }
                var result = FindVisualChild<T>(child);
                if (result != null)
                {
                    return result;
                }
            }
            return null;
        }

        private async void RatingStar_Click(object sender, RoutedEventArgs e)
        {
            if (_currentFileNode == null || !(sender is Button button) ||
                !int.TryParse(button.Tag?.ToString(), out int rating))
                return;

            if (_currentFileNode.Rating == rating)
            {
                // 同じ星をクリックした場合はレーティングをクリア
                _currentFileNode.Rating = 0;
            }
            else
            {
                _currentFileNode.Rating = rating;
            }

            // ImagePropertiesのRatingも更新
            if (ImageProperties != null)
            {
                ImageProperties.Rating = _currentFileNode.Rating;
            }

            // レーティング変更イベントを発行
            _eventAggregator?.GetEvent<RatingChangedEvent>()?.Publish(
                new RatingChangedEventArgs { FilePath = _currentFilePath, Rating = _currentFileNode.Rating });

            // UIを更新
            await UpdateRatingStars(true);

            // 非同期操作を待機
            await Task.CompletedTask;
        }

        // 外部でレーティングが変更された場合の処理
        private async void OnRatingChanged(RatingChangedEventArgs args)
        {
            if (args.FilePath == _currentFilePath && _currentFileNode != null)
            {
                _currentFileNode.Rating = args.Rating;
                // ImagePropertiesのRatingも更新
                if (ImageProperties != null)
                {
                    ImageProperties.Rating = args.Rating;
                }
                await UpdateRatingStars();
            }
        }

        private void CopyOriginalText_Click(object sender, RoutedEventArgs e)
        {
            if (ImageProperties?.StableDiffusionResult != null)
            {
                try
                {
                    Clipboard.SetText(ImageProperties.UserComment);
                    MessageBox.Show("コメントをクリップボードにコピーしました。", "コピー完了",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"クリップボードへのコピーに失敗しました：{ex.Message}", "エラー",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void UpdatePropertiesDisplay()
        {
            if (ImageProperties != null)
            {
                // ファイルが実際に存在する場合のみレーティングを表示
                if (!string.IsNullOrEmpty(ImageProperties.FilePath) && File.Exists(ImageProperties.FilePath))
                {
                    _ = LoadFileNodeAsync(ImageProperties.FilePath);
                }
            }
        }

        private void OnFilterChanged(FilterChangedEventArgs args)
        {
            // タグフィルタが変更された場合、CurrentTagFilterを更新
            if (args.Type == FilterChangedEventArgs.FilterChangedType.TagFilterChanged)
            {
                if (args.IsTagFilterEnabled)
                {
                    // 複数タグのフィルタリングをサポート
                    _currentTagFilters = new List<string>(args.TagFilters);
                }
                else
                {
                    _currentTagFilters = new List<string>();
                }

                // タグの表示状態を更新
                UpdateAllTagsHighlight();
            }
        }

        private void OnFolderChanged(FolderSelectedEventArgs args)
        {
            // フォルダが変更された場合、タグフィルタをクリア
            _currentTagFilters = new List<string>();
            ImageProperties = new ImagePropertiesModel();
            DataContext = ImageProperties;
            UpdateAllTagsHighlight();
        }

        private void Tag_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            // 右クリックされたタグを記憶
            if (sender is TextBox textBox)
            {
                textBox.Tag = textBox.Text;
            }
        }

        private void AddTagToFilter_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem &&
                menuItem.Parent is ContextMenu contextMenu &&
                contextMenu.PlacementTarget is TextBox textBox)
            {
                string tag = textBox.Text;
                if (!string.IsNullOrEmpty(tag))
                {
                    // タグが既に存在しない場合のみ追加
                    if (!_currentTagFilters.Contains(tag, StringComparer.OrdinalIgnoreCase))
                    {
                        _currentTagFilters.Add(tag);
                        UpdateAllTagsHighlight();

                        // フィルタ変更イベントを発行
                        _eventAggregator?.GetEvent<FilterChangedEvent>().Publish(
                            new FilterChangedEventArgsBuilder(CONTROL_ID)
                                .WithTagFilter(true, _currentTagFilters)
                                .Build());
                    }
                }
            }
        }

        private void RemoveTagFromFilter_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem &&
                menuItem.Parent is ContextMenu contextMenu &&
                contextMenu.PlacementTarget is TextBox textBox)
            {
                string tag = textBox.Text;
                if (!string.IsNullOrEmpty(tag))
                {
                    // タグを削除
                    _currentTagFilters.RemoveAll(t => string.Equals(t, tag, StringComparison.OrdinalIgnoreCase));
                    UpdateAllTagsHighlight();

                    // フィルタ変更イベントを発行
                    _eventAggregator?.GetEvent<FilterChangedEvent>().Publish(
                        new FilterChangedEventArgsBuilder(CONTROL_ID)
                            .WithTagFilter(_currentTagFilters.Count > 0, _currentTagFilters)
                            .Build());
                }
            }
        }

        private void Tag_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                UpdateTagHighlight(textBox);
            }
        }

        private void UpdateTagHighlight(TextBox textBox)
        {
            if (textBox == null) return;

            // TextBoxのTextプロパティを使用する（Tagプロパティではなく）
            string tag = textBox.Text ?? string.Empty;
            bool isMatch = _currentTagFilters.Contains(tag, StringComparer.OrdinalIgnoreCase);

            // TextBoxの親要素（Border）を取得
            if (VisualTreeHelper.GetParent(textBox) is Border border)
            {
                // ハイライト状態を設定
                if (isMatch)
                {
                    border.BorderBrush = new SolidColorBrush(Color.FromRgb(0, 120, 215)); // #0078D7
                    border.BorderThickness = new Thickness(2);
                }
                else
                {
                    border.BorderBrush = new SolidColorBrush(Colors.Transparent);
                    border.BorderThickness = new Thickness(1);
                }
            }
        }

        private void UpdateAllTagsHighlight()
        {
            // TagsItemsControlはXAMLで定義されたコンポーネントで、
            // リンターエラーが表示されることがありますが、ビルド時には問題ありません
            if (TagsItemsControl == null) return;

            // ItemsControlの子要素を再帰的に検索
            var textBoxes = new List<TextBox>();
            FindVisualChildren<TextBox>(TagsItemsControl, textBoxes);

            // 各TextBoxのハイライト状態を更新
            foreach (var textBox in textBoxes)
            {
                UpdateTagHighlight(textBox);
            }
        }

        private void FindVisualChildren<T>(DependencyObject parent, List<T> results) where T : DependencyObject
        {
            int childCount = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childCount; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T t)
                {
                    results.Add(t);
                }
                FindVisualChildren<T>(child, results);
            }
        }
    }
}
