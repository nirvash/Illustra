using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.IO;
using System.Windows.Media;
using Illustra.Models;
using Illustra.Events;
using Illustra.Helpers;
using Illustra.Controls;
using System.Windows.Input;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using System.Text;
using System.Reflection;
using Prism.Events;

namespace Illustra.Views
{
    public partial class PropertyPanelControl : UserControl, INotifyPropertyChanged
    {
        private IEventAggregator _eventAggregator;
        private IllustraAppContext _appContext;
        private DatabaseManager _db;
        private SelectedFileModel? _selectedFile;
        private List<string> _currentTagFilters = new List<string>();
        private string CONTROL_ID = "PropertyPanel";
        private readonly string[] EXIF_SUPPORTED_FORMATS = new[] { ".jpg", ".jpeg", ".webp" };

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public bool CanEditExif
        {
            get
            {
                if (string.IsNullOrEmpty(_appContext.CurrentProperties?.FilePath)) return false;
                string ext = Path.GetExtension(_appContext.CurrentProperties.FilePath).ToLower();
                return EXIF_SUPPORTED_FORMATS.Contains(ext);
            }
        }

        // DependencyPropertyを再導入し、アプリコンテキストと同期させる
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
                // 古いImagePropertiesのイベントハンドラを解除
                if (e.OldValue is ImagePropertiesModel oldModel)
                {
                    oldModel.PropertyChanged -= control.OnImagePropertiesPropertyChanged;
                }

                // 新しいImagePropertiesのイベントハンドラを設定
                if (e.NewValue is ImagePropertiesModel newModel)
                {
                    newModel.PropertyChanged += control.OnImagePropertiesPropertyChanged;
                }

