using Illustra.Shared.Models.Tools; // Added for McpOpenFolderEventArgs
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Illustra.Helpers;
using System.IO;
using Illustra.Events;
using Illustra.Models;
using System.Collections.Specialized; // INotifyCollectionChanged を使うために追加
using System.Runtime.CompilerServices; // CallerMemberName を使うために追加
using Illustra.ViewModels;
using System.Diagnostics;
using System.Windows.Threading;
using WpfToolkit.Controls;
using Illustra.Controls;
using System.ComponentModel;
using GongSolutions.Wpf.DragDrop;
using System.Collections;
using Illustra.Functions;
using System.Windows.Documents;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Threading.Tasks;
using System.Threading;
using Illustra.Extensions;
using System.Windows.Controls.Primitives;
using System.Windows.Data; // 追加: FileNodeModelExtensionsを使用するため
using System.Text;
using System.Runtime.InteropServices;
using System.Net.Http;
using System; // IProgress を使うために追加
using Illustra.Shared.Models; // Added for MCP events
using System.Text.RegularExpressions; // Regex を使うために追加
using System.Web; // HttpUtility を使うために追加

namespace Illustra.Views
{
    [System.Windows.Markup.ContentProperty("Content")]
    public partial class ThumbnailListControl : UserControl, IActiveAware, IFileSystemChangeHandler, INotifyPropertyChanged // INotifyPropertyChanged を追加
    {
        private IEventAggregator _eventAggregator = null!;
        private ThumbnailListViewModel _viewModel;
        private IllustraAppContext _appContext;
        private MainWindowViewModel _mainWindowViewModel = null!; // MainWindowViewModel のフィールドを追加
        // 画像閲覧用
        private ImageViewerWindow? _imageViewerWindow;
        private string? _currentFolderPath;

        private AppSettingsModel _appSettings;
        private ThumbnailLoaderHelper _thumbnailLoader;
        private FileSystemMonitor _fileSystemMonitor;

        private bool _isInitialized = false;
        private bool _isUpdatingSelection = false;  // 選択状態の更新中フラグ
        private bool _isDragging = false;
        private readonly DispatcherTimer _resizeTimer;

        // クラスレベルの変数を追加
        private bool _isPromptFilterEnabled = false;
        private List<string> _currentTagFilters = new List<string>();
        private bool _isTagFilterEnabled = false;

        private List<string> _currentExtensionFilters = new List<string>();
        private bool _isExtensionFilterEnabled = false;
        private bool _isScrolling = false;
        private DispatcherTimer _scrollStopTimer;

        private CancellationTokenSource? _thumbnailLoadCts;


        private bool _isLoadingFileNodes = false; // ファイルノード読み込み中フラグ
        private string? _initialSelectedFilePath;

        // ツールバーの表示状態を管理するプロパティを追加
        private bool _isToolbarVisible = true;
        public bool IsToolbarVisible
        {
            get => _isToolbarVisible;
            set => SetProperty(ref _isToolbarVisible, value);
        }

