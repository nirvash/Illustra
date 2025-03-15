using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Data;
using Illustra.Models;
using Illustra.Services;

namespace Illustra.ViewModels
{
    /// <summary>
    /// ソート方向を表す列挙型
    /// </summary>
    public enum SortDirection
    {
        /// <summary>
        /// 昇順
        /// </summary>
        Ascending,

        /// <summary>
        /// 降順
        /// </summary>
        Descending
    }

    /// <summary>
    /// 画像表示のためのViewModelクラス
    /// </summary>
    public class ImageViewModel : INotifyPropertyChanged
    {
        private readonly ImageCollectionModel _imageCollection;
        private readonly UpdateSequenceManager _updateManager;
        private readonly OperationCache _operationCache;

        private bool _isLoading;
        private bool _isSorting;
        private bool _isFiltering;
        private int _currentRatingFilter;
        private bool _sortByDate;
        private bool _sortAscending;
        private string? _statusMessage;
        private int _progressValue;
        private SortDirection _sortDirection = SortDirection.Ascending;
        private int _sortType = 1; // 0: 名前, 1: 日付, 2: 評価
        private int _ratingFilter = 0;

        /// <summary>
        /// 表示用の画像アイテムコレクション
        /// </summary>
        public ObservableCollection<FileNodeModel> DisplayItems { get; } = new();

