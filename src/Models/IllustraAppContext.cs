using Illustra.Helpers;
using Prism.Mvvm;
using Illustra.ViewModels;
using System.Threading.Tasks; // 追加
using System; // 追加
// using Illustra.Helpers; // 重複のため削除
using Illustra.Services; // DatabaseManager を使うために追加

namespace Illustra.Models
{
    /// <summary>
    /// アプリケーション全体で共有する状態を管理するクラス
    /// </summary>
    public class IllustraAppContext : BindableBase
    {
        private readonly DatabaseManager _dbManager; // 追加
        private ImagePropertiesModel _currentProperties;
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
        /// 指定されたファイルのプロパティを非同期で読み込み、CurrentPropertiesを更新します。
        /// </summary>
        /// <param name="filePath">プロパティを読み込むファイルのパス。</param>
        public async Task UpdateCurrentPropertiesAsync(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                CurrentProperties = new ImagePropertiesModel(); // パスが空なら空のプロパティを設定
                return;
            }

            // 既に同じファイルのプロパティが読み込まれていれば更新しない (任意)
            // if (CurrentProperties?.FilePath == filePath) return;

            try
            {
                LogHelper.LogWithTimestamp($"プロパティ読み込み開始: {filePath}", LogHelper.Categories.UI);
                // ImagePropertiesHelper.LoadPropertiesAsync は静的メソッドと仮定
                // ImagePropertiesServiceと同様の静的メソッドを使用
                var properties = await ImagePropertiesModel.LoadFromFileAsync(filePath);
                if (properties == null)
                {
                    properties = new ImagePropertiesModel { FilePath = filePath }; // 読み込めなかった場合は最低限の情報を設定
                }

                // MainViewModel から Rating を取得して設定
                var fileNode = MainViewModel?.Items?.FirstOrDefault(n => n.FullPath == filePath);
                if (fileNode != null)
                {
                    properties.Rating = fileNode.Rating;
                    LogHelper.LogWithTimestamp("MainViewModelからレーティングを設定", LogHelper.Categories.UI);
                }

                CurrentProperties = properties; // 更新されたプロパティをセット
                LogHelper.LogWithTimestamp($"プロパティ読み込み完了: {filePath}", LogHelper.Categories.UI);
            }
            catch (Exception ex)
            {
                LogHelper.LogError($"プロパティの読み込み中にエラーが発生しました: {filePath}", ex);
                CurrentProperties = new ImagePropertiesModel { FilePath = filePath }; // エラー時も最低限の情報を設定
            }
        }
    }
}
