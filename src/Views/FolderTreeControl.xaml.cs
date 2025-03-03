using System.Windows;
using System.Windows.Controls;
using Illustra.Events;

namespace Illustra.Views
{
    public partial class FolderTreeControl : UserControl, IActiveAware
    {
        private IEventAggregator? _eventAggregator;
        private string? _currentSelectedFilePath = string.Empty;
        private bool ignoreSelectedChangedOnce;
        private const string CONTROL_ID = "FolderTree";

        #region IActiveAware Implementation
#pragma warning disable 0067 // 使用されていませんという警告を無視
        public bool IsActive { get; set; }
        public event EventHandler? IsActiveChanged;
#pragma warning restore 0067 // 警告の無視を終了
        #endregion

        // xaml でインスタンス化するためのデフォルトコンストラクタ
        public FolderTreeControl()
        {
            InitializeComponent();
            Loaded += FolderTreeControl_Loaded;
        }

        private void FolderTreeControl_Loaded(object sender, RoutedEventArgs e)
        {
            // ContainerLocatorを使ってEventAggregatorを取得
            _eventAggregator = ContainerLocator.Container.Resolve<IEventAggregator>();
            _eventAggregator.GetEvent<FolderSelectedEvent>().Subscribe(OnFolderSelected, ThreadOption.UIThread, false,
                filter => filter.SourceId != CONTROL_ID); // 自分が発信したイベントは無視
        }

        private void OnFolderSelected(FolderSelectedEventArgs args)
        {
            if (args.Path == _currentSelectedFilePath) return;
            if (ignoreSelectedChangedOnce)
            {
                ignoreSelectedChangedOnce = false;
                return;
            }

            ignoreSelectedChangedOnce = false;
            _currentSelectedFilePath = args.Path;

            // Expand 処理は FileSystemTreeViewControl が行うのでここでは行わない
            _eventAggregator?.GetEvent<SelectFileRequestEvent>().Publish("");
        }


        public void SaveAllData()
        {
            // 必要に応じて実装
        }
    }
}