        /// <summary>
        /// 読み込み中かどうか
        /// </summary>
        public bool IsLoading
        {
            get => _isLoading;
            private set
            {
                if (_isLoading != value)
                {
                    _isLoading = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// ソート中かどうか
        /// </summary>
        public bool IsSorting
        {
            get => _isSorting;
            private set
            {
                if (_isSorting != value)
                {
                    _isSorting = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// フィルタリング中かどうか
        /// </summary>
        public bool IsFiltering
        {
            get => _isFiltering;
            private set
            {
                if (_isFiltering != value)
                {
                    _isFiltering = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// 現在のレーティングフィルタ値
        /// </summary>
        public int CurrentRatingFilter
        {
            get => _currentRatingFilter;
            set
            {
                if (_currentRatingFilter != value)
                {
                    _currentRatingFilter = value;
                    OnPropertyChanged();
                    _ = ApplyFilterAsync(value);
                }
            }
        }

        /// <summary>
        /// 日付順でソートするかどうか
        /// </summary>
        public bool SortByDate
        {
            get => _sortByDate;
            set
            {
                if (_sortByDate != value)
                {
                    _sortByDate = value;
                    OnPropertyChanged();
                    _ = ApplySortAsync(_sortByDate, _sortAscending);
                }
            }
        }

        /// <summary>
        /// 昇順でソートするかどうか
        /// </summary>
        public bool SortAscending
        {
            get => _sortAscending;
            set
            {
                if (_sortAscending != value)
                {
                    _sortAscending = value;
                    OnPropertyChanged();
                    _ = ApplySortAsync(_sortByDate, _sortAscending);
                }
            }
        }

        /// <summary>
        /// ソート方向（昇順/降順）
        /// </summary>
        public SortDirection SortDirection
        {
            get => _sortDirection;
            set
            {
                if (_sortDirection != value)
                {
                    _sortDirection = value;
                    OnPropertyChanged();
                    SortAscending = value == SortDirection.Ascending;
                }
            }
        }

        /// <summary>
        /// ソートタイプ（0: 名前, 1: 日付, 2: 評価）
        /// </summary>
        public int SortType
        {
            get => _sortType;
            set
            {
                if (_sortType != value)
                {
                    _sortType = value;
                    OnPropertyChanged();
                    SortByDate = value == 1; // 1の場合は日付順
                }
            }
        }

        /// <summary>
        /// レーティングフィルター（0: すべて表示, 1-5: 指定評価以上）
        /// </summary>
        public int RatingFilter
        {
            get => _ratingFilter;
            set
            {
                if (_ratingFilter != value)
                {
                    _ratingFilter = value;
                    OnPropertyChanged();
                    CurrentRatingFilter = value;
                }
            }
        }

        /// <summary>
        /// ステータスメッセージ
        /// </summary>
        public string? StatusMessage
        {
            get => _statusMessage;
            private set
            {
                if (_statusMessage != value)
                {
                    _statusMessage = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// 進捗値（0-100）
        /// </summary>
        public int ProgressValue
        {
            get => _progressValue;
            private set
            {
                if (_progressValue != value)
                {
                    _progressValue = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// 選択されたアイテム
        /// </summary>
        private FileNodeModel? _selectedItem;
        public FileNodeModel? SelectedItem
        {
            get => _selectedItem;
            set
            {
                if (_selectedItem != value)
                {
                    _selectedItem = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// コンストラクタ
        /// </summary>
        public ImageViewModel()
        {
            _imageCollection = new ImageCollectionModel();
            _operationCache = new OperationCache();
            _updateManager = new UpdateSequenceManager(_imageCollection);

            // 状態変更イベントの購読
            _updateManager.StateChanged += OnUpdateManagerStateChanged;
        }

        /// <summary>
        /// 指定されたフォルダから画像を読み込む
        /// </summary>
        /// <param name="folderPath">フォルダパス</param>
        /// <returns>読み込まれたファイル数</returns>
        public async Task<int> LoadImagesFromFolderAsync(string folderPath)
        {
            try
            {
                IsLoading = true;
                StatusMessage = "フォルダから画像を読み込んでいます...";
                ProgressValue = 0;

                // 画像コレクションの読み込み
                var count = await _imageCollection.LoadImagesFromFolderAsync(folderPath);

                // キャッシュのクリア
                _operationCache.Clear();

                // 表示アイテムの更新
                await UpdateDisplayItemsAsync();

                // 初期ソートの適用
                await ApplySortAsync(_sortByDate, _sortAscending);

                StatusMessage = $"{count}個の画像を読み込みました";
                ProgressValue = 100;

                return count;
            }
            catch (Exception ex)
            {
                StatusMessage = $"エラー: {ex.Message}";
                return 0;
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>
        /// フィルタを適用する
        /// </summary>
        /// <param name="rating">レーティングフィルタ値</param>
        /// <returns>フィルタリングされたアイテム数</returns>
        public async Task<int> ApplyFilterAsync(int rating)
        {
            try
            {
                IsFiltering = true;
                StatusMessage = "フィルタを適用しています...";
                ProgressValue = 0;

                // キャッシュからフィルタ結果を取得
                var cachedResult = _operationCache.GetCachedFilterResult(rating);
                if (cachedResult != null)
                {
                    UpdateDisplayItems(cachedResult);
                    StatusMessage = $"フィルタを適用しました（{cachedResult.Count}個のアイテム）";
                    ProgressValue = 100;
                    return cachedResult.Count;
                }

                // UpdateSequenceManagerを使用してフィルタ処理を実行
                var result = await _updateManager.ExecuteFilterAsync(rating, () =>
                {
                    StatusMessage = "フィルタ処理が完了しました";
                    ProgressValue = 100;
                    return Task.CompletedTask;
                });

                // 結果をキャッシュに保存
                _operationCache.CacheFilterResult(rating, result.ToList());

                // 表示アイテムの更新
                UpdateDisplayItems(result);

                return result.Count;
            }
            catch (Exception ex)
            {
                StatusMessage = $"フィルタエラー: {ex.Message}";
                return 0;
            }
            finally
            {
                IsFiltering = false;
            }
        }

        /// <summary>
        /// ソートを適用する
        /// </summary>
        /// <param name="sortByDate">日付順でソートするかどうか</param>
        /// <param name="sortAscending">昇順でソートするかどうか</param>
        /// <returns>ソートされたアイテム数</returns>
        public async Task<int> ApplySortAsync(bool sortByDate, bool sortAscending)
        {
            try
            {
                IsSorting = true;
                StatusMessage = "ソートを適用しています...";
                ProgressValue = 0;

                // キャッシュからソート結果を取得
                var cachedResult = _operationCache.GetCachedSortResult(sortByDate, sortAscending);
                if (cachedResult != null)
                {
                    UpdateDisplayItems(cachedResult);
                    StatusMessage = $"ソートを適用しました（{cachedResult.Count}個のアイテム）";
                    ProgressValue = 100;
                    return cachedResult.Count;
                }

                // UpdateSequenceManagerを使用してソート処理を実行
                var result = await _updateManager.ExecuteSortAsync(sortByDate, sortAscending, () =>
                {
                    StatusMessage = "ソート処理が完了しました";
                    ProgressValue = 100;
                    return Task.CompletedTask;
                });

                // 結果をキャッシュに保存
                _operationCache.CacheSortResult(sortByDate, sortAscending, result.ToList());

                // 表示アイテムの更新
                UpdateDisplayItems(result);

                return result.Count;
            }
            catch (Exception ex)
            {
                StatusMessage = $"ソートエラー: {ex.Message}";
                return 0;
            }
            finally
            {
                IsSorting = false;
            }
        }

        /// <summary>
        /// サムネイルの読み込みをキューに追加
        /// </summary>
        /// <param name="visibleItems">表示中のアイテム</param>
        /// <param name="isVisible">表示中かどうか</param>
        public void EnqueueThumbnailLoad(IEnumerable<FileNodeModel> visibleItems, bool isVisible)
        {
            _updateManager.EnqueueThumbnailLoad(visibleItems, isVisible);
        }

        /// <summary>
        /// 表示アイテムの更新
        /// </summary>
        private async Task UpdateDisplayItemsAsync()
        {
            var items = _imageCollection.Items.ToList();
            UpdateDisplayItems(items);
        }

        /// <summary>
        /// 表示アイテムの更新
        /// </summary>
        private void UpdateDisplayItems(IList<FileNodeModel> items)
        {
            // 選択アイテムの保存
            var selectedItem = SelectedItem;

            // 表示アイテムの更新
            DisplayItems.Clear();
            foreach (var item in items)
            {
                DisplayItems.Add(item);
            }

            // 選択アイテムの復元
            if (selectedItem != null && DisplayItems.Contains(selectedItem))
            {
                SelectedItem = selectedItem;
            }
            else if (DisplayItems.Count > 0)
            {
                SelectedItem = DisplayItems[0];
            }
            else
            {
                SelectedItem = null;
            }
        }

        /// <summary>
        /// UpdateManagerの状態変更イベントハンドラ
        /// </summary>
        private void OnUpdateManagerStateChanged(object? sender, OperationStateChangedEventArgs e)
        {
            switch (e.Type)
            {
                case OperationType.ThumbnailLoad:
                    HandleThumbnailLoadStateChange(e.State);
                    break;
                case OperationType.Filter:
                    HandleFilterStateChange(e.State);
                    break;
                case OperationType.Sort:
                    HandleSortStateChange(e.State);
                    break;
            }
        }

        /// <summary>
        /// サムネイル読み込み状態変更の処理
        /// </summary>
        private void HandleThumbnailLoadStateChange(OperationState state)
        {
            switch (state)
            {
                case OperationState.Running:
                    StatusMessage = "サムネイルを読み込んでいます...";
                    break;
                case OperationState.Completed:
                    StatusMessage = "サムネイル読み込みが完了しました";
                    break;
                case OperationState.Failed:
                    StatusMessage = "サムネイル読み込みに失敗しました";
                    break;
            }
        }

        /// <summary>
        /// フィルタ状態変更の処理
        /// </summary>
        private void HandleFilterStateChange(OperationState state)
        {
            switch (state)
            {
                case OperationState.Running:
                    IsFiltering = true;
                    StatusMessage = "フィルタを適用しています...";
                    break;
                case OperationState.Completed:
                    IsFiltering = false;
                    StatusMessage = "フィルタ適用が完了しました";
                    break;
                case OperationState.Failed:
                    IsFiltering = false;
                    StatusMessage = "フィルタ適用に失敗しました";
                    break;
            }
        }

        /// <summary>
        /// ソート状態変更の処理
        /// </summary>
        private void HandleSortStateChange(OperationState state)
        {
            switch (state)
            {
                case OperationState.Running:
                    IsSorting = true;
                    StatusMessage = "ソートを適用しています...";
                    break;
                case OperationState.Completed:
                    IsSorting = false;
                    StatusMessage = "ソート適用が完了しました";
                    break;
                case OperationState.Failed:
                    IsSorting = false;
                    StatusMessage = "ソート適用に失敗しました";
                    break;
            }
        }

        /// <summary>
        /// プロパティ変更通知イベント
        /// </summary>
        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// プロパティ変更通知
        /// </summary>
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