        // INotifyPropertyChanged の実装
        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        protected bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string propertyName = "")
        {
            if (EqualityComparer<T>.Default.Equals(storage, value)) return false;
            storage = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        /// <summary>
        /// ViewModelの選択状態をUIに反映します
        /// </summary>
        private void UpdateUISelection()
        {
            try
            {
                _isUpdatingSelection = true;

                ThumbnailItemsControl.SelectedItems.Clear();
                foreach (var item in _viewModel.SelectedItems)
                {
                    ThumbnailItemsControl.SelectedItems.Add(item);
                }

                // 選択アイテムがある場合はログ出力
                if (_viewModel.SelectedItems.Any())
                {
                    var selectedItem = _viewModel.SelectedItems.First();
                    var nodeIndex = _viewModel.Items.IndexOf(selectedItem);
                    System.Diagnostics.Debug.WriteLine($"[選択更新] アイテムを選択: [{nodeIndex}] {(selectedItem as FileNodeModel)?.FullPath ?? "不明"}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[選択更新] エラー: {ex.Message}");
            }
            finally
            {
                _isUpdatingSelection = false;
            }
        }
        private readonly Queue<Func<Task>> _thumbnailLoadQueue = new Queue<Func<Task>>();
        private readonly DispatcherTimer _thumbnailLoadTimer;
        private const string CONTROL_ID = "ThumbnailList";
        private bool _isSortAscending = true;
        private bool _isSortByDate = true;
        private bool _isFirstLoad = true;

        #region IActiveAware Implementation
#pragma warning disable 0067 // 使用されていませんという警告を無視
        public bool IsActive { get; set; }
        public event EventHandler? IsActiveChanged;
#pragma warning restore 0067 // 警告の無視を終了
        #endregion

        public static bool CanAcceptImageDrop(IDataObject dataObject)
        {
            if (dataObject == null) return false;

            bool hasDescriptor = dataObject.GetDataPresent("FileGroupDescriptorW");
            bool hasContents = dataObject.GetDataPresent("FileContents");
            bool hasIgnoreFlag = dataObject.GetDataPresent("chromium/x-ignore-file-contents");

            // ✅ 仮想ファイル（本物）だけOK
            if (!hasIgnoreFlag && hasDescriptor && hasContents)
                return true;

            // ✅ ローカルファイル（FileDrop）は無条件OK
            if (dataObject.GetDataPresent(DataFormats.FileDrop))
                return true;

            // ✅ URLの場合、画像URLかどうか拡張子チェック
            if (dataObject.GetDataPresent(DataFormats.UnicodeText))
            {
                try
                {
                    string url = dataObject.GetData(DataFormats.UnicodeText) as string;
                    if (Uri.TryCreate(url, UriKind.Absolute, out Uri uri))
                    {
                        string ext = Path.GetExtension(uri.LocalPath).ToLowerInvariant();
                        // Use FileHelper.SupportedExtensions which includes video extensions
                        string[] allowedExts = FileHelper.SupportedExtensions;

                        if (!string.IsNullOrEmpty(ext) && allowedExts.Contains(ext))
                            return true;
                    }
                }
                catch
                {
                    // 無視（URL壊れてるか不明）
                }
            }

            // ❌ それ以外は拒否
            return false;
        }


        public class CustomDropHandler : DefaultDropHandler
        {
            private ThumbnailListControl _control = null;
            public CustomDropHandler(ThumbnailListControl control)
            {
                _control = control;
            }
            public override void DragOver(IDropInfo e)
            {
                base.DragOver(e);
                // サムネイル一覧からサムネイル一覧へのドロップ無効

                // カスタムデータフォーマットを確認
                var dataObject = e.Data as IDataObject;
                if (dataObject != null && dataObject.GetDataPresent(typeof(FileNodeModel).Name))
                {
                    e.DropTargetAdorner = null;
                    return;
                }

                if (e.DropTargetAdorner == DropTargetAdorners.Insert)
                {
                    e.DropTargetAdorner = null;
                }

                if (CanAcceptImageDrop(dataObject))
                {
                    bool isCopy = (e.KeyStates & DragDropKeyStates.ControlKey) != 0;
                    e.Effects = isCopy ? DragDropEffects.Copy : DragDropEffects.Move;
                }
                else
                {
                    e.Effects = DragDropEffects.None;
                }
            }

            public override void Drop(IDropInfo e)
            {
                _control.ThumbnailItemsControl_Drop(e);
            }
        }

        private class CustomPreviewItemSorter : IDragPreviewItemsSorter
        {
            public IEnumerable SortDragPreviewItems(IEnumerable items)
            {
                var itemList = items.Cast<object>().ToList(); // リストに変換
                var count = Math.Min(itemList.Count, 4); // 最大4つ、でも要素数が4未満ならその数に調整
                var allItems = itemList.GetRange(0, count); // 安全な範囲で取得
                var compositeItem = new CompositeItem { Items = allItems };
                return new[] { compositeItem };
            }

            // コンテナクラス
            public class CompositeItem
            {
                public IEnumerable<object> Items { get; set; }
            }
        }

        // xaml でインスタンス化するためのデフォルトコンストラクタ
        public ThumbnailListControl()
        {
            InitializeComponent();
            Loaded += ThumbnailListControl_Loaded;

            // サムネイルサイズ変更用のタイマーを初期化
            _resizeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
            _resizeTimer.Tick += async (s, e) =>
            {
                _resizeTimer.Stop();
                await UpdateThumbnailSize();
            };

            // 設定を読み込む
            _appSettings = SettingsHelper.GetSettings();

            // ViewModelをDIコンテナから取得
            // MainWindowViewModel を DI コンテナから取得
            _mainWindowViewModel = ContainerLocator.Container.Resolve<MainWindowViewModel>();
            _viewModel = ContainerLocator.Container.Resolve<ThumbnailListViewModel>();
            DataContext = _viewModel;

            // アプリケーション全体で共有するコンテキストにViewModelを設定
            var appContext = ContainerLocator.Container.Resolve<IllustraAppContext>();
            if (appContext != null)
            {
                appContext.MainViewModel = _viewModel;
            }

            var db = ContainerLocator.Container.Resolve<DatabaseManager>();

            GongSolutions.Wpf.DragDrop.DragDrop.SetDropHandler(ThumbnailItemsControl, new CustomDropHandler(this));
            GongSolutions.Wpf.DragDrop.DragDrop.SetDragHandler(ThumbnailItemsControl, new FileNodeDragHandler());
            GongSolutions.Wpf.DragDrop.DragDrop.SetDragPreviewItemsSorter(ThumbnailItemsControl, new CustomPreviewItemSorter());
            GongSolutions.Wpf.DragDrop.DragDrop.SetDragAdornerTranslation(ThumbnailItemsControl, new Point(5, 20));

            // キーボードイベントハンドラのバインド
            ThumbnailItemsControl.PreviewKeyDown += async (s, e) => await ThumbnailItemsControl_PreviewKeyDown(s, e);

            // DatabaseManagerの取得とサムネイルローダーの初期化
            _thumbnailLoader = new ThumbnailLoaderHelper(
                ThumbnailItemsControl,
                SelectThumbnail,
                this,
                _viewModel,
                db,
                new DefaultThumbnailProcessor(this) // IThumbnailProcessorServiceの実装を追加
            );
            _thumbnailLoader.ScrollToItemRequested += OnScrollToItemRequested;
            // FileNodesLoaded イベント購読は削除

            // スライダーのドラッグイベントを購読
            ThumbnailSizeSlider.PreviewMouseLeftButtonDown += (s, e) =>
            {
                // スライダーのつまみを掴んだ場合のみドラッグモードにする
                _isDragging = !(e.OriginalSource is System.Windows.Shapes.Rectangle);
            };

            // ファイルシステム監視の初期化
            _fileSystemMonitor = new FileSystemMonitor(this);

            // サムネイルサイズを設定から復元
            ThumbnailSizeSlider.Value = _appSettings.ThumbnailSize;
            ThumbnailSizeText.Text = _appSettings.ThumbnailSize.ToString();
            _thumbnailLoader.ThumbnailSize = _appSettings.ThumbnailSize;

            // サムネイルロード用のタイマーを初期化
            _thumbnailLoadTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _thumbnailLoadTimer.Tick += async (s, e) => await ProcessThumbnailLoadQueue();
            _thumbnailLoadTimer.Start();

            // ソート設定を復元
            _isSortByDate = _appSettings.SortByDate;
            _isSortAscending = _appSettings.SortAscending;
            SortTypeText.Text = _isSortByDate ?
                (string)Application.Current.FindResource("String_Thumbnail_SortByDate") :
                (string)Application.Current.FindResource("String_Thumbnail_SortByName");
            SortDirectionText.Text = _isSortAscending ?
                (string)Application.Current.FindResource("String_Thumbnail_SortAscending") :
                (string)Application.Current.FindResource("String_Thumbnail_SortDescending");

            _initialSelectedFilePath = _appSettings.SelectLastFileOnStartup ? _appSettings.LastSelectedFilePath : null;
            _isInitialized = true;

            // スクロール停止検出用タイマーの初期化
            _scrollStopTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(300)
            };
            _scrollStopTimer.Tick += ScrollStopTimer_Tick;

            // 初期ツールバー状態を設定
            UpdateToolbarState();

            // Tabs コレクションの変更を監視
            if (_mainWindowViewModel.Tabs is INotifyCollectionChanged tabsCollection)
            {
                tabsCollection.CollectionChanged += Tabs_CollectionChanged;
            }

            // ViewModel が破棄されるときにイベントハンドラを解除する処理も必要 (例: Unloaded イベント)
            Unloaded += (s, e) =>
            {
                if (_mainWindowViewModel?.Tabs is INotifyCollectionChanged collection) // Nullチェック追加
                {
                    collection.CollectionChanged -= Tabs_CollectionChanged;
                }
            };
        }

        private void OnFileSelected(SelectedFileModel args)
        {
            if (args.SourceId != CONTROL_ID)
            {
                SelectThumbnail(args.FullPath);
            }
        }

        private enum PromptCopyType { Positive, Negative, All }

        private void CopyPrompt(PromptCopyType type)
        {
            if (_viewModel.SelectedItems.LastOrDefault() is not FileNodeModel selectedItem) return;

            var currentProperties = _appContext.CurrentProperties;
            if (currentProperties?.StableDiffusionResult == null) return;

            try
            {
                string textToCopy = "";
                var result = currentProperties.StableDiffusionResult;

                switch (type)
                {
                    case PromptCopyType.Positive:
                        textToCopy = result.Prompt;
                        break;
                    case PromptCopyType.Negative:
                        textToCopy = result.NegativePrompt;
                        break;
                    case PromptCopyType.All:
                        textToCopy = currentProperties.UserComment; // UserComment全体をコピー
                        break;
                }

                if (!string.IsNullOrEmpty(textToCopy))
                {
                    Clipboard.SetText(textToCopy.Trim());
                    ToastNotificationHelper.ShowRelativeTo(this, (string)Application.Current.FindResource("String_Thumbnail_PromptCopied"));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"プロンプトのコピー中にエラーが発生しました: {ex.Message}");
            }
        }

        /// <summary>
        /// 指定されたアイテムのコンテキストメニューを表示します。
        /// </summary>
        /// <param name="clickedItem">右クリックされたアイテムのFileNodeModel</param>
        /// <param name="placementTarget">メニューを表示する基準要素</param>
        private void ShowContextMenu(FileNodeModel clickedItem, FrameworkElement placementTarget)
        {
            if (clickedItem == null) return;

            // 右クリックされたアイテムのプロパティを取得 (必要に応じて非同期で取得)
            // この例では同期的にAppContextから取得していますが、
            // 必要であれば clickedItem.FullPath を使ってDBなどから取得してください。
            // var itemProperties = await GetPropertiesForItemAsync(clickedItem.FullPath);
            // 現状は選択中のアイテムのプロパティを表示する仕様のままにします。
            // 右クリックアイテムのプロパティを表示したい場合は、AppContext.CurrentProperties の更新が必要です。
            var currentProperties = _appContext.CurrentProperties;

            // コンテキストメニューを作成
            var menu = new ContextMenu();

            // --- プロンプト関連メニュー (選択中のアイテムのプロパティを使用) ---
            if (currentProperties?.StableDiffusionResult != null)
            {
                var copyPromptItem = new MenuItem
                {
                    Header = (string)Application.Current.FindResource("String_Thumbnail_CopyPrompt")
                };
                // CopyPromptは選択中のアイテムを対象とするため、引数は不要
                copyPromptItem.Click += (s, e) => CopyPrompt(PromptCopyType.Positive);
                menu.Items.Add(copyPromptItem);

                if (!string.IsNullOrEmpty(currentProperties.StableDiffusionResult.NegativePrompt))
                {
                    var copyNegativePromptItem = new MenuItem
                    {
                        Header = (string)Application.Current.FindResource("String_Thumbnail_CopyNegativePrompt")
                    };
                    copyNegativePromptItem.Click += (s, e) => CopyPrompt(PromptCopyType.Negative);
                    menu.Items.Add(copyNegativePromptItem);
                }

                var copyAllPromptItem = new MenuItem
                {
                    Header = (string)Application.Current.FindResource("String_Thumbnail_CopyAllPrompt")
                };
                copyAllPromptItem.Click += (s, e) => CopyPrompt(PromptCopyType.All);
                menu.Items.Add(copyAllPromptItem);
                menu.Items.Add(new Separator()); // 区切り線を追加
            }

            // --- 右クリックされたアイテムに対する操作 ---
            // ファイルパスコピーを追加
            var copyPathItem = new MenuItem
            {
                Header = (string)Application.Current.FindResource("String_Thumbnail_CopyFilePath")
            };
            copyPathItem.Click += (s, e) =>
            {
                if (clickedItem != null)
                {
                    Clipboard.SetText(clickedItem.FullPath);
                    ToastNotificationHelper.ShowRelativeTo(this, (string)Application.Current.FindResource("String_Thumbnail_FilePathCopied"));
                }
            };
            menu.Items.Add(copyPathItem);

            // セパレータを追加
            menu.Items.Add(new Separator());

            // ファイルをコピー（CopySelectedImagesToClipboardメソッドを再利用）
            var copyFileItem = new MenuItem
            {
                Header = (string)Application.Current.FindResource("String_Thumbnail_CopyFile")
            };
            copyFileItem.Click += (s, e) =>
            {
                // 既存のCopySelectedImagesToClipboardメソッドを使用
                // これにより複数選択されたファイルのコピーにも対応
                CopySelectedImagesToClipboard();
            };
            menu.Items.Add(copyFileItem);

                        // 名前の変更
            var renameItem = new MenuItem
            {
                Header = (string)Application.Current.FindResource("String_Thumbnail_ContextMenu_Rename")
            };
            // DoRenameAsyncには右クリックされたアイテムを渡す
            renameItem.Click += async (s, e) => await DoRenameAsync(clickedItem);
            menu.Items.Add(renameItem);


            // ファイル削除のメニュー項目を追加
            var deleteItem = new MenuItem
            {
                Header = (string)Application.Current.FindResource("String_Thumbnail_ContextMenu_Delete")
            };
            deleteItem.Click += (s, e) =>
            {
                DeleteSelectedItems();
            };
            menu.Items.Add(deleteItem);
            menu.Items.Add(new Separator()); // 区切り線を追加

            var explorerItem = new MenuItem
            {
                Header = (string)Application.Current.FindResource("String_Thumbnail_OpenInExplorer")
            };
            explorerItem.Click += (s, e) =>
            {
                if (clickedItem != null && File.Exists(clickedItem.FullPath))
                {
                    Process.Start("explorer.exe", $"/select,\"{clickedItem.FullPath}\"");
                }
            };
            menu.Items.Add(explorerItem);

            // メニュー項目が1つもない場合は表示しない
            if (menu.Items.Count == 0) return;

            // メニューを表示
            menu.PlacementTarget = placementTarget; // 引数で受け取った要素を基準にする
            menu.IsOpen = true;
        }

        private async Task DoRenameAsync(FileNodeModel selectedItem)
        {
            var oldPath = selectedItem.FullPath;
            var dialog = new RenameDialog(oldPath, false)
            {
                Owner = Window.GetWindow(this),
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            if (dialog.ShowDialog() == true)
            {
                var newPath = dialog.NewFilePath;
                try
                {
                    var fileOp = new FileOperationHelper(ContainerLocator.Container.Resolve<DatabaseManager>());
                    var fileNode = await fileOp.RenameFile(oldPath, newPath);
                    if (fileNode == null) return; // 変更なし

                    var oldFileNode = _viewModel.Items.FirstOrDefault(x => x.FullPath == oldPath);
                    if (oldFileNode != null)
                    {
                        oldFileNode.FullPath = newPath;
                        oldFileNode.FileName = Path.GetFileName(newPath);
                    }

                    await SortThumbnailAsync(_isSortByDate, _isSortAscending, true);
                    // ビューワなどで新しいパスで開くためにイベントを発行
                    _eventAggregator.GetEvent<FileSelectedEvent>().Publish(new SelectedFileModel(CONTROL_ID, newPath));
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"エラー: {ex.Message}", "名前の変更エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void ThumbnailListControl_Loaded(object sender, RoutedEventArgs e)
        {
            // AppContextを取得
            _appContext = ContainerLocator.Container.Resolve<IllustraAppContext>();

            // ContainerLocatorを使ってEventAggregatorを取得
            _eventAggregator = ContainerLocator.Container.Resolve<IEventAggregator>();
            _eventAggregator.GetEvent<SelectedTabChangedEvent>().Subscribe(OnSelectedTabChanged, ThreadOption.UIThread);


            // ThumbnailItemsControlの右クリックイベントを設定 (async void に変更)
            ThumbnailItemsControl.PreviewMouseRightButtonDown += async (s, e) =>
            {
                // 右クリックされた位置のListViewItemを取得
                var listViewItem = UIHelper.FindVisualParent<ListViewItem>(e.OriginalSource as DependencyObject);
                if (listViewItem != null && listViewItem.DataContext is FileNodeModel clickedItem)
                {
                    e.Handled = true; // イベントを先に処理済みにする

                    // 右クリックされたアイテムを選択状態にする
                    if (!ThumbnailItemsControl.SelectedItems.Contains(clickedItem))
                    {
                        ThumbnailItemsControl.SelectedItems.Clear();
                        ThumbnailItemsControl.SelectedItems.Add(clickedItem);
                    }
                    // ViewModelの選択状態も更新 (UI->ViewModel)
                    _viewModel.SelectedItems.Clear();
                    _viewModel.SelectedItems.Add(clickedItem);

                    try
                    {
                        // AppContextのプロパティを非同期で更新し、完了を待つ
                        await _appContext.UpdateCurrentPropertiesAsync(clickedItem.FullPath);

                        // プロパティ更新後にコンテキストメニューを表示
                        ShowContextMenu(clickedItem, listViewItem);
                    }
                    catch (Exception ex)
                    {
                        LogHelper.LogError($"コンテキストメニュー表示前のプロパティ更新中にエラー: {clickedItem.FullPath}", ex);
                        // エラーが発生した場合でも、基本的なメニューは表示試行する（オプション）
                        // ShowContextMenu(clickedItem, listViewItem);
                    }
                }
                else
                {
                    // アイテム外で右クリックされた場合は何もしないか、別のメニューを表示するなど
                    e.Handled = false; // デフォルトのコンテキストメニューなどを許可する場合
                }
            };

            var window = Window.GetWindow(this);
            if (window != null)
            {
                window.Activated += ParentWindow_Activated;
            }

            // ショートカットキーイベントを購読
            _eventAggregator.GetEvent<ShortcutKeyEvent>().Subscribe(OnShortcutKeyReceived, ThreadOption.UIThread);

            // 自分が発信したイベントは無視
            _eventAggregator.GetEvent<FilterChangedEvent>().Subscribe(OnFilterChanged, ThreadOption.UIThread, false,
                filter => filter.SourceId != CONTROL_ID); // 自分が発信したイベントは無視
            _eventAggregator.GetEvent<RatingChangedEvent>().Subscribe(OnRatingChanged, ThreadOption.UIThread, false);
            _eventAggregator.GetEvent<LanguageChangedEvent>().Subscribe(OnLanguageChanged);
            _eventAggregator.GetEvent<SortOrderChangedEvent>().Subscribe(OnSortOrderChanged, ThreadOption.UIThread, false,
                filter => filter.SourceId != CONTROL_ID); // 自分が発信したイベントは無視
            _eventAggregator.GetEvent<FileSelectedEvent>().Subscribe(OnFileSelected, ThreadOption.UIThread, false,
                filter => filter.SourceId != CONTROL_ID); // 自分が発信したイベントは無視

            // ViewModelからのコマンド実行要求イベントを購読
            _eventAggregator.GetEvent<RequestCopyEvent>().Subscribe(OnRequestCopy, ThreadOption.UIThread);
            _eventAggregator.GetEvent<RequestPasteEvent>().Subscribe(OnRequestPaste, ThreadOption.UIThread);
            _eventAggregator.GetEvent<RequestSelectAllEvent>().Subscribe(OnRequestSelectAll, ThreadOption.UIThread);


            // ListView の ScrollViewer を取得
            if (ThumbnailItemsControl.Template.FindName("ScrollViewer", ThumbnailItemsControl) is ScrollViewer scrollViewer)
            {
                // ScrollBar を取得してイベントを追加
                if (scrollViewer.Template.FindName("PART_VerticalScrollBar", scrollViewer) is ScrollBar verticalScrollBar)
                {
                    // `PART_Track` から `Thumb` を取得
                    verticalScrollBar.ApplyTemplate();
                    if (verticalScrollBar.Template.FindName("PART_Track", verticalScrollBar) is Track track &&
                        track.Thumb is Thumb thumb)
                    {
                        // イベントを追加
                        thumb.DragStarted += ScrollBar_DragStarted;
                        thumb.DragCompleted += ScrollBar_DragCompleted;
                    }
                }
            }
        }

        private void ParentWindow_Activated(object? sender, EventArgs e)
        {
            // ウィンドウがアクティブになったときにフォーカスを設定
            var focused = Keyboard.FocusedElement;
            // TreeViewやその子孫がフォーカス中ならフォーカスしない
            if (focused is DependencyObject d && !IsInsideTreeView(d))
            {
                ThumbnailItemsControl.Focus();
            }
        }

        #region ViewModel Event Handlers

        private void OnRequestCopy()
        {
            // 実際のコピー処理を実行
            if (_viewModel.CopyCommand.CanExecute(null)) // Use CanExecute method of the command
            {
                CopySelectedImagesToClipboard();
            }
        }

        private void OnRequestPaste()
        {
            // 実際の貼り付け処理を実行
            // PasteのCanExecuteはViewModel側でフォルダパスのみチェックしているため、
            // ここでクリップボードの状態を最終確認する
            if (Clipboard.ContainsFileDropList() && !string.IsNullOrEmpty(_currentFolderPath))
            {
                PasteFilesFromClipboard();
            }
        }

        private void OnRequestSelectAll()
        {
            // 実際の全選択処理を実行
            ThumbnailItemsControl.SelectAll();
        }

        #endregion


        private void OnShortcutKeyReceived(ShortcutKeyEventArgs args)
        {
            // 自分自身から発行されたイベントは無視
            if (args.SourceId == CONTROL_ID)
                return;

            var shortcutHandler = KeyboardShortcutHandler.Instance;

            // コピー (Ctrl+C)
            if (shortcutHandler.IsShortcutMatch(FuncId.Copy, args.Key)) // Modifiers 引数を削除
            {
                CopySelectedImagesToClipboard();
            }
            // 貼り付け (Ctrl+V)
            else if (shortcutHandler.IsShortcutMatch(FuncId.Paste, args.Key)) // Modifiers 引数を削除
            {
                PasteFilesFromClipboard();
            }
            // 切り取り (Ctrl+X) - Note: FuncIdにCutがない場合は追加が必要
            // else if (shortcutHandler.IsShortcutMatch(FuncId.Cut, args.Key)) // Modifiers 引数を削除
            // {
            //     // 切り取り処理を実装
            // }
            // すべて選択 (Ctrl+A)
            else if (shortcutHandler.IsShortcutMatch(FuncId.SelectAll, args.Key)) // Modifiers 引数を削除
            {
                ThumbnailItemsControl.SelectAll();
            }
            // 削除 (Delete)
            else if (shortcutHandler.IsShortcutMatch(FuncId.Delete, args.Key)) // Modifiers 引数を削除
            {
                DeleteSelectedItems();
            }
            // リストの先頭に移動 (Home)
            else if (shortcutHandler.IsShortcutMatch(FuncId.MoveToStart, args.Key)) // Modifiers 引数を削除
            {
                if (ThumbnailItemsControl.Items.Count > 0)
                {
                    ThumbnailItemsControl.SelectedIndex = 0;
                    ThumbnailItemsControl.ScrollIntoView(ThumbnailItemsControl.SelectedItem);
                }
            }
            // リストの末尾に移動 (End)
            else if (shortcutHandler.IsShortcutMatch(FuncId.MoveToEnd, args.Key)) // Modifiers 引数を削除
            {
                if (ThumbnailItemsControl.Items.Count > 0)
                {
                    ThumbnailItemsControl.SelectedIndex = ThumbnailItemsControl.Items.Count - 1;
                    ThumbnailItemsControl.ScrollIntoView(ThumbnailItemsControl.SelectedItem);
                }
            }
        }

        private async void OnFilterChanged(FilterChangedEventArgs args)
        {
            // 自分自身 ("ThumbnailList") が発行したイベントは無視 (UI操作の結果なのでViewModelは更新済みのはず)
            if (args.SourceId == CONTROL_ID)
            {
                // Debug.WriteLine($"[ThumbnailListControl.OnFilterChanged] Ignored event from self.");
                return;
            }

            // args.Type はもう存在しないので、新しいプロパティでログ出力
            Debug.WriteLine($"[ThumbnailListControl.OnFilterChanged] Received from: {args.SourceId}, Clear: {args.IsClearOperation}, Changed: {string.Join(",", args.ChangedTypes)}"); // IsFullUpdate 参照を削除

            try
            {
                int ratingToApply = _viewModel.CurrentRatingFilter;
                bool promptToApply = _isPromptFilterEnabled;
                List<string> tagsToApply = _currentTagFilters;
                bool isTagEnabledToApply = _isTagFilterEnabled;
                List<string> extensionsToApply = _currentExtensionFilters;
                bool isExtensionEnabledToApply = _isExtensionFilterEnabled;

                // クリア操作の場合
                if (args.IsClearOperation)
                {
                    ratingToApply = 0;
                    promptToApply = false;
                    tagsToApply = new List<string>();
                    isTagEnabledToApply = false;
                    extensionsToApply = new List<string>();
                    isExtensionEnabledToApply = false;
                }
                // 個別の変更操作の場合
                else
                {
                    if (args.ChangedTypes.Contains(FilterChangedEventArgs.FilterChangedType.Rating))
                        ratingToApply = args.RatingFilter;
                    if (args.ChangedTypes.Contains(FilterChangedEventArgs.FilterChangedType.Prompt))
                        promptToApply = args.IsPromptFilterEnabled;
                    if (args.ChangedTypes.Contains(FilterChangedEventArgs.FilterChangedType.Tag))
                    {
                        tagsToApply = args.TagFilters ?? new List<string>();
                        isTagEnabledToApply = args.IsTagFilterEnabled;
                    }
                    if (args.ChangedTypes.Contains(FilterChangedEventArgs.FilterChangedType.Extension))
                    {
                        extensionsToApply = args.ExtensionFilters ?? new List<string>();
                        isExtensionEnabledToApply = args.IsExtensionFilterEnabled;
                    }
                }

                // 内部状態を更新 (UI表示用) - ViewModelのプロパティも更新
                _viewModel.CurrentRatingFilter = ratingToApply;
                _isPromptFilterEnabled = promptToApply;
                _currentTagFilters = new List<string>(tagsToApply); // 新しいリストを作成
                _isTagFilterEnabled = isTagEnabledToApply;
                _currentExtensionFilters = new List<string>(extensionsToApply); // 新しいリストを作成
                _isExtensionFilterEnabled = isExtensionEnabledToApply;


                // ViewModelにフィルタ適用を指示
                await _viewModel.ApplyAllFilters(
                    ratingToApply,
                    promptToApply,
                    tagsToApply, // 更新されたリストを渡す
                    isTagEnabledToApply,
                    extensionsToApply, // 更新されたリストを渡す
                    isExtensionEnabledToApply
                );

                // フィルタリング後の選択位置を更新 (ApplyAllFilters内で実行されるように変更された可能性もあるため、必要に応じて調整)
                // _thumbnailLoader.UpdateSelectionAfterFilter(); // ApplyAllFilters内で実行されるなら不要かも
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"フィルタ変更イベントの処理中にエラーが発生しました: {ex.Message}");
            }
        }

        public void SetCurrentSettings()
        {
            // 現在のサムネイルサイズを保存
            _appSettings.ThumbnailSize = (int)ThumbnailSizeSlider.Value;
            _appSettings.LastSelectedFilePath = _viewModel.SelectedItems.LastOrDefault()?.FullPath ?? "";

            // ソート条件を保存
            _appSettings.SortByDate = _isSortByDate;
            _appSettings.SortAscending = _isSortAscending;
        }

        /// <summary>
        /// マウスホイールでサムネイルをスクロールする処理
        /// </summary>
        private void ThumbnailItemsControl_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            try
            {
                var scrollViewer = UIHelper.FindVisualChild<ScrollViewer>(ThumbnailItemsControl);
                if (scrollViewer != null)
                {
                    // マウスホイールの方向を維持しつつ、倍率を適用
                    double multiplier = _appSettings?.MouseWheelMultiplier ?? 1.0;
                    double delta = e.Delta;

                    // 標準のスクロール量は48ピクセル
                    const double baseScrollAmount = 48.0;
                    // 上スクロール時は負の値、下スクロール時は正の値を維持
                    double scrollAmount = baseScrollAmount * multiplier * Math.Sign(delta);

                    // 現在のオフセット位置から計算した新しい位置にスクロール
                    double newOffset = scrollViewer.VerticalOffset - scrollAmount;
                    scrollViewer.ScrollToVerticalOffset(newOffset);

                    // ログ出力
                    LogHelper.LogWithTimestamp($"マウスホイール移動: 倍率={multiplier:F1}, Delta={delta}, 移動量={scrollAmount:F1}px", LogHelper.Categories.ScrollTracking);

                    // スクロールイベントが処理されたことをマーク
                    e.Handled = true;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"マウスホイールスクロール処理エラー: {ex.Message}");
            }
        }

        private async void ThumbnailItemsControl_Loaded(object sender, RoutedEventArgs e)
        {
            var scrollViewer = await Task.Run(() => UIHelper.FindVisualChild<ScrollViewer>(ThumbnailItemsControl));
            if (scrollViewer != null)
            {
                // 実際のScrollViewerにイベントハンドラを直接登録
                scrollViewer.ScrollChanged += OnScrollChanged;

                // ウィンドウのサイズ変更イベントを購読
                var window = Window.GetWindow(this);
                if (window != null)
                {
                    window.SizeChanged += async (s, args) => await OnWindowSizeChanged(scrollViewer);
                }

                // スクロールバーのThumbを取得してドラッグイベントを登録
                var verticalScrollBar = UIHelper.FindVisualChild<ScrollBar>(scrollViewer);
                if (verticalScrollBar != null)
                {
                    var thumb = UIHelper.FindVisualChild<Thumb>(verticalScrollBar);
                    if (thumb != null)
                    {
                        thumb.DragStarted += (s, args) =>
                        {
                            // スクロールバードラッグ開始時の処理
                            _isScrolling = true;
                            _scrollStopTimer.Stop();
                            _scrollStopTimer.Start();
                        };

                        thumb.DragCompleted += (s, args) =>
                        {
                            // スクロールバードラッグ完了時の処理
                            _scrollStopTimer.Stop();
                            _scrollStopTimer.Start();
                        };
                    }
                }

                // マウスホイールイベントを登録
                scrollViewer.PreviewMouseWheel += OnMouseWheel;
            }

            // プロパティ変更通知の購読
            ((INotifyPropertyChanged)_viewModel).PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(ThumbnailListViewModel.SelectedItems))
                {
                    UpdateUISelection();
                }
            };

            // ListViewの選択状態が変更されたときのイベントハンドラを追加
            ThumbnailItemsControl.SelectionChanged += (s, args) =>
            {
                // UIからの更新ループを防ぐ / ViewModel更新中の処理をスキップ
                if (_isUpdatingSelection) return;

                // ViewModelの選択状態をUIの選択状態に同期させる (UI -> ViewModel)
                try
                {
                    _isUpdatingSelection = true; // ViewModel更新中のフラグ

                    // ViewModelの選択リストを効率的に更新
                    // (ViewModelにAddRange/RemoveRangeのようなメソッドがあれば、それを使うのが理想)
                    var currentUiSelection = ThumbnailItemsControl.SelectedItems.Cast<FileNodeModel>().ToList();

                    // ViewModelの選択リストを一括更新
                    _viewModel.SelectedItems.ReplaceAll(currentUiSelection);

                    // イベントの発行（最後に選択されたアイテムがある場合のみ）
                    var lastSelected = _viewModel.SelectedItems.LastOrDefault();
                    if (lastSelected != null)
                    {
                        var selectedFileModel = new SelectedFileModel(CONTROL_ID, lastSelected.FullPath);
                        _eventAggregator?.GetEvent<FileSelectedEvent>()?.Publish(selectedFileModel);
                    }
                    // 選択アイテム数を通知するイベントを発行
                    _eventAggregator?.GetEvent<SelectionCountChangedEvent>()?.Publish(new SelectionCountChangedEventArgs(ThumbnailItemsControl.SelectedItems.Count));
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[選択同期エラー] UI->ViewModel同期中にエラー: {ex.Message}");
                }
                finally
                {
                    _isUpdatingSelection = false;
                }
            };
        }



        /// <summary>
        /// 指定されたファイルパスの前の画像ファイルパスを取得します
        /// </summary>
        /// <param name="currentFilePath">現在の画像ファイルパス</param>
        /// <returns>前の画像のファイルパス、存在しない場合はnull</returns>
        public string? GetPreviousImage(string currentFilePath)
        {
            bool enableCyclicNavigation = IsCyclicNavigationEnabled();

            // フィルタリングされたアイテムのリストを取得
            var filteredItems = _viewModel.FilteredItems.Cast<FileNodeModel>().ToList();
            if (filteredItems.Count <= 1)
                return null;

            // 現在の画像のインデックスを検索
            var currentIndex = filteredItems.FindIndex(i => i.FullPath == currentFilePath);
            if (currentIndex < 0)
                return null;

            // 前のインデックスを計算
            if (currentIndex > 0)
            {
                // 通常の移動
                return filteredItems[currentIndex - 1].FullPath;
            }
            else if (IsCyclicNavigationEnabled())
            {
                // 循環移動が有効な場合は最後に移動
                return filteredItems[filteredItems.Count - 1].FullPath;
            }
            return null;
        }

        /// <summary>
        /// 指定されたファイルパスの次の画像ファイルパスを取得します
        /// </summary>
        /// <param name="currentFilePath">現在の画像ファイルパス</param>
        /// <returns>次の画像のファイルパス、存在しない場合はnull</returns>
        public string? GetNextImage(string currentFilePath)
        {
            bool enableCyclicNavigation = IsCyclicNavigationEnabled();

            // フィルタリングされたアイテムのリストを取得
            var filteredItems = _viewModel.FilteredItems.Cast<FileNodeModel>().ToList();
            if (filteredItems.Count <= 1)
                return null;

            // 現在の画像のインデックスを検索
            var currentIndex = filteredItems.FindIndex(i => i.FullPath == currentFilePath);
            if (currentIndex < 0)
                return null;

            // 次のインデックスを計算
            if (currentIndex < filteredItems.Count - 1)
            {
                // 通常の移動
                return filteredItems[currentIndex + 1].FullPath;
            }
            else if (IsCyclicNavigationEnabled())
            {
                // 循環移動が有効な場合は先頭に移動
                return filteredItems[0].FullPath;
            }
            return null;
        }

        private static bool IsInsideTreeView(DependencyObject element)
        {
            while (element != null)
            {
                if (element is TreeView || element is TreeViewItem)
                    return true;
                element = VisualTreeHelper.GetParent(element);
            }
            return false;
        }

        /// <summary>
        /// ファイルシステムの変更を処理するためのインターフェースの実装
        /// </summary>
        #region IFileSystemChangeHandler Implementation
        public void OnFileCreated(string path)
        {
            Debug.WriteLine($"[サムネイル] ファイル作成: {path}");
            // 画像ファイルと動画ファイルの両方を対象にする
            if (!FileHelper.IsMediaFile(path)) return;

            Dispatcher.Invoke(async () =>
            {
                try
                {
                    var fileNode = await _thumbnailLoader.CreateFileNodeAsync(path);

                    // 既存のファイルノードをチェック
                    if (_viewModel.Items.Any(x => x.FullPath == path))
                    {
                        Debug.WriteLine($"File already exists in the list: {path}");
                        return;
                    }

                    if (fileNode != null)
                    {
                        // ソート順に従って適切な位置に挿入
                        _viewModel.AddItem(fileNode);

                        // 新規ファイル自動選択の処理
                        if (_appSettings.AutoSelectNewFile)
                        {
                            // UIスレッドで選択処理とフォーカスを実行
                            await Dispatcher.InvokeAsync(() =>
                            {
                                SelectThumbnail(fileNode.FullPath);
                            });
                        }

                        _ = Task.Run(async () =>
                        {
                            await _viewModel.UpdatePromptCacheAsync(path);

                            // RefreshFiltering を await で完了を待ってから次の処理に進む
                            await Dispatcher.InvokeAsync(() =>
                            {
                                _viewModel.RefreshFiltering();
                            }).Task;  // Task を取得して await

                            // サムネイル生成をトリガー
                            var index = _viewModel.FilteredItems.Cast<FileNodeModel>().ToList().IndexOf(fileNode);
                            if (index >= 0)
                            {
                                await _thumbnailLoader.LoadMoreThumbnailsAsync(index, index, CancellationToken.None, isHighPriority: true);
                            }
                        });
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error processing created file: {ex.Message}");
                }
            });
        }

        public void OnFileDeleted(string path)
        {
            Dispatcher.Invoke(() =>
            {
                var fileNode = _viewModel.Items.FirstOrDefault(x => x.FullPath == path);
                if (fileNode != null)
                {
                    _viewModel.Items.Remove(fileNode);
                }
            });
        }

        public async Task OnChildFolderRenamed(string oldPath, string newPath)
        {
            Debug.WriteLine($"[サムネイル] 子フォルダ名変更: {oldPath} -> {newPath}");

            await Dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    // 一覧から検索
                    var existingNode = _viewModel.Items.FirstOrDefault(x => x.FullPath == oldPath);

                    // 新規ノードの取得
                    var newNode = await _thumbnailLoader.HandleFileRenamed(oldPath, newPath);
                    if (existingNode != null)
                    {
                        if (newNode != null)
                        {
                            existingNode.FullPath = newNode.FullPath;
                            existingNode.Rating = newNode.Rating;
                            existingNode.FileName = newNode.FileName;
                            existingNode.ThumbnailInfo = newNode.ThumbnailInfo;

                            await SortThumbnailAsync(_isSortByDate, _isSortAscending, true);

                            // サムネイル生成をトリガー
                            var index = _viewModel.Items.IndexOf(existingNode);
                            if (index >= 0)
                            {
                                await _thumbnailLoader.LoadMoreThumbnailsAsync(index, index, CancellationToken.None, isHighPriority: true);
                            }
                        }
                        else
                        {
                            Debug.WriteLine($"Error processing renamed file: {newPath}. {oldPath} is not found.");
                        }
                    }
                    else if (newNode != null)
                    {
                        OnFileCreated(newPath);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error processing renamed file: {ex.Message}");
                }
            });
        }
        #endregion

