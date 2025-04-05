using System;
using System.Linq;
using System.Threading.Tasks;
using Illustra.Events;
using Illustra.Helpers;
using Illustra.Models;
using Prism.Events;

namespace Illustra.Services
{
    /// <summary>
    /// 画像プロパティの更新処理を担当するサービス
    /// </summary>
    public interface IImagePropertiesService
    {
        void Initialize();
        Task ReloadPropertiesAsync(string filePath);
    }

    public class ImagePropertiesService : IImagePropertiesService, IDisposable
    {
        private readonly IEventAggregator _eventAggregator;
        private readonly IllustraAppContext _appContext;

        public ImagePropertiesService(
            IEventAggregator eventAggregator,
            IllustraAppContext appContext)
        {
            _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
            _appContext = appContext ?? throw new ArgumentNullException(nameof(appContext));

            // デバッグ情報を出力
            LogHelper.LogWithTimestamp("ImagePropertiesServiceが作成されました", LogHelper.Categories.UI);
        }

        public void Initialize()
        {
            LogHelper.LogWithTimestamp("イベント購読を開始", LogHelper.Categories.UI);

            // FileSelectedEventを確実に購読（型を確認）
            _eventAggregator.GetEvent<FileSelectedEvent>().Subscribe(OnFileSelected, ThreadOption.UIThread);

            // プロパティ更新リクエストイベントを購読
            _eventAggregator.GetEvent<PubSubEvent<string>>()
                .Subscribe(OnReloadPropertiesRequested, ThreadOption.UIThread, false,
                    filter => filter != null && !string.IsNullOrEmpty(filter));

            // レーティング変更イベントを購読
            _eventAggregator.GetEvent<RatingChangedEvent>()
                .Subscribe(OnRatingChanged, ThreadOption.UIThread);

            LogHelper.LogWithTimestamp("初期化完了", LogHelper.Categories.UI);
        }

        private void OnRatingChanged(RatingChangedEventArgs args)
        {
            if (args == null || string.IsNullOrEmpty(args.FilePath))
            {
                return;
            }

            try
            {
                LogHelper.LogWithTimestamp($"レーティング変更: {args.FilePath} → {args.Rating}", LogHelper.Categories.UI);

                // 現在のプロパティが対象ファイルと一致する場合のみレーティングを更新
                if (_appContext.CurrentProperties != null &&
                    _appContext.CurrentProperties.FilePath == args.FilePath)
                {
                    _appContext.CurrentProperties.Rating = args.Rating;
                    LogHelper.LogWithTimestamp("共有コンテキストのレーティングを更新", LogHelper.Categories.UI);
                }
            }
            catch (Exception ex)
            {
                LogHelper.LogError("レーティング更新エラー", ex);
            }
        }

        private async void OnReloadPropertiesRequested(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                LogHelper.LogWithTimestamp("無効なファイルパス", LogHelper.Categories.UI);
                return;
            }

            await ReloadPropertiesAsync(filePath);
        }

        public async Task ReloadPropertiesAsync(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                throw new ArgumentException("ファイルパスが空です", nameof(filePath));
            }

            try
            {
                LogHelper.LogWithTimestamp($"プロパティ再読み込み: {filePath}", LogHelper.Categories.UI);

                var properties = await ImagePropertiesModel.LoadFromFileAsync(filePath);

                // MainViewModelから対応するFileNodeModelのレーティングを設定
                var fileNode = _appContext.MainViewModel?.Items?.FirstOrDefault(n => n.FullPath == filePath);
                if (fileNode != null)
                {
                    properties.Rating = fileNode.Rating;
                    LogHelper.LogWithTimestamp("MainViewModelからレーティングを設定", LogHelper.Categories.UI);
                }

                // _appContext.CurrentProperties = properties; // 直接代入は不可
                // UpdateCurrentPropertiesAsync を呼び出して更新を依頼する
                // 注意: これによりプロパティが再度読み込まれる
                await _appContext.UpdateCurrentPropertiesAsync(filePath);

                LogHelper.LogWithTimestamp("プロパティ再読み込み完了", LogHelper.Categories.UI);
            }
            catch (Exception ex)
            {
                LogHelper.LogError("プロパティ再読み込みエラー", ex);
                throw;
            }
        }

        private async void OnFileSelected(SelectedFileModel model)
        {
            if (model == null || string.IsNullOrEmpty(model.FullPath))
            {
                LogHelper.LogWithTimestamp("無効なファイル選択イベント", LogHelper.Categories.UI);
                return;
            }

            try
            {
                LogHelper.LogWithTimestamp($"ファイル選択: {model.FullPath}", LogHelper.Categories.UI);

                // UpdateCurrentPropertiesAsync を呼び出して更新を依頼する
                // 注意: これによりプロパティが再度読み込まれる
                await _appContext.UpdateCurrentPropertiesAsync(model.FullPath);
            }
            catch (Exception ex)
            {
                LogHelper.LogError("プロパティ読み込みエラー", ex);
            }
        }

        // Disposeメソッドをオーバーライド（IDisposableを実装する場合）
        public void Dispose()
        {
            LogHelper.LogWithTimestamp("ImagePropertiesServiceが破棄されました", LogHelper.Categories.UI);
            // リソース解放処理
        }
    }
}
