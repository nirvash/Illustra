using System;
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
    }

    public class ImagePropertiesService : IImagePropertiesService
    {
        private readonly IEventAggregator _eventAggregator;
        private readonly IllustraAppContext _appContext;

        public ImagePropertiesService(
            IEventAggregator eventAggregator,
            IllustraAppContext appContext)
        {
            _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
            _appContext = appContext ?? throw new ArgumentNullException(nameof(appContext));
        }

        public void Initialize()
        {
            LogHelper.LogWithTimestamp("イベント購読を開始", LogHelper.Categories.UI);

            // FileSelectedEventをPubSubEvent<SelectedFileModel>として購読
            var fileSelectedEvent = _eventAggregator.GetEvent<PubSubEvent<SelectedFileModel>>();
            fileSelectedEvent.Subscribe(OnFileSelected, ThreadOption.UIThread);

            LogHelper.LogWithTimestamp("初期化完了", LogHelper.Categories.UI);
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

                var properties = await ImagePropertiesModel.LoadFromFileAsync(model.FullPath);
                _appContext.CurrentProperties = properties;
            }
            catch (Exception ex)
            {
                LogHelper.LogError("プロパティ読み込みエラー", ex);
            }
        }
    }
}
