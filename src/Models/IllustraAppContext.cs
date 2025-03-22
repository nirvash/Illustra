using Illustra.Helpers;
using Prism.Mvvm;
using Illustra.ViewModels;

namespace Illustra.Models
{
    /// <summary>
    /// アプリケーション全体で共有する状態を管理するクラス
    /// </summary>
    public class IllustraAppContext : BindableBase
    {
        private ImagePropertiesModel _currentProperties;
        public ImagePropertiesModel CurrentProperties
        {
            get => _currentProperties;
            set
            {
                if (SetProperty(ref _currentProperties, value))
                {
                    LogHelper.LogWithTimestamp(
                        $"プロパティを更新: {value?.FilePath ?? "null"}",
                        LogHelper.Categories.UI);
                }
            }
        }

        private MainViewModel _mainViewModel;
        public MainViewModel MainViewModel
        {
            get => _mainViewModel;
            set => SetProperty(ref _mainViewModel, value);
        }

        public IllustraAppContext()
        {
            _currentProperties = new ImagePropertiesModel();
            // ThumbnailListControlがMainViewModelのライフサイクルを管理するため、ここでは初期化しない
            LogHelper.LogWithTimestamp("初期化完了", LogHelper.Categories.UI);
        }
    }
}
