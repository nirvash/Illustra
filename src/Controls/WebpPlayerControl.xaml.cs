using Illustra.Helpers;
using System.Threading.Tasks;
using System.Windows.Controls;
using Illustra.ViewModels;
using Illustra.Services;

namespace Illustra.Controls
{
    public partial class WebpPlayerControl : UserControl
    {
        private readonly WebpPlayerViewModel _viewModel;

        public WebpPlayerControl()
        {
            InitializeComponent();
            _viewModel = new WebpPlayerViewModel(new WebpAnimationService());
            this.DataContext = _viewModel;
        }

        public async Task LoadWebpAsync(string filePath)
        {
            LogHelper.LogWithTimestamp("WebpPlayerControl.LoadWebpAsync - Start", LogHelper.Categories.Performance);
            LogHelper.LogWithTimestamp("WebpPlayerControl.LoadWebpAsync - Before ViewModel.LoadAsync", LogHelper.Categories.Performance);
            await _viewModel.LoadAsync(filePath);
            LogHelper.LogWithTimestamp("WebpPlayerControl.LoadWebpAsync - After ViewModel.LoadAsync", LogHelper.Categories.Performance);
        }
    }
}
