using Illustra.Helpers;
using Prism.Mvvm;
using Illustra.ViewModels;
using System.Threading.Tasks; // 追加
using System; // 追加
// using Illustra.Helpers; // 重複のため削除
using Illustra.Services; // DatabaseManager を使うために追加
using System.IO;
using System.Linq;

namespace Illustra.Models
{
    /// <summary>
    /// アプリケーション全体で共有する状態を管理するクラス
    /// </summary>
    public class IllustraAppContext : BindableBase
    {
        private readonly DatabaseManager _dbManager; // 追加
        private ImagePropertiesModel _currentProperties;

        // プロパティパネルの表示状態（ウィンドウごとに管理し、いずれかが表示されていれば解析を行う）
        private bool _isMainPanelVisible = true;
        private bool _isViewerPanelVisible;

        // プロパティ読み込み要求の世代カウンタ（古い読み込み結果の適用を防ぐ）
        private int _propertiesRequestId;

        /// <summary>
        /// プロパティパネルがいずれかのウィンドウで表示中かどうか。
        /// 非表示の場合はメタデータ解析をスキップする。
        /// </summary>
        public bool IsPropertyPanelVisible => _isMainPanelVisible || _isViewerPanelVisible;

        public ImagePropertiesModel CurrentProperties
        {
            get => _currentProperties;
            private set // Setterをprivateに変更して外部からの直接変更を防ぐ
            {
                if (SetProperty(ref _currentProperties, value))
                {
                    LogHelper.LogWithTimestamp(
                        $"プロパティを更新: {value?.FilePath ?? "null"}",
                        LogHelper.Categories.UI);
                }
            }
        }

        private ThumbnailListViewModel _mainViewModel;
        public ThumbnailListViewModel MainViewModel
        {
            get => _mainViewModel;
            set => SetProperty(ref _mainViewModel, value);
        }

        // DatabaseManager を注入するようにコンストラクタを変更
        public IllustraAppContext(ThumbnailListViewModel mainViewModel, DatabaseManager dbManager)
        {
            _currentProperties = new ImagePropertiesModel();
            _mainViewModel = mainViewModel ?? throw new ArgumentNullException(nameof(mainViewModel));
            _dbManager = dbManager ?? throw new ArgumentNullException(nameof(dbManager)); // 確実に代入
            LogHelper.LogWithTimestamp("初期化完了", LogHelper.Categories.UI);
        }

        /// <summary>
        /// メインウィンドウのプロパティパネル表示状態を通知します。
        /// </summary>
        public void SetMainPropertyPanelVisible(bool visible)
        {
            if (_isMainPanelVisible == visible) return;
            _isMainPanelVisible = visible;
            OnPanelVisibilityChanged();
        }

        /// <summary>
        /// ビューアウィンドウのプロパティパネル表示状態を通知します。
        /// </summary>
        public void SetViewerPropertyPanelVisible(bool visible)
        {
            if (_isViewerPanelVisible == visible) return;
            _isViewerPanelVisible = visible;
            OnPanelVisibilityChanged();
        }

        private void OnPanelVisibilityChanged()
        {
            RaisePropertyChanged(nameof(IsPropertyPanelVisible));

            // パネルが表示された場合は、現在選択中のファイルのプロパティを再読み込みする
            if (IsPropertyPanelVisible && !string.IsNullOrEmpty(CurrentProperties?.FilePath))
            {
                _ = UpdateCurrentPropertiesAsync(CurrentProperties.FilePath, forceReload: true);
            }
        }

        /// <summary>
        /// 指定されたファイルのプロパティを非同期で読み込み、CurrentPropertiesを更新します。
        /// プロパティパネルが非表示の場合はメタデータ解析を行わず、基本情報のみを設定します。
        /// </summary>
        /// <param name="filePath">プロパティを読み込むファイルのパス。</param>
        /// <param name="forceReload">同一ファイル済み読み込みでも強制的に再読み込みするかどうか。</param>
        public async Task UpdateCurrentPropertiesAsync(string filePath, bool forceReload = false)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                CurrentProperties = new ImagePropertiesModel(); // パスが空なら空のプロパティを設定
                return;
            }

            // 既に同じファイルのプロパティが読み込まれていれば更新しない
            if (!forceReload && CurrentProperties?.FilePath == filePath) return;

            // プロパティパネル非表示時はメタデータ解析（重い処理）をスキップする
            if (!IsPropertyPanelVisible)
            {
                SetLightweightProperties(filePath);
                return;
            }

            var requestId = ++_propertiesRequestId;

            try
            {
                LogHelper.LogWithTimestamp($"プロパティ読み込み開始: {filePath}", LogHelper.Categories.UI);
                // ImagePropertiesHelper.LoadPropertiesAsync は静的メソッドと仮定
                // ImagePropertiesServiceと同様の静的メソッドを使用
                var properties = await ImagePropertiesModel.LoadFromFileAsync(filePath);

                // 読み込み中に別のファイルが選択された場合は古い結果を破棄する
                if (requestId != _propertiesRequestId) return;

                if (properties == null)
                {
                    properties = new ImagePropertiesModel { FilePath = filePath }; // 読み込めなかった場合は最低限の情報を設定
                }

                // MainViewModel から Rating を取得して設定
                ApplyRatingFromItems(properties, filePath);

                CurrentProperties = properties; // 更新されたプロパティをセット
                LogHelper.LogWithTimestamp($"プロパティ読み込み完了: {filePath}", LogHelper.Categories.UI);
            }
            catch (Exception ex)
            {
                LogHelper.LogError($"プロパティの読み込み中にエラーが発生しました: {filePath}", ex);
                if (requestId == _propertiesRequestId)
                {
                    CurrentProperties = new ImagePropertiesModel { FilePath = filePath }; // エラー時も最低限の情報を設定
                }
            }
        }

        private void SetLightweightProperties(string filePath)
        {
            var properties = new ImagePropertiesModel
            {
                FilePath = filePath,
                FileName = Path.GetFileName(filePath),
                FolderPath = Path.GetDirectoryName(filePath) ?? string.Empty,
            };
            ApplyRatingFromItems(properties, filePath);
            CurrentProperties = properties;
        }

        private void ApplyRatingFromItems(ImagePropertiesModel properties, string filePath)
        {
            var fileNode = MainViewModel?.Items?.FirstOrDefault(n => n.FullPath == filePath);
            if (fileNode != null)
            {
                properties.Rating = fileNode.Rating;
            }
        }
    }
}