        // OnFileNodesLoaded イベントハンドラは不要になったため削除

        public async Task SortThumbnailAsync(bool sortByDate, bool sortAscending, bool selectItem = false)
        {
            var currentSelectedPath = selectItem ? _viewModel.SelectedItems.LastOrDefault()?.FullPath : null;

            // ソート実行
            await _viewModel.SortItemsAsync(sortByDate, sortAscending);

            if (!string.IsNullOrEmpty(currentSelectedPath))
            {
                // UIの更新を待つ
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);

                // 選択とスクロールを実行
                SelectThumbnail(currentSelectedPath);

                // ScrollIntoViewを確実に実行
                var item = _viewModel.FilteredItems.Cast<FileNodeModel>().FirstOrDefault(x => x.FullPath == currentSelectedPath);
                if (item != null)
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        ThumbnailItemsControl.ScrollIntoView(item);
                        ThumbnailItemsControl.UpdateLayout();
                    }, DispatcherPriority.Render);

                    // 現在表示されている範囲のサムネイルを再ロード
                    var scrollViewer = UIHelper.FindVisualChild<ScrollViewer>(ThumbnailItemsControl);
                    if (scrollViewer != null)
                    {
                        await LoadVisibleThumbnailsAsync(scrollViewer);
                    }
                }
            }
        }


        /// <summary>
        /// 指定されたファイルを選択します
        /// </summary>
        private async void SelectThumbnail(string filePath, bool requestFocus = false)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                System.Diagnostics.Debug.WriteLine("[選択] ファイルパスが空のため選択をスキップします");
                return;
            }

            System.Diagnostics.Debug.WriteLine($"[選択] SelectThumbnail メソッドが呼ばれました: {filePath}");

            await Dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    DisplayGeneratedItemsInfo(ThumbnailItemsControl);

                    // 選択するアイテムを検索
                    var filteredItems = GetFilteredItemsList();
                    var matchingItem = filteredItems.FirstOrDefault(x => x.FullPath == filePath);

                    if (matchingItem != null)
                    {
                        // アイテムのインデックスを取得
                        int selectedIndex = filteredItems.IndexOf(matchingItem);
                        LogHelper.LogWithTimestamp($"[選択] インデックス: {selectedIndex}, ファイル: {matchingItem.FullPath}", LogHelper.Categories.ThumbnailQueue);

                        _viewModel.SelectedItems.Clear();
                        // _viewModel.SelectedItems.Add(matchingItem);
                        ThumbnailItemsControl.SelectedItems.Add(matchingItem);
                        ThumbnailItemsControl.ScrollIntoView(matchingItem);
                        if (requestFocus)
                        {
                            ThumbnailItemsControl.Focus();
                        }

                        // SelectedItem 更新をトリガーに SelectedFileModel が発行されている
                        LogHelper.LogWithTimestamp($"[選択完了] インデックス: {selectedIndex}, ファイル: {filePath}", LogHelper.Categories.ThumbnailQueue);
                    }
                    else
                    {
                        // フィルタリングされていないアイテムから検索
                        var allItems = _viewModel.Items.Cast<FileNodeModel>().ToList();
                        var matchingItemFromAll = allItems.FirstOrDefault(x => x.FullPath == filePath);

                        if (matchingItemFromAll != null)
                        {
                            System.Diagnostics.Debug.WriteLine($"[選択] 全アイテムから一致するアイテムを見つけました: {matchingItemFromAll.FullPath}");

                            // フィルタリングを解除する必要がある場合
                            // ここでフィルタリングを解除するロジックを追加

                            // 再度検索
                            await Task.Delay(100); // フィルタリング解除後の更新を待つ
                            filteredItems = GetFilteredItemsList();
                            matchingItem = filteredItems.FirstOrDefault(x => x.FullPath == filePath);

                            if (matchingItem != null)
                            {
                                if (!ThumbnailItemsControl.SelectedItems.Contains(matchingItem))
                                {
                                    ThumbnailItemsControl.SelectedItems.Add(matchingItem);
                                }
                                _viewModel.SelectedItems.Clear();
                                _viewModel.SelectedItems.Add(matchingItem);
                                ThumbnailItemsControl.ScrollIntoView(matchingItem);

                                // SelectedItem 更新をトリガーに SelectedFileModel が発行されている
                                var index = _viewModel.Items.IndexOf(matchingItem);
                                System.Diagnostics.Debug.WriteLine($"[選択更新] フィルタリング解除後にアイテムを選択: [{index}] {filePath}");
                            }
                            else
                            {
                                System.Diagnostics.Debug.WriteLine($"[選択エラー] フィルタリング解除後もアイテムが見つかりません: {filePath}");
                            }
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"[選択エラー] 指定されたファイルパスに一致するアイテムが見つかりません: {filePath}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[選択エラー] SelectThumbnail でエラーが発生しました: {ex.Message}");
                }
            });
        }


        private async void OnScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            // ファイルノード読み込み中は処理しない
            if (_isLoadingFileNodes) return;

            // スクロール中フラグを設定
            _isScrolling = true;

            // スクロールタイマーをリセット
            _scrollStopTimer.Stop();
            _scrollStopTimer.Start();

            try
            {
                // ScrollViewerを取得
                var scrollViewer = sender as ScrollViewer;
                if (scrollViewer == null) return;

                // スクロール位置から表示範囲を取得（小さめのバッファを使用）
                (int firstIndex, int lastIndex) = GetVisibleRangeWithBuffer(scrollViewer, bufferSize: 5);

                // 現在の処理をキャンセル
                if (_thumbnailLoadCts != null)
                {
                    _thumbnailLoadCts.Cancel();
                    _thumbnailLoadCts.Dispose();
                }

                // 新しいキャンセルトークンを作成
                _thumbnailLoadCts = new CancellationTokenSource();

                // 表示範囲のサムネイルを読み込む（低優先度）
                await _thumbnailLoader.LoadMoreThumbnailsAsync(firstIndex, lastIndex, _thumbnailLoadCts.Token, isHighPriority: false);
            }
            catch (OperationCanceledException)
            {
                // キャンセルは正常な動作として扱う
                LogHelper.LogScrollTracking("[スクロール中] サムネイル読み込みがキャンセルされました");
            }
            catch (Exception ex)
            {
                // その他の例外はログに記録
                LogHelper.LogError($"[スクロール中] サムネイル読み込み中にエラーが発生しました: {ex.Message}", ex);
            }
        }


        private async Task ProcessThumbnailLoadQueue()
        {
            try
            {
                if (_thumbnailLoadQueue.Count == 0)
                {
                    _thumbnailLoadTimer.Stop();
                    return;
                }

                LogWithTimestamp($"[サムネイルロードキュー] ProcessThumbnailLoadQueue メソッドが呼ばれました Deque {_thumbnailLoadQueue.Count} items");

                // キューから取り出して処理
                var task = _thumbnailLoadQueue.Dequeue();
                await task();

                // キューが空になったらタイマーを停止
                if (_thumbnailLoadQueue.Count == 0)
                {
                    _thumbnailLoadTimer.Stop();
                    LogWithTimestamp("[サムネイルロードキュー] 全てのサムネイル読み込みが完了しました");
                }
            }
            catch (OperationCanceledException)
            {
                // キャンセルは正常な動作
                LogWithTimestamp("[サムネイルロードキュー] 処理がキャンセルされました");
            }
            catch (Exception ex)
            {
                LogWithTimestamp($"[サムネイルロードキュー] エラー: {ex.Message}");
            }
        }

        private async Task LoadVisibleThumbnailsAsync(ScrollViewer scrollViewer, bool includePreload = false, CancellationToken cancellationToken = default)
        {
            try
            {
                await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
                cancellationToken.ThrowIfCancellationRequested();

                if (ThumbnailItemsControl == null || ThumbnailItemsControl.Items.Count == 0)
                    return;

                // スクロール位置のデバッグ情報
                Debug.WriteLine($"[検出] スクロール位置: {scrollViewer.VerticalOffset:F2}/{scrollViewer.ScrollableHeight:F2}");

                // 表示範囲内のアイテムを特定するための変数
                int firstIndexToLoad = -1;
                int lastIndexToLoad = -1;

                // 方法1: コンテナの可視性を直接チェック（正確だが重い）
                bool foundVisibleItems = false;

                // スクロール中は軽量な計算方式を使用し、停止時は正確な方法を使用
                if (!_isScrolling || includePreload)
                {
                    // 実際のコンテナの可視性をチェック
                    for (int i = 0; i < ThumbnailItemsControl.Items.Count; i++)
                    {
                        var container = ThumbnailItemsControl.ItemContainerGenerator.ContainerFromIndex(i) as ListViewItem;
                        if (container != null && container.IsVisible)
                        {
                            if (firstIndexToLoad == -1)
                                firstIndexToLoad = i;
                            lastIndexToLoad = i;
                            foundVisibleItems = true;
                        }
                        else if (foundVisibleItems && firstIndexToLoad != -1)
                        {
                            // 可視アイテムの連続が途切れたら終了（パフォーマンス向上のため）
                            break;
                        }
                    }
                }

                // 方法2: 計算ベースの方法（軽量だが誤差あり）
                if (!foundVisibleItems)
                {
                    double viewportTop = scrollViewer.VerticalOffset;
                    double viewportBottom = viewportTop + scrollViewer.ViewportHeight;
                    double viewportWidth = scrollViewer.ViewportWidth;

                    var panel = UIHelper.FindVisualChild<VirtualizingWrapPanel>(ThumbnailItemsControl);
                    if (panel == null)
                    {
                        Debug.WriteLine("[検出] パネルが見つかりませんでした");
                        return;
                    }

                    double thumbnailSize = _thumbnailLoader.ThumbnailSize;
                    double itemWidth = thumbnailSize + 20; // マージンを含む
                    double itemHeight = thumbnailSize + 40; // マージンを含む

                    int itemsPerRow = Math.Max(1, (int)(viewportWidth / itemWidth));

                    int firstVisibleRow = Math.Max(0, (int)(viewportTop / itemHeight));
                    int lastVisibleRow = Math.Min((int)(ThumbnailItemsControl.Items.Count / itemsPerRow),
                                                 (int)(viewportBottom / itemHeight) + 1);

                    firstIndexToLoad = firstVisibleRow * itemsPerRow;
                    lastIndexToLoad = Math.Min(ThumbnailItemsControl.Items.Count - 1,
                                             (lastVisibleRow + 1) * itemsPerRow - 1);

                    // 最下部に近いかどうかを判定
                    bool isNearBottom = scrollViewer.VerticalOffset > scrollViewer.ScrollableHeight - scrollViewer.ViewportHeight * 1.2;
                    if (isNearBottom)
                    {
                        lastIndexToLoad = ThumbnailItemsControl.Items.Count - 1;
                        Debug.WriteLine("[検出] 最下部に近いため、最後のアイテムまで読み込みます");
                    }
                }

                // 先読みバッファを追加
                int bufferSize = includePreload ? 30 : 5;
                int firstIndex = Math.Max(0, firstIndexToLoad - bufferSize);
                int lastIndex = Math.Min(ThumbnailItemsControl.Items.Count - 1, lastIndexToLoad + bufferSize);

                // 先読み時は範囲を広げる
                if (includePreload)
                {
                    // 先読み時は前後に大きなバッファを追加
                    firstIndex = Math.Max(0, firstIndex - 20);
                    lastIndex = Math.Min(ThumbnailItemsControl.Items.Count - 1, lastIndex + 20);

                    // 全体の10%以上を読み込む場合は、全体の読み込みを検討
                    int itemsToLoad = lastIndex - firstIndex + 1;
                    int totalItems = ThumbnailItemsControl.Items.Count;

                    if (itemsToLoad > totalItems * 0.1)
                    {
                        // 全体の10%以上を読み込む場合は、全体を読み込む
                        firstIndex = 0;
                        lastIndex = totalItems - 1;
                        Debug.WriteLine($"[検出] 広範囲の読み込みが必要なため、全体を読み込みます ({itemsToLoad}/{totalItems})");
                    }
                }

                // デバッグ情報
                if (!includePreload)
                {
                    Debug.WriteLine($"[スクロール中] 表示範囲読み込み: {firstIndex}～{lastIndex} (バッファ: {bufferSize})");
                }
                else
                {
                    Debug.WriteLine($"[サムネイル読み込み] 範囲: {firstIndex}～{lastIndex} (先読みバッファ: {bufferSize})");
                }

                cancellationToken.ThrowIfCancellationRequested();

                // サムネイルを読み込む
                await _thumbnailLoader.LoadMoreThumbnailsAsync(firstIndex, lastIndex, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                LogHelper.LogWarning("LoadVisibleThumbnailsAsync がキャンセルされました", "ThumbnailList");
                // キャンセルされた場合は何もしない
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"LoadVisibleThumbnailsAsync エラー: {ex.Message}");
            }
        }

        /// <summary>
        /// サムネイルがクリックされたときの処理
        /// </summary>
        private void Thumbnail_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is FileNodeModel fileNode)
            {
                ShowImageViewer(fileNode.FullPath);
                e.Handled = true;
            }
        }


        internal void ApplySettings()
        {
            // 設定を再読み込み
            _appSettings = SettingsHelper.GetSettings();

            // サムネイルサイズを設定
            ThumbnailSizeSlider.Value = _appSettings.ThumbnailSize;
            LogHelper.LogWithTimestamp($"マウスホイール倍率を更新: {_appSettings.MouseWheelMultiplier:F1}", LogHelper.Categories.UI);
        }


        // レーティングを設定する新しいメソッド
        private void SetRating(int rating)
        {
            if (!_viewModel.SelectedItems.Any()) return;

            var dbManager = ContainerLocator.Container.Resolve<DatabaseManager>();
            var eventAggregator = ContainerLocator.Container.Resolve<IEventAggregator>();

            // W/A 操作中にレーティングフィルタ適用によって SelectedItems が変更されるのでコピー
            var items = _viewModel.SelectedItems.Cast<FileNodeModel>().ToList();
            var isSameRating = items.All(item => item.Rating == rating);
            foreach (var selectedItem in items)
            {
                // 同じレーティングの場合
                if (selectedItem.Rating == rating && rating != 0)
                {
                    if (isSameRating)
                    {
                        rating = 0; // 対象のレーティングがすべて一致する時はレーティングをクリア
                    }
                    else
                    {
                        continue;   // 一致しない時はレーティングを変更しない
                    }
                }

                // レーティングを更新
                selectedItem.Rating = rating;

                // イベントを発行して他の画面に通知 (複数まとめて通知させたほうがよさそう)
                // レーティングの永続化は受信先で行う
                eventAggregator?.GetEvent<RatingChangedEvent>()?.Publish(
                    new RatingChangedEventArgs { FilePath = selectedItem.FullPath, Rating = rating });
            }
        }

        private async void PasteFilesFromClipboard()
        {
            if (Clipboard.ContainsFileDropList())
            {
                var dataObject = Clipboard.GetDataObject();
                var files = Clipboard.GetFileDropList().Cast<string>().ToList();
                if (files.Any())
                {
                    // カット操作かどうかを判定
                    bool isCut = false;

                    // より信頼性の高いカット操作の判定方法
                    if (dataObject.GetDataPresent(DataFormats.FileDrop) &&
                        dataObject.GetDataPresent("Preferred DropEffect"))
                    {
                        var memoryStream = dataObject.GetData("Preferred DropEffect") as MemoryStream;
                        if (memoryStream != null)
                        {
                            byte[] bytes = new byte[4];
                            memoryStream.Position = 0;
                            memoryStream.Read(bytes, 0, bytes.Length);
                            // DragDropEffects.Move (2) の場合はカット操作
                            isCut = BitConverter.ToInt32(bytes, 0) == 2;
                        }
                    }
                    // ファイルを処理（isCut=trueの場合は移動、falseの場合はコピー）
                    var processedFiles = await ProcessImageFiles(files, !isCut);

                    if (processedFiles.Any())
                    {
                        if (isCut)
                        {
                            // カット操作の場合はクリップボードをクリア
                            Clipboard.Clear();
                            ShowNotification((string)Application.Current.FindResource("String_Thumbnail_FilesMoved"));
                        }
                        else
                        {
                            ShowNotification((string)Application.Current.FindResource("String_Thumbnail_FilesCopied"));
                        }

                        // ペーストされたファイルの最初のファイルを選択
                        try
                        {
                            if (files.Count > 0)
                            {
                                string firstFile = files[0];
                                string fileName = Path.GetFileName(firstFile);
                                string destPath = Path.Combine(_currentFolderPath ?? "", fileName);

                                // ファイルリストが更新されるのを少し待つ
                                await Task.Delay(100);

                                // ファイルを選択
                                var fileNode = _viewModel.Items.FirstOrDefault(f => f.FullPath == destPath);
                                if (fileNode != null)
                                {
                                    SelectThumbnail(destPath);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"ペーストされたファイルの選択中にエラーが発生しました: {ex.Message}");
                        }
                    }
                }
            }
        }

        private void ShowNotification(string message, int fontSize = 24)
        {
            NotificationText.Text = message;
            NotificationText.FontSize = fontSize;
            var storyboard = (Storyboard)FindResource("ShowNotificationStoryboard");
            storyboard.Begin(Notification);
        }

        private void CopySelectedImagesToClipboard()
        {
            if (_viewModel.SelectedItems.Any())
            {
                var imagePaths = _viewModel.SelectedItems.Select(item => item.FullPath).ToArray();
                var dataObject = new DataObject();
                dataObject.SetData(DataFormats.FileDrop, imagePaths);

                // 単独の画像が選択されている場合は、画像形式でもコピー
                if (_viewModel.SelectedItems.Count == 1)
                {
                    try
                    {
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.UriSource = new Uri(imagePaths[0]);
                        bitmap.EndInit();
                        dataObject.SetImage(bitmap);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"画像形式でのコピー中にエラーが発生しました: {ex.Message}");
                    }
                }

                Clipboard.SetDataObject(dataObject, true);

                // リソースから文言を取得して通知を表示
                ShowNotification((string)Application.Current.FindResource("String_Thumbnail_ImageCopied"));
            }
        }

        private async Task ThumbnailItemsControl_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            var shortcutHandler = KeyboardShortcutHandler.Instance;

            // 修飾キーの場合はデフォルト動作を許可
            if (shortcutHandler.IsModifierKey(e.Key))
            {
                e.Handled = false;
                return;
            }

            // 全選択のショートカットの場合は、ListViewのSelectAllメソッドを呼び出す
            if (shortcutHandler.IsShortcutMatch(FuncId.SelectAll, e.Key))
            {
                ThumbnailItemsControl.SelectAll();
                e.Handled = true;
                return;
            }

            // 画像コピーのショートカットの場合は、画像をクリップボードにコピーする
            if (shortcutHandler.IsShortcutMatch(FuncId.Copy, e.Key))
            {
                CopySelectedImagesToClipboard();
                e.Handled = true;
                return;
            }

            // 画像ペーストのショートカットの場合は、クリップボードから画像をペーストする
            if (shortcutHandler.IsShortcutMatch(FuncId.Paste, e.Key))
            {
                PasteFilesFromClipboard();
                e.Handled = true;
                return;
            }

            // リネーム
            if (shortcutHandler.IsShortcutMatch(FuncId.Rename, e.Key))
            {
                if (_viewModel.SelectedItems.Count == 1)
                {
                    var selectedItem = _viewModel.SelectedItems.FirstOrDefault() as FileNodeModel;
                    if (selectedItem != null)
                    {
                        await DoRenameAsync(selectedItem); // CS4014 Fix: Await the async method call
                    }
                }
                e.Handled = true;
                return;
            }

            // その他のキーの場合は、ListViewのデフォルト動作を無効化
            if (!shortcutHandler.IsShortcutMatch(FuncId.TogglePropertyPanel, e.Key))
            {
                e.Handled = true;
            }

            // リストの先頭に移動
            if (shortcutHandler.IsShortcutMatch(FuncId.MoveToStart, e.Key))
            {
                if (ThumbnailItemsControl.Items.Count > 0)
                {
                    ThumbnailItemsControl.SelectedIndex = 0;
                    ThumbnailItemsControl.ScrollIntoView(ThumbnailItemsControl.SelectedItem);
                }
                e.Handled = true;
                return;
            }

            // リストの末尾に移動
            if (shortcutHandler.IsShortcutMatch(FuncId.MoveToEnd, e.Key))
            {
                if (ThumbnailItemsControl.Items.Count > 0)
                {
                    ThumbnailItemsControl.SelectedIndex = ThumbnailItemsControl.Items.Count - 1;
                    ThumbnailItemsControl.ScrollIntoView(ThumbnailItemsControl.SelectedItem);
                }
                e.Handled = true;
                return;
            }

            var selectedIndex = ThumbnailItemsControl.SelectedIndex;
            var panel = UIHelper.FindVisualChild<VirtualizingWrapPanel>(ThumbnailItemsControl);
            if (panel != null)
            {
                var targetItem = HandleNavigationKey(e);
                if (targetItem != null)
                {
                    ThumbnailItemsControl.SelectedItem = targetItem;
                    ThumbnailItemsControl.ScrollIntoView(targetItem);
                    return;
                }
            }

            // レーティングキーの処理
            await HandleRatingKey(e);
        }

        /// <summary>
        /// ナビゲーションキー（矢印キー、Home、End、Return）の処理を行います
        /// </summary>
        private FileNodeModel? HandleNavigationKey(KeyEventArgs e)
        {
            FileNodeModel? targetItem = null;

            if (KeyboardShortcutHandler.Instance.IsShortcutMatch(FuncId.ToggleViewer, e.Key))
            {
                if (_viewModel.SelectedItems.Any())
                {
                    ShowImageViewer(_viewModel.SelectedItems.Last().FullPath);
                    e.Handled = true;
                    return null;
                }
            }

            if (KeyboardShortcutHandler.Instance.IsShortcutMatch(FuncId.Delete, e.Key))
            {
                if (_viewModel.SelectedItems.Any())
                {
                    DeleteSelectedItems();
                    e.Handled = true;
                    return null;
                }
            }

            var shortcutHandler = KeyboardShortcutHandler.Instance;

            // 循環移動の設定を取得
            var mainWindow = Window.GetWindow(this) as MainWindow;
            bool enableCyclicNavigation = mainWindow?.EnableCyclicNavigation ?? false;

            // 方向キーの判定を行う
            bool isLeft = shortcutHandler.IsShortcutMatch(FuncId.NavigateLeft, e.Key);
            bool isRight = shortcutHandler.IsShortcutMatch(FuncId.NavigateRight, e.Key);
            bool isUp = shortcutHandler.IsShortcutMatch(FuncId.NavigateUp, e.Key);
            bool isDown = shortcutHandler.IsShortcutMatch(FuncId.NavigateDown, e.Key);

            // いずれかの方向キーが押された場合
            if (isLeft || isRight || isUp || isDown)
            {
                e.Handled = true;

                var filteredItems = _viewModel.FilteredItems.Cast<FileNodeModel>().ToList();
                if (!filteredItems.Any())
                    return null;

                var currentIndex = -1;
                var selectedItem = _viewModel.SelectedItems.LastOrDefault();
                if (selectedItem != null)
                {
                    currentIndex = filteredItems.IndexOf(selectedItem);
                }
                if (currentIndex < 0)
                    return null;

                var panel = UIHelper.FindVisualChild<VirtualizingWrapPanel>(ThumbnailItemsControl);
                if (panel == null)
                    return null;

                var itemsPerRow = GetItemsPerRow(panel);
                if (itemsPerRow <= 0)
                    return null;

                int targetIndex;
                if (isLeft || isRight)
                {
                    targetIndex = GetHorizontalNavigationIndex(currentIndex, isRight, filteredItems.Count);
                }
                else
                {
                    targetIndex = GetVerticalNavigationIndex(currentIndex, isDown, itemsPerRow, filteredItems.Count);
                }

                if (targetIndex >= 0 && targetIndex < filteredItems.Count)
                {
                    targetItem = filteredItems[targetIndex];
                }
            }

            return targetItem;
        }

        private async void DeleteSelectedItems()
        {
            var settings = ViewerSettingsHelper.LoadSettings();
            try
            {
                var selectedItems = _viewModel.SelectedItems.ToList();
                if (!selectedItems.Any()) return;

                // 複数選択時は確認ダイアログを表示
                if (selectedItems.Count > 1)
                {
                    bool moveToRecycleBin = settings.DeleteMode == FileDeleteMode.RecycleBin;

                    var messageKey = moveToRecycleBin ?
                        "String_Thumbnail_MoveToRecycleBinConfirmMessage" :
                        "String_Thumbnail_DeleteConfirmMessage";

                    var message = string.Format(
                        (string)Application.Current.FindResource(messageKey),
                        selectedItems.Count);
                    var result = MessageBox.Show(
                        message,
                        (string)Application.Current.FindResource("String_Thumbnail_DeleteConfirmTitle"),
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (result != MessageBoxResult.Yes) return;
                }

                var db = ContainerLocator.Container.Resolve<DatabaseManager>();
                var fileOp = new FileOperationHelper(db);

                foreach (var item in selectedItems)
                {
                    if (File.Exists(item.FullPath))
                    {
                        bool moveToRecycleBin = settings.DeleteMode == FileDeleteMode.RecycleBin;
                        await fileOp.DeleteFile(item.FullPath, moveToRecycleBin, false);
                        _viewModel.Items.Remove(item);

                        // 削除完了通知を表示（ごみ箱に移動した場合は専用メッセージ）
                        var message = moveToRecycleBin
                            ? (string)Application.Current.FindResource("String_Status_FileMovedToRecycleBin")
                            : (string)Application.Current.FindResource("String_Status_FileDeleted");
                        ToastNotificationHelper.ShowRelativeTo(this, message);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ファイルの削除中にエラーが発生しました: {ex.Message}");
            }
        }


        /// <summary>
        /// レーティングキーの処理を行います
        /// </summary>
        private async Task<bool> HandleRatingKey(KeyEventArgs e)
        {
            for (int i = 1; i <= 5; i++)
            {
                // レーティング設定
                if (KeyboardShortcutHandler.Instance.IsShortcutMatch(FuncId.Ratings[i], e.Key))
                {
                    SetRating(i);
                    e.Handled = true;
                    return true;
                }

                // レーティングフィルター
                var filterId = new FuncId($"filter_rating_{i}");
                if (KeyboardShortcutHandler.Instance.IsShortcutMatch(filterId, e.Key))
                {
                    _ = ApplyFilterling(i);
                    e.Handled = true;
                    return true;
                }
            }

            // レーティング解除
            if (KeyboardShortcutHandler.Instance.IsShortcutMatch(FuncId.Rating0, e.Key))
            {
                SetRating(0);
                e.Handled = true;
                return true;
            }

            // レーティング5の代替キー
            if (KeyboardShortcutHandler.Instance.IsShortcutMatch(FuncId.Rating5, e.Key))
            {
                SetRating(5);
                e.Handled = true;
                return true;
            }

            // フィルター解除
            if (KeyboardShortcutHandler.Instance.IsShortcutMatch(FuncId.FilterRating0, e.Key))
            {
                await ApplyFilterling(0);
                e.Handled = true;
                return true;
            }

            return false;
        }


        // スライダーの値が変更されたときの処理（表示の更新のみ）
        /// <summary>
        /// 選択中のサムネイルを画面内に表示します
        /// </summary>
        private async Task EnsureSelectedThumbnailVisibleAsync()
        {
            var scrollViewer = UIHelper.FindVisualChild<ScrollViewer>(ThumbnailItemsControl);
            if (scrollViewer != null && _viewModel.SelectedItems.Any())
            {
                var selectedItem = _viewModel.SelectedItems.First();

                // レイアウトの更新を待機（複数回待機して確実に完了を待つ）
                for (int i = 0; i < 3; i++)
                {
                    await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
                }

                // 選択中のアイテムを画面内に表示
                ThumbnailItemsControl.ScrollIntoView(selectedItem);

                // レイアウトの更新を待機（複数回待機して確実に完了を待つ）
                for (int i = 0; i < 3; i++)
                {
                    await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
                }

                // コンテナを取得して画面内に表示
                var container = ThumbnailItemsControl.ItemContainerGenerator.ContainerFromItem(selectedItem) as ListViewItem;
                container?.BringIntoView();

                // 最終的なレイアウトの更新を待機
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
            }
        }

        private void ThumbnailSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            try
            {
                // 初期化が完了していない場合は何もしない
                if (!_isInitialized) return;

                // 整数値として取得
                int newSize = (int)e.NewValue;

                // サイズ表示を更新（TextBlockがnullでないことを確認）
                if (ThumbnailSizeText != null)
                    ThumbnailSizeText.Text = newSize.ToString();

                // ドラッグ中でない場合（クリックでの値変更）はリサイズタイマーを開始
                if (!_isDragging)
                {
                    // 実行中のタイマーがあれば停止し、新しいタイマーを開始
                    // 最後の値変更から300ms後に実行される
                    _resizeTimer.Stop();
                    _resizeTimer.Start();
                }

                // 選択中のサムネイルを画面内に表示
                _ = Dispatcher.InvokeAsync(async () =>
                {
                    await EnsureSelectedThumbnailVisibleAsync();
                }, DispatcherPriority.Render);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"サムネイルサイズ変更中にエラーが発生しました: {ex.Message}");
            }
        }

        // スライダーのドラッグが完了したときの処理（サムネイルの再生成）
        private async void ThumbnailSizeSlider_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
        {
            if (!_isInitialized) return;

            _isDragging = false;
            await UpdateThumbnailSize();
        }

        private async Task UpdateThumbnailSize()
        {
            int newSize = (int)ThumbnailSizeSlider.Value;

            // サムネイルローダーにサイズを設定
            if (_thumbnailLoader != null)
            {
                _thumbnailLoader.ThumbnailSize = newSize;

                // サムネイル画面の再描画をリクエスト
                ThumbnailItemsControl.InvalidateMeasure();
                ThumbnailItemsControl.InvalidateVisual();

                // ScrollViewerも更新
                var scrollViewer = UIHelper.FindVisualChild<ScrollViewer>(ThumbnailItemsControl);
                if (scrollViewer != null)
                {
                    scrollViewer.InvalidateMeasure();
                    scrollViewer.InvalidateVisual();

                    // 現在表示されているサムネイルを再ロード
                    await Dispatcher.InvokeAsync(async () =>
                    {
                        await EnsureSelectedThumbnailVisibleAsync();
                        await LoadVisibleThumbnailsAsync(scrollViewer);
                    }, DispatcherPriority.Background);
                }
            }
        }

        /// <summary>
        /// 画像ファイルを指定されたフォルダにコピーまたは移動します
        /// </summary>
        private async Task<List<string>> ProcessImageFiles(List<string> files, bool isCopy)
        {
            try
            {
                if (string.IsNullOrEmpty(_currentFolderPath))
                    return new List<string>();
                var db = ContainerLocator.Container.Resolve<DatabaseManager>();
                var fileOp = new FileOperationHelper(db);

                // フォルダパスを取得
                string targetFolder = _currentFolderPath ?? "";

                // --- 進捗ダイアログと後処理コールバックを設定 ---
                List<string> processedFiles = new List<string>(); // 処理されたファイルのリスト
                string dialogTitle = isCopy ? (string)FindResource("String_Dialog_FileCopyTitle") : (string)FindResource("String_Dialog_FileMoveTitle");

                // オーナーウィンドウとして Application.Current.MainWindow を使用
                var owner = Application.Current.MainWindow;

                // owner が null または MetroWindow でない場合はエラー処理
                if (owner == null || !(owner is MahApps.Metro.Controls.MetroWindow))
                {
                    throw new InvalidOperationException("Owner window could not be determined.");
                }

                var cts = new CancellationTokenSource(); // CancellationTokenSource を生成
                (IProgress<FileOperationProgressInfo> progress, Action closeDialog) = (null, null); // 初期化

                try
                {
                    // 進捗ダイアログを表示し、progress と closeDialog を取得 (静的呼び出しに戻す)
                    (progress, closeDialog) =
                        await DialogHelper.ShowProgressDialogAsync(owner, dialogTitle, cts); // cts を渡す

                    // 実際のファイル操作は Task.Run でバックグラウンド実行
                    processedFiles = await Task.Run(async () =>
                    {
                        try
                        {
                            // Define postProcessAction lambda first
                            Action<string> postProcessAction = (processedPath) =>
                            {
                                // --- ファイルごとの後処理 (UIスレッドで実行) ---
                                _ = Application.Current.Dispatcher.InvokeAsync(async () =>
                                {
                                    if (!FileHelper.IsImageFile(processedPath)) return;
                                    var fileNode = await _thumbnailLoader.CreateFileNodeAsync(processedPath);

                                    // 既存のファイルノードをチェック
                                    if (_viewModel.Items.Any(x => x.FullPath == processedPath))
                                    {
                                        Debug.WriteLine($"File already exists in the list: {processedPath}");
                                        return;
                                    }

                                    if (fileNode != null)
                                    {
                                        // ソート順に従って適切な位置に挿入
                                        _viewModel.AddItem(fileNode);

                                        // キャッシュ更新とサムネイル生成はバックグラウンドで実行
                                        _ = Task.Run(async () =>
                                        {
                                            await _viewModel.UpdatePromptCacheAsync(processedPath);
                                            // RefreshFiltering を UI スレッドで実行
                                            await Application.Current.Dispatcher.InvokeAsync(() =>
                                            {
                                                _viewModel.RefreshFiltering();
                                            });

                                            // サムネイル生成をトリガー
                                            var filteredItems = _viewModel.FilteredItems.Cast<FileNodeModel>().ToList();
                                            var index = filteredItems.IndexOf(fileNode);
                                            if (index >= 0)
                                            {
                                                await _thumbnailLoader.CreateThumbnailAsync(index, CancellationToken.None);
                                            }
                                        });
                                    }
                                });
                                // --- 後処理ここまで ---
                            };

                            // Call ExecuteFileOperation with the defined action and token
                            return await fileOp.ExecuteFileOperation(files, targetFolder, isCopy, progress, postProcessAction, cts.Token); // Pass CancellationToken
                        }
                        catch (OperationCanceledException)
                        {
                            // Handle cancellation (e.g., log, update UI if needed)
                            System.Diagnostics.Debug.WriteLine("File operation cancelled in ThumbnailListControl.");
                            return new List<string>(); // Return empty list or handle as appropriate
                        }
                    });
                }
                finally
                {
                    // キャンセルされていなければダイアログを閉じる
                    if (cts != null && !cts.IsCancellationRequested)
                    {
                        closeDialog?.Invoke();
                    }
                    cts?.Dispose(); // Dispose CancellationTokenSource
                }


                // ペーストされたファイルの最初のファイルを選択 (processedFiles を使用)
                try
                {
                    if (processedFiles.Count > 0)
                    {
                        string firstFile = processedFiles[0];
                        string fileName = Path.GetFileName(firstFile);
                        string destPath = Path.Combine(_currentFolderPath ?? "", fileName);

                        // ファイルリストが更新されるのを少し待つ
                        await Task.Delay(100);

                        // ファイルを選択
                        var fileNode = _viewModel.Items.FirstOrDefault(f => f.FullPath == destPath);
                        if (fileNode != null)
                        {
                            SelectThumbnail(destPath);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"ペーストされたファイルの選択中にエラーが発生しました: {ex.Message}");
                }

                return processedFiles;
            }
            catch (Exception ex)
            {
                string operation = isCopy ?
                    (string)Application.Current.FindResource("String_Thumbnail_FileOperation_Copy") :
                    (string)Application.Current.FindResource("String_Thumbnail_FileOperation_Move");

                LogHelper.LogError($"[ファイル操作] {operation} 失敗: {ex.Message}");

                MessageBox.Show(
                    string.Format((string)Application.Current.FindResource("String_Thumbnail_FileOperationError"),
                    operation, ex.Message),
                    (string)Application.Current.FindResource("String_Thumbnail_FileOperationErrorTitle"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                return new List<string>();
            }
        }

        // async void に変更し、UIスレッドで非同期実行
        public async void ThumbnailItemsControl_Drop(IDropInfo e)
        {
            var dataObject = e.Data as IDataObject;
            if (dataObject == null) return;

            // サムネイル同士のドロップは無視
            if (dataObject.GetDataPresent(typeof(FileNodeModel).Name))
                return;

            // 対象外のドロップは無視
            if (!CanAcceptImageDrop(dataObject))
            {
                e.Effects = DragDropEffects.None;
                return;
            }

            // フォーマット情報をログ出力
            foreach (var format in dataObject.GetFormats())
            {
                LogHelper.LogAnalysis("Format: " + format);
            }

            // ImageDropHelperを使用してファイルを保存
            var dropHelper = new ImageDropHelper();
            var fileList = await dropHelper.ProcessImageDrop(dataObject, _currentFolderPath ?? "");

            // 仮想ファイルかどうかを判定（FileDropでない場合は仮想ファイル）
            bool isVirtual = !dataObject.GetDataPresent(DataFormats.FileDrop);
            if (fileList.Any())
            {
                bool isCopy = isVirtual || (e.KeyStates & DragDropKeyStates.ControlKey) != 0;
                var processed = await ProcessImageFiles(fileList, isCopy);
                e.Effects = processed.Any()
                    ? (isCopy ? DragDropEffects.Copy : DragDropEffects.Move)
                    : DragDropEffects.None;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
        }


        /// <summary>
        /// ドラッグ＆ドロップ操作の結果を処理します
        /// </summary>
        private async void ProcessDragDropResult(DragDropEffects result, List<FileNodeModel> draggedItems)
        {
            if (result == DragDropEffects.Move)
            {
                // 移動操作の場合、非同期でファイルの存在を確認
                await Task.Run(() =>
                {
                    var removedItems = new List<FileNodeModel>();

                    foreach (var item in draggedItems)
                    {
                        // ファイルが存在しない場合は削除リストに追加
                        if (!File.Exists(item.FullPath))
                        {
                            removedItems.Add(item);
                        }
                    }

                    // UIスレッドで一覧から削除
                    if (removedItems.Any())
                    {
                        Dispatcher.Invoke(() =>
                        {
                            foreach (var item in removedItems)
                            {
                                _viewModel.Items.Remove(item);
                            }

                            // 削除されたファイル数を通知（デバッグ用）
                            LogHelper.LogAnalysis($"{removedItems.Count}個のファイルが移動により一覧から削除されました");
                        });
                    }
                });
            }
        }

        private bool IsCyclicNavigationEnabled()
        {
            return App.Instance.EnableCyclicNavigation;
        }


        private void ShowImageViewer(string filePath)
        {
            try
            {
                if (_imageViewerWindow == null)
                {
                    // 新規作成処理
                    _imageViewerWindow = new ImageViewerWindow()
                    {
                        Parent = this
                    };

                    // イベントハンドラを設定 (async追加)
                    _imageViewerWindow.IsFullscreenChanged += async (s, e) => // async追加
                    {
                        bool isFullScreen = _imageViewerWindow?.IsFullScreen ?? false; // _imageViewerWindow のプロパティを参照
                        _thumbnailLoader?.SetFullscreenMode(isFullScreen);

                        // 全画面モードが解除された場合にサムネイルを再生成
                        if (!isFullScreen)
                        {
                            // UIスレッドで実行し、レイアウト更新を待つ
                            await Dispatcher.InvokeAsync(async () =>
                            {
                                var scrollViewer = UIHelper.FindVisualChild<ScrollViewer>(ThumbnailItemsControl);
                                if (scrollViewer != null)
                                {
                                    // レイアウト更新を待つ (念のため)
                                    await Task.Delay(100); // 少し待機
                                    await LoadVisibleThumbnailsAsync(scrollViewer, includePreload: true); // includePreload: true で広範囲を読み込む
                                }
                            }, DispatcherPriority.Background); // Background優先度で実行
                        }
                    };

                    _imageViewerWindow.Closed += async (s, e) => // async を追加
                    {
                        _thumbnailLoader?.SetFullscreenMode(false);

                        // Closed イベントハンドラ内でフルスクリーン状態を確認
                        bool wasFullScreen = _imageViewerWindow?.IsFullScreen ?? false; // Closed時点のIsFullScreenを参照

                        // インスタンスをクリア
                        _imageViewerWindow = null; // 先にクリアしておく

                        // フルスクリーンで閉じた場合にサムネイルを再生成
                        if (wasFullScreen)
                        {
                            // UIスレッドで実行し、レイアウト更新を待つ
                            await Dispatcher.InvokeAsync(async () =>
                            {
                                var scrollViewer = UIHelper.FindVisualChild<ScrollViewer>(ThumbnailItemsControl);
                                if (scrollViewer != null)
                                {
                                    // レイアウト更新を待つ (念のため)
                                    await Task.Delay(100); // 少し待機
                                    await LoadVisibleThumbnailsAsync(scrollViewer, includePreload: true); // includePreload: true で広範囲を読み込む
                                }
                            }, DispatcherPriority.Background); // Background優先度で実行
                        }
                    };

                    // 画像を読み込んでウィンドウを表示・フォーカス
                    _imageViewerWindow.LoadContentFromPath(filePath, true);
                    _imageViewerWindow.Show();
                    _imageViewerWindow.Activate(); // ウィンドウを前面に表示しアクティブにする
                    _imageViewerWindow.Focus();
                }
                else
                {
                    // 既存ウィンドウの処理
                    // 最小化されている場合は通常状態に戻す
                    if (_imageViewerWindow.WindowState == WindowState.Minimized)
                    {
                        _imageViewerWindow.WindowState = WindowState.Normal;
                    }
                    // ウィンドウをアクティブにする
                    _imageViewerWindow.Activate();
                    // 新しい画像を読み込む
                    _imageViewerWindow.LoadContentFromPath(filePath, true);
                }
            }
            catch (Exception ex)
            {
                var message = string.Format(
                    (string)Application.Current.FindResource("String_Thumbnail_ImageDisplayError"),
                    ex.Message);
                MessageBox.Show(message,
                    (string)Application.Current.FindResource("String_Thumbnail_FileOperationErrorTitle"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                LogHelper.LogError($"[画像表示] エラー: {ex.Message}");

                // エラー時はインスタンスをクリア
                _imageViewerWindow = null;
            }
        }

        public ThumbnailListViewModel GetViewModel()
        {
            return _viewModel;
        }

        /// <summary>
        /// 左右キー入力時の移動先インデックスを取得します
        /// </summary>
        private int GetHorizontalNavigationIndex(int currentIndex, bool isRight, int itemCount)
        {
            bool enableCyclicNavigation = IsCyclicNavigationEnabled();

            if (isRight)
            {
                if (currentIndex < itemCount - 1)
                {
                    // 右移動：次のインデックス
                    return currentIndex + 1;
                }
                else if (enableCyclicNavigation)
                {
                    // 循環移動が有効な場合のみ先頭へ
                    return 0;
                }
                return currentIndex;
            }
            else
            {
                if (currentIndex > 0)
                {
                    // 左移動：前のインデックス
                    return currentIndex - 1;
                }
                else if (enableCyclicNavigation)
                {
                    // 循環移動が有効な場合のみ最後へ
                    return itemCount - 1;
                }
                return currentIndex;
            }
        }

        /// <summary>
        /// 上下キー入力時の移動先インデックスを取得します
        /// </summary>
        private int GetVerticalNavigationIndex(int currentIndex, bool isDown, int itemsPerRow, int itemCount)
        {
            bool enableCyclicNavigation = IsCyclicNavigationEnabled();

            int currentRow = currentIndex / itemsPerRow;
            int currentCol = currentIndex % itemsPerRow;
            int totalRows = (itemCount + itemsPerRow - 1) / itemsPerRow;

            if (isDown)
            {
                // 下移動
                int targetIndex = currentIndex + itemsPerRow;
                if (targetIndex >= itemCount)
                {
                    if (currentRow < totalRows - 1)
                    {
                        // 最後の行に到達した場合、その行の最後のアイテムまでに制限
                        return Math.Min(targetIndex, itemCount - 1);
                    }
                    else if (enableCyclicNavigation)
                    {
                        // 循環移動が有効な場合は最初の行の同じ列へ
                        return currentCol;
                    }
                    return currentIndex;
                }
                return targetIndex;
            }
            else
            {
                // 上移動
                if (currentRow > 0)
                {
                    // 上の行の同じ列へ
                    return currentIndex - itemsPerRow;
                }
                else if (enableCyclicNavigation)
                {
                    // 循環移動が有効な場合は最後の行の同じ列へ（存在する場合のみ）
                    int lastRowIndex = (totalRows - 1) * itemsPerRow + currentCol;
                    return Math.Min(lastRowIndex, itemCount - 1);
                }
                return currentIndex;
            }
        }

        /// <summary>
        /// 選択中のサムネイルにフォーカスを設定します。
        /// </summary>
        public void FocusSelectedThumbnail()
        {
            Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    if (_viewModel.SelectedItems.LastOrDefault() is FileNodeModel selectedItem)
                    {
                        var container = ThumbnailItemsControl.ItemContainerGenerator.ContainerFromItem(selectedItem) as ListViewItem;
                        if (Window.GetWindow(this)?.IsActive == true)
                        {
                            container?.Focus();
                            System.Diagnostics.Debug.WriteLine($"[フォーカス] 選択アイテムにフォーカス: {selectedItem.FullPath}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[フォーカス] エラー: {ex.Message}");
                }
            }, DispatcherPriority.Input);
        }

        /// <summary>
        /// 指定されたフォルダのファイルノードを読み込みます
        /// </summary>
        public async Task LoadFileNodesAsync(string path, string? initialSelectedFilePath = null, FilterSettings? filterSettings = null, SortSettings? sortSettings = null, bool requestFocus = false)
        {
            try
            {
                _isLoadingFileNodes = true; // ファイルノード読み込み開始
                LogHelper.LogWithTimestamp($"[フォルダ切替] LoadFileNodes: {path}, 初期選択ファイル: {initialSelectedFilePath ?? "なし"}", LogHelper.Categories.ThumbnailLoader);

                // スクロール位置をリセット
                var scrollViewer = UIHelper.FindVisualChild<ScrollViewer>(ThumbnailItemsControl);
                if (scrollViewer != null)
                {
                    scrollViewer.ScrollToTop();
                    LogHelper.LogWithTimestamp("[フォルダ切替] LoadFileNodesでスクロール位置を先頭にリセットしました", LogHelper.Categories.ThumbnailLoader);
                }

                // 選択状態をクリア
                _viewModel.SelectedItems.Clear();
                ThumbnailItemsControl.SelectedItems.Clear();

                // サムネイル読み込みをキャンセル
                if (_thumbnailLoadCts != null)
                {
                    _thumbnailLoadCts.Cancel();
                    _thumbnailLoadCts.Dispose();
                    _thumbnailLoadCts = null;
                }

                // 新しいサムネイル読み込み用トークンを作成
                _thumbnailLoadCts = new CancellationTokenSource();

                _currentFolderPath = path;
                _viewModel.CurrentFolderPath = path; // ViewModelにフォルダパスを設定

                // 重要: UIの更新を待機
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);

                // ファイル一覧を読み込む前にスクロールイベントを一時的に無効化
                bool originalScrollingState = _isScrolling;
                _isScrolling = true; // スクロール中フラグを設定してサムネイル読み込みをスキップ

                try
                {
                    // ファイル一覧を読み込む - サムネイルトークンは渡さない
                    // ファイル一覧を読み込む
                    // ThumbnailLoaderHelper を使ってファイルノードを読み込む (イベント削除に伴いパラメータ削除)
                    await _thumbnailLoader.LoadFileNodesAsync(path);
                    _isLoadingFileNodes = false; // 読み込み完了

                    // フィルタとソート設定を適用 (読み込み後)
                    if (filterSettings != null)
                    {
                        await _viewModel.ApplyAllFilters(
                            filterSettings.Rating,
                            filterSettings.HasPrompt,
                            filterSettings.Tags,
                            filterSettings.Tags.Any(),
                            filterSettings.Extensions,
                            filterSettings.Extensions.Any()
                        );
                    }
                    if (sortSettings != null)
                    {
                        _viewModel.SortByDate = sortSettings.SortByDate;
                        _viewModel.SortAscending = sortSettings.SortAscending;
                        _viewModel.SortItems(_viewModel.SortByDate, _viewModel.SortAscending);
                    }

                    // 重要: ファイル一覧の読み込みとフィルタ/ソート適用後、UIの更新を待機
                    await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);

                    // アイテムが読み込まれたことを確認 (フィルタ適用後なのでFilteredItemsで確認)
                    if (_viewModel.FilteredItems.Cast<object>().Any())
                    {
                        LogHelper.LogWithTimestamp($"[フォルダ切替] {ThumbnailItemsControl.Items.Count}件のアイテムが読み込まれました", LogHelper.Categories.ThumbnailLoader);
                    }
                    else
                    {
                        LogHelper.LogWithTimestamp("[フォルダ切替] アイテムが読み込まれませんでした", LogHelper.Categories.ThumbnailLoader);

                        // ViewModelのFilteredItemsをチェック
                        int filteredItemsCount = 0;
                        try
                        {
                            filteredItemsCount = _viewModel.FilteredItems.Cast<FileNodeModel>().Count();
                            LogHelper.LogWithTimestamp($"[フォルダ切替] FilteredItemsの件数: {filteredItemsCount}件", LogHelper.Categories.ThumbnailLoader);
                        }
                        catch (Exception ex)
                        {
                            LogHelper.LogError($"[フォルダ切替] FilteredItemsの取得中にエラーが発生しました: {ex.Message}", ex);
                        }
                    }

                    requestFocus |= _isFirstLoad; // 初回読み込み時はフォーカスを要求

                    // --- OnFileNodesLoaded で行っていた処理をここに移動 ---
                    // 初期選択ファイルパスがある場合は選択
                    if (!string.IsNullOrEmpty(initialSelectedFilePath))
                    {
                        LogHelper.LogWithTimestamp($"[LoadFileNodesAsync] 初期選択を実行: {initialSelectedFilePath}", LogHelper.Categories.ThumbnailLoader);
                        SelectThumbnail(initialSelectedFilePath, requestFocus);
                    }
                    else
                    {
                        // 初期選択がない場合は、最初のアイテムを選択（フィルタ適用後）
                        var firstItem = _viewModel.FilteredItems.Cast<FileNodeModel>().FirstOrDefault();
                        if (firstItem != null)
                        {
                            LogHelper.LogWithTimestamp($"[LoadFileNodesAsync] 最初のアイテムを選択: {firstItem.FullPath}", LogHelper.Categories.ThumbnailLoader);
                            SelectThumbnail(firstItem.FullPath, requestFocus);
                        }
                        else
                        {
                            LogHelper.LogWithTimestamp("[LoadFileNodesAsync] 選択可能なアイテムなし", LogHelper.Categories.ThumbnailLoader);
                            // 選択可能なアイテムがない場合、選択をクリア
                            _viewModel.SelectedItems.Clear();
                            UpdateUISelection(); // UIにも反映
                        }
                    }
                    ThumbnailItemsControl.Visibility = Visibility.Visible;

                    // フォーカス要求がある場合はフォーカスを設定
                    if (requestFocus)
                    {
                        LogHelper.LogWithTimestamp("[LoadFileNodesAsync] フォーカスを設定", LogHelper.Categories.ThumbnailLoader);
                        await Dispatcher.InvokeAsync(() =>
                        {
                            ThumbnailItemsControl.Focus();
                            // 選択されているアイテムがあれば、それを表示範囲に入れる
                            if (ThumbnailItemsControl.SelectedItem != null)
                            {
                                ThumbnailItemsControl.ScrollIntoView(ThumbnailItemsControl.SelectedItem);
                            }
                        }, DispatcherPriority.Render);
                    }
                    // --- ここまで移動 ---

                }
                finally
                {
                    ThumbnailItemsControl.Visibility = Visibility.Visible;
                    // スクロールイベントを元の状態に戻す
                    _isScrolling = originalScrollingState;
                    _isFirstLoad = false; // 初回読み込みフラグをリセット
                }
            }
            catch (Exception ex)
            {
                LogHelper.LogError($"[フォルダ切替] LoadFileNodesでエラーが発生しました: {ex.Message}", ex);
            }
            finally
            {
                _isLoadingFileNodes = false; // 読み込み完了または例外発生時にフラグをリセット
                // _processingFolderPath = null; // OnFileNodesLoaded 削除により不要
            }
        }



        public void DisplayGeneratedItemsInfo(ListView listView)
        {
            int totalItems = listView.Items.Count;
            int generatedItems = GetGeneratedItemsCount(listView);

            Debug.WriteLine($"全アイテム数: {totalItems}");
            Debug.WriteLine($"生成されたアイテム数: {generatedItems}");
            Debug.WriteLine($"仮想化率: {(1 - (double)generatedItems / totalItems) * 100:F2}%");
        }

        /// <summary>
        /// Gets the number of items that have been generated (realized) by the virtualization system
        /// </summary>
        private int GetGeneratedItemsCount(ListView listView)
        {
            int count = 0;

            if (listView == null)
                return 0;

            for (int i = 0; i < listView.Items.Count; i++)
            {
                var container = listView.ItemContainerGenerator.ContainerFromIndex(i);
                if (container != null)
                {
                    count++;
                }
            }

            return count;
        }

        private async void RatingFilter_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button ||
                !int.TryParse(button.Tag?.ToString(), out int rating))
                return;

            // 同じレーティングが選択された場合はフィルターを解除
            if (rating == _viewModel.CurrentRatingFilter && rating != 0)
            {
                rating = 0;
            }
            await ApplyFilterling(rating);
        }
        private async void ClearFilter_Click(object sender, RoutedEventArgs e)
        {
            await ApplyFilterling(0);
        }

        private async Task ApplyFilterling(int rating)
        {
            try
            {
                // 現在のフォーカスアイテムを保存
                var focusedItem = _viewModel.SelectedItems.LastOrDefault();
                var focusedPath = focusedItem?.FullPath;

                // フィルタを適用（ViewModelが状態を管理）
                await _viewModel.ApplyAllFilters(rating, _isPromptFilterEnabled, _currentTagFilters, _isTagFilterEnabled, _currentExtensionFilters, _isExtensionFilterEnabled); // 引数追加

                // フィルター変更イベントを発行して他のコントロールに通知
                _eventAggregator.GetEvent<FilterChangedEvent>().Publish(
                    new FilterChangedEventArgsBuilder(CONTROL_ID)
                        .WithRatingFilter(rating)
                        .Build());

                // フィルタ後のアイテムリスト
                var filteredItems = _viewModel.FilteredItems.Cast<FileNodeModel>().ToList();

                // 選択するアイテムを決定
                FileNodeModel? itemToSelect = null;
                if (focusedPath != null)
                {
                    // 前回フォーカスされていたアイテムがフィルタ後も存在する場合はそれを選択
                    itemToSelect = filteredItems.FirstOrDefault(fi => fi.FullPath == focusedPath);
                }

                // フォーカスアイテムが見つからない場合は先頭のアイテムを選択
                if (itemToSelect == null && filteredItems.Any())
                {
                    itemToSelect = filteredItems.First();
                }

                // 選択を更新
                _viewModel.SelectedItems.Clear();
                if (itemToSelect != null)
                {
                    _viewModel.SelectedItems.Add(itemToSelect);
                    await Dispatcher.InvokeAsync(async () =>
                    {
                        ThumbnailItemsControl.ScrollIntoView(itemToSelect);
                        // ウィンドウがアクティブな場合のみフォーカス処理を実行
                        if (Window.GetWindow(this)?.IsActive == true)
                        {
                            var container = ThumbnailItemsControl.ItemContainerGenerator.ContainerFromItem(itemToSelect) as ListViewItem;
                            container?.Focus();
                        }

                        // サムネイルの再生成
                        var scrollViewer = UIHelper.FindVisualChild<ScrollViewer>(ThumbnailItemsControl);
                        if (scrollViewer != null)
                        {
                            await LoadVisibleThumbnailsAsync(scrollViewer);
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"フィルタークリア中にエラーが発生: {ex.Message}");
            }
        }

        private void OnLanguageChanged()
        {
            // 言語リソースの反映をまつ
            Task.Run(() =>
            {
                // フィルターボタンのテキストを更新
                Dispatcher.Invoke(() =>
                {
                    // ソート種類の文言を更新
                    SortTypeText.Text = _isSortByDate ?
                        (string)Application.Current.FindResource("String_Thumbnail_SortByDate") :
                        (string)Application.Current.FindResource("String_Thumbnail_SortByName");
                    SortDirectionText.Text = _isSortAscending ?
                        (string)Application.Current.FindResource("String_Thumbnail_SortAscending") :
                        (string)Application.Current.FindResource("String_Thumbnail_SortDescending");
                });
            });
        }

        private async void OnSortOrderChanged(SortOrderChangedEventArgs args)
        {
            Debug.WriteLine($"[ソート順変更] ソート順: {(args.IsAscending ? "昇順" : "降順")}, ソート基準: {(args.IsByDate ? "日付" : "名前")}");
            try
            {
                _isSortByDate = args.IsByDate;
                _isSortAscending = args.IsAscending;

                // ローダーの設定を更新
                _thumbnailLoader.SortByDate = _isSortByDate;
                _thumbnailLoader.SortAscending = _isSortAscending;

                // 設定を保存
                _appSettings.SortByDate = _isSortByDate;
                _appSettings.SortAscending = _isSortAscending;
                SettingsHelper.SaveSettings(_appSettings);

                // UI更新
                SortTypeText.Text = _isSortByDate ?
                    (string)Application.Current.FindResource("String_Thumbnail_SortByDate") :
                    (string)Application.Current.FindResource("String_Thumbnail_SortByName");
                SortDirectionText.Text = _isSortAscending ?
                    (string)Application.Current.FindResource("String_Thumbnail_SortAscending") :
                    (string)Application.Current.FindResource("String_Thumbnail_SortDescending");

                // フィルタリングが適用されている場合も考慮してソート
                await SortThumbnailAsync(_isSortByDate, _isSortAscending, true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in OnSortOrderChanged: {ex.Message}");
            }
        }

        private async void OnRatingChanged(RatingChangedEventArgs args)
        {
            var fileNode = _viewModel.Items.FirstOrDefault(fn => fn.FullPath == args.FilePath);
            if (fileNode != null)
            {
                fileNode.Rating = args.Rating;
                // フィルタが設定されている場合のみフィルタを再適用
                if (_viewModel.CurrentRatingFilter > 0)
                {
                    await ApplyFilterling(_viewModel.CurrentRatingFilter);
                    return;
                }

                try
                {
                    var items = _viewModel.SelectedItems.Cast<FileNodeModel>().ToList();

                    // 選択中のファイルのレーティングが変更された場合はアニメーション実行
                    foreach (var selectedItem in items)
                    {
                        if (selectedItem.FullPath == args.FilePath)
                        {
                            // UIスレッドで実行
                            await Dispatcher.InvokeAsync(() =>
                            {
                                try
                                {
                                    // ウィンドウがアクティブな場合のみフォーカス処理を実行
                                    if (Window.GetWindow(this)?.IsActive == true)
                                    {
                                        // 選択中アイテムのコンテナを取得
                                        var container = ThumbnailItemsControl.ItemContainerGenerator.ContainerFromItem(selectedItem) as ListViewItem;
                                        if (container != null)
                                        {
                                            // DataTemplateの中のRatingStarControlを検索
                                            var starControl = UIHelper.FindVisualChild<RatingStarControl>(container);
                                            if (starControl != null)
                                            {
                                                // 明示的にアニメーションを実行
                                                starControl.PlayAnimation();
                                            }
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Debug.WriteLine($"アニメーション実行中にエラー: {ex.Message}");
                                }
                            }, DispatcherPriority.Background);
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogHelper.LogError($"Error in OnRatingChanged: {ex.Message}");
                }
            }
        }

        // DataTemplate内の特定の名前を持つ要素を検索するヘルパーメソッド
        private T? FindElementInTemplate<T>(FrameworkElement container, string elementName) where T : FrameworkElement
        {
            if (container == null)
                return null;

            T? result = null;

            // コンテナ内のすべての子要素を検索
            var childCount = VisualTreeHelper.GetChildrenCount(container);
            for (int i = 0; i < childCount; i++)
            {
                var child = VisualTreeHelper.GetChild(container, i) as DependencyObject;
                if (child == null) continue;

                // 目的の型と名前に一致する要素を検索
                if (child is T element && (element.Name == elementName || string.IsNullOrEmpty(elementName)))
                {
                    return element;
                }

                // 再帰的に子要素を検索
                if (child is FrameworkElement frameworkElement)
                {
                    result = FindElementInTemplate<T>(frameworkElement, elementName);
                    if (result != null)
                        return result;
                }
            }

            return result;
        }


        private async void SortToggle_Click(object sender, RoutedEventArgs e)
        {
            _isSortAscending = !_isSortAscending;
            _appSettings.SortAscending = _isSortAscending;
            _thumbnailLoader.SortAscending = _isSortAscending;
            _viewModel.SortAscending = _isSortAscending;
            SettingsHelper.SaveSettings(_appSettings);
            SortDirectionText.Text = _isSortAscending ?
                (string)Application.Current.FindResource("String_Thumbnail_SortAscending") :
                (string)Application.Current.FindResource("String_Thumbnail_SortDescending");

            // フィルタリングが適用されている場合も考慮してソート
            await SortThumbnailAsync(_isSortByDate, _isSortAscending, true);

            // サムネイルの再生成
            var scrollViewer = UIHelper.FindVisualChild<ScrollViewer>(ThumbnailItemsControl);
            if (scrollViewer != null)
            {
                await LoadVisibleThumbnailsAsync(scrollViewer);
            }

            // ソート順変更イベントを発行
            _eventAggregator.GetEvent<SortOrderChangedEvent>().Publish(
                new SortOrderChangedEventArgs(_isSortByDate, _isSortAscending, CONTROL_ID));
        }

        private async void SortTypeToggle_Click(object sender, RoutedEventArgs e)
        {
            _isSortByDate = !_isSortByDate;
            _appSettings.SortByDate = _isSortByDate;
            _thumbnailLoader.SortByDate = _isSortByDate;
            _viewModel.SortByDate = _isSortByDate;
            SettingsHelper.SaveSettings(_appSettings);
            SortTypeText.Text = _isSortByDate ?
                (string)Application.Current.FindResource("String_Thumbnail_SortByDate") :
                (string)Application.Current.FindResource("String_Thumbnail_SortByName");

            // フィルタリングが適用されている場合も考慮してソート
            await SortThumbnailAsync(_isSortByDate, _isSortAscending, true);

            // サムネイルの再生成
            var scrollViewer = UIHelper.FindVisualChild<ScrollViewer>(ThumbnailItemsControl);
            if (scrollViewer != null)
            {
                await LoadVisibleThumbnailsAsync(scrollViewer);
            }

            // ソート順変更イベントを発行
            _eventAggregator.GetEvent<SortOrderChangedEvent>().Publish(
                new SortOrderChangedEventArgs(_isSortByDate, _isSortAscending, CONTROL_ID));
        }

        /// <summary>
        /// VirtualizingWrapPanelの実際のレイアウトから列数を取得します
        /// </summary>
        /// <summary>
        /// ウィンドウサイズ変更時に表示範囲のサムネイルを再生成します
        /// </summary>
        private async Task OnWindowSizeChanged(ScrollViewer scrollViewer)
        {
            try
            {
                // レイアウトの更新を待機
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);

                // 表示範囲のサムネイルを再ロード
                await LoadVisibleThumbnailsAsync(scrollViewer);

                // 選択中のサムネイルを画面内に表示
                _ = Dispatcher.InvokeAsync(async () =>
                {
                    await EnsureSelectedThumbnailVisibleAsync();
                }, DispatcherPriority.Render);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ウィンドウサイズ変更時のサムネイル再生成エラー: {ex.Message}");
            }
        }

        private int GetItemsPerRow(VirtualizingWrapPanel panel)
        {
            try
            {
                if (ThumbnailItemsControl.Items.Count == 0 || panel == null)
                    return 1;

                var scrollViewer = UIHelper.FindVisualChild<ScrollViewer>(ThumbnailItemsControl);
                if (scrollViewer == null)
                    return 1;

                double? firstRowY = null;
                int itemsInFirstRow = 0;
                double lastX = double.MinValue;
                bool foundFirstRow = false;

                // アイテムが表示されている範囲を計算
                var panelToScrollViewer = panel.TransformToAncestor(scrollViewer);
                var panelPoint = panelToScrollViewer.Transform(new Point(0, 0));
                var panelRect = new Rect(panelPoint, panel.RenderSize);
                var viewport = new Rect(new Point(0, 0), new Size(scrollViewer.ViewportWidth, scrollViewer.ViewportHeight));

                // 表示範囲内のアイテムを探す
                for (int i = 0; i < ThumbnailItemsControl.Items.Count; i++)
                {
                    var container = ThumbnailItemsControl.ItemContainerGenerator.ContainerFromIndex(i) as ListViewItem;
                    if (container != null)
                    {
                        // コンテナの位置をパネル座標系で取得
                        var containerToPanelTransform = container.TransformToAncestor(panel);
                        var containerPoint = containerToPanelTransform.Transform(new Point(0, 0));
                        var containerRect = new Rect(containerPoint, container.RenderSize);

                        // スクロールビューア座標系に変換
                        var containerToScrollViewerTransform = container.TransformToAncestor(scrollViewer);
                        var containerInScrollViewer = containerToScrollViewerTransform.Transform(new Point(0, 0));

                        // スクロールビューアの表示範囲内かチェック
                        if (containerInScrollViewer.Y >= -container.ActualHeight &&
                            containerInScrollViewer.Y <= viewport.Height)
                        {
                            if (!firstRowY.HasValue)
                            {
                                firstRowY = containerPoint.Y;
                                itemsInFirstRow = 1;
                                lastX = containerPoint.X;
                            }
                            else if (Math.Abs(containerPoint.Y - firstRowY.Value) <= 1)
                            {
                                if (containerPoint.X > lastX + 1)
                                {
                                    itemsInFirstRow++;
                                    lastX = containerPoint.X;
                                }
                            }
                            else if (!foundFirstRow)
                            {
                                foundFirstRow = true;
                            }
                        }
                    }
                }

                if (itemsInFirstRow > 0)
                {
                    return itemsInFirstRow;
                }

                // 見つからなかった場合は、実際のサイズから計算
                var itemWidth = ThumbnailSizeSlider.Value + 20;
                var columns = Math.Max(1, (int)(panel.ActualWidth / itemWidth));
                Debug.WriteLine($"Using calculated columns: {columns} (Width={panel.ActualWidth:F2}, ItemWidth={itemWidth:F2})");
                return columns;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in GetItemsPerRow: {ex.Message}");
                return 1;
            }
        }

        /// <summary>
        /// スクロールリクエストを処理します
        /// </summary>
        private void OnScrollToItemRequested(object? sender, ScrollToItemRequestEventArgs e)
        {
            if (e.TargetItem != null)
            {
                ThumbnailItemsControl.ScrollIntoView(e.TargetItem);
                ThumbnailItemsControl.SelectedItem = e.TargetItem;
                FocusSelectedThumbnail();
            }
        }


        // 表示範囲をログに出力するメソッド
        private void LogVisibleRange(ScrollViewer scrollViewer)
        {
            try
            {
                if (ThumbnailItemsControl == null || ThumbnailItemsControl.Items.Count == 0)
                    return;

                // スクロール位置から表示範囲を計算
                double viewportTop = scrollViewer.VerticalOffset;
                double viewportBottom = viewportTop + scrollViewer.ViewportHeight;
                double estimatedItemHeight = _thumbnailLoader.ThumbnailSize + 40; // マージンを含む

                // 表示範囲内のアイテムのインデックスを推定
                int estimatedItemsPerRow = GetItemsPerRow(UIHelper.FindVisualChild<VirtualizingWrapPanel>(ThumbnailItemsControl));
                if (estimatedItemsPerRow <= 0) estimatedItemsPerRow = 1;

                int estimatedFirstRow = Math.Max(0, (int)(viewportTop / estimatedItemHeight));
                int estimatedLastRow = Math.Min((int)(ThumbnailItemsControl.Items.Count / estimatedItemsPerRow),
                                               (int)(viewportBottom / estimatedItemHeight) + 1);

                int firstVisibleIndex = estimatedFirstRow * estimatedItemsPerRow;
                int lastVisibleIndex = Math.Min(ThumbnailItemsControl.Items.Count - 1,
                                               (estimatedLastRow + 1) * estimatedItemsPerRow - 1);

                // 最下部に近い場合の特別処理
                bool isNearBottom = scrollViewer.VerticalOffset > scrollViewer.ScrollableHeight - scrollViewer.ViewportHeight * 1.5;
                if (isNearBottom)
                {
                    // 最下部に近い場合は、最後のアイテムまで確実に含める
                    lastVisibleIndex = ThumbnailItemsControl.Items.Count - 1;
                }

                int bufferSize = 30;
                int preloadFirstIndex = Math.Max(0, firstVisibleIndex - bufferSize * estimatedItemsPerRow);
                int preloadLastIndex = ThumbnailItemsControl.Items.Count - 1; // 最後まで確実に含める

                // ログ出力
                LogHelper.LogScrollTracking($"[スクロール停止] 表示範囲: {firstVisibleIndex}～{lastVisibleIndex} (全{ThumbnailItemsControl.Items.Count}件)");
                LogHelper.LogScrollTracking($"[スクロール停止] 読み込み範囲: {preloadFirstIndex}～{preloadLastIndex} (先読みバッファ: {bufferSize})");
                LogHelper.LogScrollTracking($"[スクロール停止] 推定行: {estimatedFirstRow}～{estimatedLastRow}, 1行あたり: {estimatedItemsPerRow}項目");

                if (isNearBottom)
                {
                    LogHelper.LogScrollTracking("[スクロール停止] 最下部に近いため、最後のアイテムまで確実に読み込みます");
                }
            }
            catch (Exception ex)
            {
                LogHelper.LogError($"[ERROR] LogVisibleRange エラー: {ex.Message}");
            }
        }

        // FilteredItemsのヘルパーメソッドを追加
        private List<FileNodeModel> GetFilteredItemsList()
        {
            return _viewModel.FilteredItems.Cast<FileNodeModel>().ToList();
        }

        // 特定のインデックスのアイテムを取得するヘルパーメソッド
        private FileNodeModel? GetFilteredItemAt(int index)
        {
            var items = GetFilteredItemsList();
            if (index >= 0 && index < items.Count)
            {
                return items[index];
            }
            return null;
        }

        // フィルタリングされたアイテムの数を取得するヘルパーメソッド
        private int GetFilteredItemsCount()
        {
            return _viewModel.FilteredItems.Cast<FileNodeModel>().Count();
        }

        /// <summary>
        /// スクロールビューアの表示範囲内のアイテムインデックスを取得します。
        /// </summary>
        private (int firstVisibleIndex, int lastVisibleIndex) GetVisibleIndexRange(ScrollViewer scrollViewer)
        {
            // 可視性の直接チェックで表示範囲を取得
            int firstVisibleIndex = 0;
            int lastVisibleIndex = 0;
            bool foundFirst = false;

            // コンテナの可視性をチェックして表示範囲を特定
            for (int i = 0; i < ThumbnailItemsControl.Items.Count; i++)
            {
                var container = ThumbnailItemsControl.ItemContainerGenerator.ContainerFromIndex(i) as ListViewItem;
                if (container != null && container.IsVisible)
                {
                    if (!foundFirst)
                    {
                        firstVisibleIndex = i;
                        foundFirst = true;
                    }
                    lastVisibleIndex = i;
                }
                else if (foundFirst)
                {
                    // 可視範囲を超えたら終了
                    break;
                }
            }

            // 直接チェックで見つからなかった場合は計算方式にフォールバック
            if (!foundFirst)
            {
                // 位置ベースでの計算による表示範囲の取得
                double viewportTop = scrollViewer.VerticalOffset;
                double viewportBottom = viewportTop + scrollViewer.ViewportHeight;

                var panel = UIHelper.FindVisualChild<VirtualizingWrapPanel>(ThumbnailItemsControl);
                int itemsPerRow = GetItemsPerRow(panel);
                if (itemsPerRow <= 0) itemsPerRow = 1;

                double itemHeight = _thumbnailLoader.ThumbnailSize + 40;
                int firstVisibleRow = Math.Max(0, (int)(viewportTop / itemHeight));
                int lastVisibleRow = Math.Min((int)(ThumbnailItemsControl.Items.Count / itemsPerRow),
                                          (int)(viewportBottom / itemHeight) + 1);

                firstVisibleIndex = firstVisibleRow * itemsPerRow;
                lastVisibleIndex = Math.Min(ThumbnailItemsControl.Items.Count - 1,
                                        (lastVisibleRow + 1) * itemsPerRow - 1);

                LogHelper.LogWithTimestamp($"[表示範囲] 計算方式にフォールバック: {firstVisibleIndex}～{lastVisibleIndex}",
                    LogHelper.Categories.ThumbnailQueue);
            }
            else
            {
                LogHelper.LogWithTimestamp($"[表示範囲] 可視性チェックで検出: {firstVisibleIndex}～{lastVisibleIndex}",
                    LogHelper.Categories.ThumbnailQueue);
            }

            return (firstVisibleIndex, lastVisibleIndex);
        }

        /// <summary>
        /// スクロールビューアの表示範囲内のアイテムインデックスをバッファ付きで取得します。
        /// </summary>
        private (int firstVisibleIndex, int lastVisibleIndex) GetVisibleRangeWithBuffer(ScrollViewer scrollViewer, int bufferSize)
        {
            var (first, last) = GetVisibleIndexRange(scrollViewer);

            // バッファを追加
            int firstWithBuffer = Math.Max(0, first - bufferSize);
            int lastWithBuffer = Math.Min(ThumbnailItemsControl.Items.Count - 1, last + bufferSize);

            return (firstWithBuffer, lastWithBuffer);
        }

        private void LogWithTimestamp(string message)
        {
            string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            System.Diagnostics.Debug.WriteLine($"[{timestamp}] {message}");
        }

        private async void ScrollStopTimer_Tick(object? sender, EventArgs e)
        {
            _scrollStopTimer.Stop();

            try
            {
                // スクロール停止時の処理
                _isScrolling = false;

                // サムネイル処理キューのスクロール状態を通常に戻す
                _thumbnailLoader.SetScrollType(ScrollType.None);

                // アイテムがない場合は何もしない
                if (ThumbnailItemsControl == null || ThumbnailItemsControl.Items.Count == 0)
                {
                    LogHelper.LogScrollTracking("[スクロール停止] アイテムが存在しないため処理をスキップします");
                    return;
                }

                // ScrollViewerを取得
                var scrollViewer = UIHelper.FindVisualChild<ScrollViewer>(ThumbnailItemsControl);
                if (scrollViewer == null) return;

                // 表示範囲を取得（大きめのバッファを使用）
                (int firstIndex, int lastIndex) = GetVisibleRangeWithBuffer(scrollViewer, bufferSize: 30);

                // 表示範囲のサムネイルを高優先度で読み込む
                if (_thumbnailLoadCts != null)
                {
                    _thumbnailLoadCts.Cancel();
                    _thumbnailLoadCts.Dispose();
                }

                _thumbnailLoadCts = new CancellationTokenSource();
                await _thumbnailLoader.LoadMoreThumbnailsAsync(firstIndex, lastIndex, _thumbnailLoadCts.Token, isHighPriority: true);

                LogHelper.LogScrollTracking($"[スクロール停止] 表示範囲のサムネイルを高優先度で読み込み完了: {firstIndex}～{lastIndex}");
            }
            catch (OperationCanceledException)
            {
                // キャンセルは正常な動作として扱う
                LogHelper.LogScrollTracking("[スクロール停止] サムネイル読み込みがキャンセルされました");
            }
            catch (Exception ex)
            {
                LogHelper.LogError($"[スクロール停止] エラーが発生しました: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 指定されたインデックスのサムネイルを生成します
        /// </summary>
        public Task CreateThumbnailAsync(int index, CancellationToken cancellationToken)
        {
            // ThumbnailLoaderHelperのCreateThumbnailAsyncメソッドを呼び出す
            return _thumbnailLoader.CreateThumbnailAsync(index, cancellationToken);
        }

        // スクロールバードラッグ開始時（高速スクロール用）
        private void ScrollBar_DragStarted(object sender, System.Windows.Controls.Primitives.DragStartedEventArgs e)
        {
            LogHelper.LogWithTimestamp("[スクロール状態] スクロールバードラッグ開始", LogHelper.Categories.ThumbnailQueue);

            // サムネイル処理キューにスクロールバードラッグ状態を設定
            _thumbnailLoader.SetScrollType(ScrollType.ScrollBar);

            // スクロール中フラグを設定
            _isScrolling = true;

            // スクロールタイマーをリセット
            _scrollStopTimer.Stop();
            _scrollStopTimer.Start();
        }

        // スクロールバードラッグ完了時
        private void ScrollBar_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
        {
            LogHelper.LogWithTimestamp("[スクロール状態] スクロールバードラッグ完了", LogHelper.Categories.ThumbnailQueue);

            // サムネイル処理キューのスクロール状態を通常に戻す
            _thumbnailLoader.SetScrollType(ScrollType.None);

            // スクロール停止処理を開始
            _scrollStopTimer.Stop();
            _scrollStopTimer.Start();
        }

        // ホイールスクロール時（低速スクロール用）
        private void OnMouseWheel(object sender, MouseWheelEventArgs e)
        {
            LogHelper.LogWithTimestamp("[スクロール状態] マウスホイールスクロール開始", LogHelper.Categories.ThumbnailQueue);

            // サムネイル処理キューにホイールスクロール状態を設定
            _thumbnailLoader.SetScrollType(ScrollType.Wheel);

            // スクロール中フラグを設定
            _isScrolling = true;

            // スクロールタイマーをリセット
            _scrollStopTimer.Stop();
            _scrollStopTimer.Start();
        }


        private async void ReloadThumbnailsButton_Click(object sender, RoutedEventArgs e)
        {
            if (_thumbnailLoader == null) return;

            // すべてのサムネイルの状態をリセット
            _thumbnailLoader.ResetAllThumbnailStatus();

            // 現在表示されている範囲のサムネイルを再読み込み
            var scrollViewer = UIHelper.FindVisualChild<ScrollViewer>(ThumbnailItemsControl);
            if (scrollViewer != null)
            {
                // 既存の読み込み処理をキャンセル
                if (_thumbnailLoadCts != null)
                {
                    _thumbnailLoadCts.Cancel();
                    _thumbnailLoadCts.Dispose();
                }
                _thumbnailLoadCts = new CancellationTokenSource();

                // 表示範囲のサムネイルを読み込む (高優先度で)
                (int firstIndex, int lastIndex) = GetVisibleRangeWithBuffer(scrollViewer, bufferSize: 5); // 小さめのバッファでまず表示
                await _thumbnailLoader.LoadMoreThumbnailsAsync(firstIndex, lastIndex, _thumbnailLoadCts.Token, isHighPriority: true);

                // 少し待ってから広範囲を読み込む (オプション)
                _ = Task.Delay(500, _thumbnailLoadCts.Token).ContinueWith(async _ =>
                {
                    if (!_thumbnailLoadCts.IsCancellationRequested)
                    {
                        (int preloadFirst, int preloadLast) = GetVisibleRangeWithBuffer(scrollViewer, bufferSize: 30); // 広めのバッファ
                        await _thumbnailLoader.LoadMoreThumbnailsAsync(preloadFirst, preloadLast, _thumbnailLoadCts.Token, isHighPriority: false);
                    }
                }, TaskContinuationOptions.OnlyOnRanToCompletion);
            }
        }

        /// <summary>
        /// SelectedTabChangedEvent を受信したときの処理 (Control側)
        /// OnMcpFolderSelected と同様のロジックでフォルダを読み込む。
        /// </summary>
        private async void OnSelectedTabChanged(SelectedTabChangedEventArgs args)
        {
            var newState = args?.NewTabState;
            if (newState == null || string.IsNullOrEmpty(newState.FolderPath))
            {
                // 選択されたタブがない、またはフォルダパスがない場合
                _viewModel.ClearItems(); // ViewModelのアイテムをクリア
                _currentFolderPath = null;
                if (_fileSystemMonitor.IsMonitoring)
                {
                    _fileSystemMonitor.StopMonitoring();
                }
                Debug.WriteLine("[タブ変更] 新しいタブの状態が無効です。クリアします。");
                return;
            }

            string folderPath = newState.FolderPath;
            string? selectedFilePath = newState.SelectedItemPath;
            var sortSettings = newState.SortSettings;
            var filterSettings = newState.FilterSettings;

            Debug.WriteLine($"[タブ変更] フォルダ: {folderPath}, 選択: {selectedFilePath ?? "なし"}, ソート: {sortSettings.SortByDate}/{sortSettings.SortAscending}, フィルタ: R{filterSettings.Rating}/P{filterSettings.HasPrompt}/T{filterSettings.Tags.Count}/E{filterSettings.Extensions.Count}");

            // 同じフォルダが既に開かれている場合はリロードしない
            // FilterChangedEventも発行してUIを更新

            // タブごとに設定が異なるため同じパスでもスキップしない

            // --- フォルダ変更処理 ---
            ThumbnailItemsControl.Visibility = Visibility.Hidden; // ちらつき防止

            // 1. ViewModelの状態をクリア（フィルタ、アイテム、選択）
            _viewModel.ClearAllFilters();
            _viewModel.ClearItems();
            _currentTagFilters.Clear(); // Control側のフィルタ状態もクリア
            _isTagFilterEnabled = false;
            _isPromptFilterEnabled = false;
            _currentExtensionFilters.Clear();
            _isExtensionFilterEnabled = false;

            // 2. ファイルシステム監視を更新
            if (_fileSystemMonitor.IsMonitoring)
            {
                _fileSystemMonitor.StopMonitoring();
            }
            _fileSystemMonitor.StartMonitoring(folderPath);

            // 3. ViewModelに新しい状態を適用 (フィルタとソート)
            _viewModel.SortByDate = sortSettings.SortByDate;
            _viewModel.SortAscending = sortSettings.SortAscending;

            // 4. ファイルノードとサムネイルをロード (選択ファイルパスとフィルタ/ソート設定を渡す)
            // LoadFileNodesAsync の引数を変更する必要がある
            await LoadFileNodesAsync(folderPath, selectedFilePath, filterSettings, sortSettings, requestFocus: true);

            // 6. UIを表示 (OnFileNodesLoaded で表示される想定)
            // FilterChangedEventも発行してUIを更新 (全更新として発行)
            _eventAggregator.GetEvent<FilterChangedEvent>().Publish(
                new FilterChangedEventArgsBuilder(CONTROL_ID) // SourceId は "ThumbnailList"
                .SetFullUpdate(filterSettings) // filterSettings オブジェクト全体を渡す
                .Build());

            // 7. ソート/フィルタ状態をUIに反映させるイベントを発行
            _eventAggregator.GetEvent<SortOrderChangedEvent>().Publish(
                new SortOrderChangedEventArgs(_viewModel.SortByDate, _viewModel.SortAscending, CONTROL_ID));
        }

        // Tabs コレクション変更時のイベントハンドラ
        private void Tabs_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            // UI スレッドで実行
            Dispatcher.Invoke(() =>
            {
                UpdateToolbarState();
            });
        }

        // ツールバーの表示状態を更新するメソッド
        private void UpdateToolbarState()
        {
            IsToolbarVisible = _mainWindowViewModel.Tabs.Count > 0;
            // Debug.WriteLine($"Toolbar Enabled: {IsToolbarEnabled} (Tab Count: {_mainWindowViewModel.Tabs.Count})");
        }

    }
}