                control.OnPropertyChanged(nameof(CanEditExif));
            }
        }

        // アプリコンテキストのプロパティ変更を監視し、DependencyPropertyとシンクロさせる
        private void OnAppContextPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(IllustraAppContext.CurrentProperties))
            {
                // CurrentPropertiesが変更されたらDependencyPropertyも更新
                ImageProperties = _appContext.CurrentProperties;

                // DataContextも更新（これが重要）
                DataContext = ImageProperties;
            }
        }

        public PropertyPanelControl()
        {
            // InitializeComponentはXAMLから自動生成されるメソッドで、
            // リンターエラーが表示されることがありますが、ビルド時には問題ありません
            InitializeComponent();

            // 依存関係の解決
            _db = ContainerLocator.Container.Resolve<DatabaseManager>();
            _eventAggregator = ContainerLocator.Container.Resolve<IEventAggregator>();
            _appContext = ContainerLocator.Container.Resolve<IllustraAppContext>();

            // AppContextからプロパティを初期化（DependencyPropertyにセット）
            ImageProperties = _appContext.CurrentProperties;

            // DataContextは自分自身ではなくImagePropertiesに設定（XAML側でプロパティを直接バインドできるように）
            DataContext = ImageProperties;

            // AppContextのCurrentPropertiesの変更を監視
            _appContext.PropertyChanged += OnAppContextPropertyChanged;

            Loaded += PropertyPanelControl_Loaded;
            Unloaded += PropertyPanelControl_Unloaded;
            PreviewMouseDoubleClick += PropertyPanelControl_PreviewMouseDoubleClick;
            IsVisibleChanged += PropertyPanelControl_IsVisibleChanged;
        }

        private void OnImagePropertiesPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ImagePropertiesModel.FilePath))
            {
                OnPropertyChanged(nameof(CanEditExif));
            }
        }

        private void PropertyPanelControl_Loaded(object sender, RoutedEventArgs e)
        {
            _eventAggregator = ContainerLocator.Container.Resolve<IEventAggregator>();
            _eventAggregator.GetEvent<FileSelectedEvent>().Subscribe(OnFileSelected, ThreadOption.UIThread);
            _eventAggregator.GetEvent<FolderSelectedEvent>().Subscribe(OnFolderChanged);
            _eventAggregator.GetEvent<RatingChangedEvent>().Subscribe(OnRatingChanged);
            _eventAggregator.GetEvent<FilterChangedEvent>().Subscribe(OnFilterChanged, ThreadOption.UIThread, false,
                filter => filter.SourceId != CONTROL_ID); // 自分が発信したイベントは無視);
        }

        private void PropertyPanelControl_Unloaded(object sender, RoutedEventArgs e)
        {
            if (_eventAggregator != null)
            {
                _eventAggregator.GetEvent<FileSelectedEvent>().Unsubscribe(OnFileSelected);
                _eventAggregator.GetEvent<FolderSelectedEvent>().Unsubscribe(OnFolderChanged);
                _eventAggregator.GetEvent<RatingChangedEvent>().Unsubscribe(OnRatingChanged);
                _eventAggregator.GetEvent<FilterChangedEvent>().Unsubscribe(OnFilterChanged);
            }
        }

        private void PropertyPanelControl_PreviewMouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // プロパティパネル上でのダブルクリックイベントを処理済みとしてマークし、
            // 親コントロールへの伝播を停止
            e.Handled = true;
        }


        public async void OnFileSelected(SelectedFileModel selectedFile)
        {
            if (selectedFile == null
                || string.IsNullOrEmpty(selectedFile.FullPath)
                || !File.Exists(selectedFile.FullPath))
                return;

            // 選択されたファイルが現在の共有コンテキストと異なる場合、または選択がnullの場合
            // _selectedFileは履歴として保持し、実際の判定はアプリコンテキストと比較する
            if (string.IsNullOrEmpty(_appContext.CurrentProperties?.FilePath) ||
                !_appContext.CurrentProperties.FilePath.Equals(selectedFile.FullPath))
            {
                _selectedFile = selectedFile;

                // 共有コンテキストを更新するサービスが行うため、自分で読み込む必要はない
                // このコントロールではレーティングの同期だけ行う

                void syncRating()
                {
                    if (_appContext.CurrentProperties != null)
                    {
                        _appContext.CurrentProperties.Rating = _selectedFile.Rating;
                        UpdateRatingStars();
                    }
                }

                // 選択ファイルのレーティング変更を監視
                _selectedFile.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(SelectedFileModel.Rating))
                    {
                        syncRating();
                    }
                };

                syncRating();
                Visibility = Visibility.Visible;
            }
            else if (selectedFile == null || string.IsNullOrEmpty(selectedFile.FullPath))
            {
                // ファイルが選択されていない場合はプロパティパネルを非表示にする
                _selectedFile.FullPath = string.Empty;
                Visibility = Visibility.Collapsed;
            }
        }

        // FindVisualChild を利用するため非表示状態では描画更新されない
        private void UpdateRatingStars()
        {
            // PropertiesGridはXAMLで定義されたコンポーネントで、
            // リンターエラーが表示されることがありますが、ビルド時には問題ありません
            if (_selectedFile != null && PropertiesGrid != null)
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
                            starControl.IsFilled = rating <= _selectedFile.Rating;

                            // レーティング値に応じた色を設定（空白時は透明）
                            if (rating <= _selectedFile.Rating)
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
            if (_selectedFile == null || !(sender is Button button) ||
                !int.TryParse(button.Tag?.ToString(), out int rating))
                return;

            if (_selectedFile.Rating == rating)
            {
                // 同じ星をクリックした場合はレーティングをクリア
                _selectedFile.Rating = 0;
            }
            else
            {
                _selectedFile.Rating = rating;
            }

            // レーティング変更イベントを発行
            // レーティングの永続化とコンテキスト更新はサービス側で行う
            _eventAggregator?.GetEvent<RatingChangedEvent>()?.Publish(
                new RatingChangedEventArgs { FilePath = _selectedFile.FullPath, Rating = _selectedFile.Rating });

            // UIを更新（コンテキスト変更の通知後に自動更新される場合は不要）
            UpdateRatingStars();

            // 非同期操作を待機
            await Task.CompletedTask;
        }

        // 外部でレーティングが変更された場合の処理
        private async void OnRatingChanged(RatingChangedEventArgs args)
        {
            if (args.FilePath == _selectedFile?.FullPath && _selectedFile != null)
            {
                // 選択ファイルのレーティングを更新
                _selectedFile.Rating = args.Rating;

                // コンテキストの更新はサービス側で行われるため不要

                // UI表示を更新
                UpdateRatingStars();
            }
        }

        private void CopyOriginalText_Click(object sender, RoutedEventArgs e)
        {
            if (_appContext.CurrentProperties?.StableDiffusionResult != null)
            {
                try
                {
                    Clipboard.SetText(_appContext.CurrentProperties.UserComment);
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

        private async Task SaveUserComment(string filePath, string comment)
        {

            try
            {
                using var image = await SixLabors.ImageSharp.Image.LoadAsync(filePath);

                // ExifProfile が存在しない場合は新規作成
                if (image.Metadata.ExifProfile == null)
                {
                    image.Metadata.ExifProfile = new SixLabors.ImageSharp.Metadata.Profiles.Exif.ExifProfile();
                }

                //  UTF-16BEエンコーディングでユーザーコメントを設定
                ExifProfileExtensions.SetUtf16BEUserComment(image.Metadata.ExifProfile, comment);
                //ExifProfileExtensions.SetUserComment(image.Metadata.ExifProfile, comment, EncodedString.CharacterCode.Unicode);

                // ファイル拡張子に基づいて適切なエンコーダーを取得
                var extension = Path.GetExtension(filePath).ToLowerInvariant();

                // 同じファイルに上書き保存
                using var fileStream = File.Create(filePath);
                await image.SaveAsync(fileStream, GetEncoder(extension));

                // 保存成功メッセージを表示
                MessageBox.Show("プロンプトが保存されました。", "保存完了",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"プロンプトの保存中にエラーが発生しました：{ex.Message}", "エラー",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                throw;
            }
        }

        // ファイル拡張子に基づいて適切なエンコーダーを返すヘルパーメソッド
        private SixLabors.ImageSharp.Formats.IImageEncoder GetEncoder(string extension)
        {
            switch (extension)
            {
                case ".jpg":
                case ".jpeg":
                    return new SixLabors.ImageSharp.Formats.Jpeg.JpegEncoder();
                case ".png":
                    return new SixLabors.ImageSharp.Formats.Png.PngEncoder();
                case ".webp":
                    return new SixLabors.ImageSharp.Formats.Webp.WebpEncoder();
                default:
                    return new SixLabors.ImageSharp.Formats.Jpeg.JpegEncoder(); // デフォルトはJPEG
            }
        }

        private async void AddPrompt_Click(object sender, RoutedEventArgs e)
        {
            if (_appContext.CurrentProperties != null && CanEditExif)
            {
                var dialog = new EditPromptDialog(string.Empty)
                {
                    Owner = Window.GetWindow(this)
                };

                if (dialog.ShowDialog() == true && dialog.IsSaved)
                {
                    try
                    {
                        await SaveUserComment(_appContext.CurrentProperties.FilePath, dialog.PromptText);

                        // プロパティ更新サービスに再読み込みをリクエスト
                        // filePath を送信してプロパティの再読み込みを要求
                        _eventAggregator?.GetEvent<PubSubEvent<string>>()?.Publish(
                            _appContext.CurrentProperties.FilePath);
                    }
                    catch
                    {
                        // エラーは SaveUserComment 内で処理済み
                    }
                }
            }
        }

        private async void EditOriginalPrompt_Click(object sender, RoutedEventArgs e)
        {
            if (_appContext.CurrentProperties != null && CanEditExif)
            {
                var dialog = new EditPromptDialog(_appContext.CurrentProperties.UserComment)
                {
                    Owner = Window.GetWindow(this)
                };

                if (dialog.ShowDialog() == true && dialog.IsSaved)
                {
                    try
                    {
                        await SaveUserComment(_appContext.CurrentProperties.FilePath, dialog.PromptText);

                        // プロパティ更新サービスに再読み込みをリクエスト
                        // filePath を送信してプロパティの再読み込みを要求
                        _eventAggregator?.GetEvent<PubSubEvent<string>>()?.Publish(
                            _appContext.CurrentProperties.FilePath);
                    }
                    catch
                    {
                        // エラーは SaveUserComment 内で処理済み
                    }
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

        private async void PropertyPanelControl_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue is bool isVisible && isVisible)
            {
                // 表示完了するまで待つ
                await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Render);
                UpdateRatingStars();
            }
        }

        private void OnFolderChanged(FolderSelectedEventArgs args)
        {
            // フォルダが変更された場合、タグフィルタをクリア
            _currentTagFilters = new List<string>();
            _selectedFile = null;

            // コンテキストの更新はサービスが行う

            UpdateAllTagsHighlight();
            Visibility = Visibility.Collapsed;
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
            if (TagsItemsControl == null && LoraTagsItemsControl == null) return;

            // ItemsControlの子要素を再帰的に検索
            var textBoxes = new List<TextBox>();

            // 通常のタグを検索
            if (TagsItemsControl != null)
            {
                FindVisualChildren<TextBox>(TagsItemsControl, textBoxes);
            }

            // Loraタグを検索
            if (LoraTagsItemsControl != null)
            {
                FindVisualChildren<TextBox>(LoraTagsItemsControl, textBoxes);
            }

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

        private void CopyTag_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem &&
                menuItem.Parent is ContextMenu contextMenu &&
                contextMenu.PlacementTarget is TextBox textBox)
            {
                try
                {
                    Clipboard.SetText(textBox.Text);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"クリップボードへのコピーに失敗しました：{ex.Message}", "エラー",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }
}
