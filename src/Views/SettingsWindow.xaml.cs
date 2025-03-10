using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Controls;
using Illustra.ViewModels;
using Prism.Ioc;
using Prism.Events;

namespace Illustra.Views
{
    /// <summary>
    /// SettingsWindow.xaml の相互作用ロジック
    /// </summary>
    public partial class SettingsWindow : Window
    {
        private SettingsViewModel _viewModel;

        public SettingsWindow()
        {
            InitializeComponent();

            try
            {
                // SettingsViewModelをDIコンテナから取得
                _viewModel = ((App)Application.Current).Container.Resolve<SettingsViewModel>();

                // ウィンドウのDataContextを設定
                this.DataContext = _viewModel;

                // SettingsViewのDataContextを明示的に設定
                if (SettingsViewControl != null)
                {
                    SettingsViewControl.DataContext = _viewModel;
                    System.Diagnostics.Debug.WriteLine("SettingsViewControl DataContext set successfully");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("SettingsViewControl is null");
                }

                // 言語変更イベントを購読して、変更があったらウィンドウを閉じる
                var eventAggregator = ((App)Application.Current).Container.Resolve<IEventAggregator>();
                eventAggregator.GetEvent<Services.LanguageChangedEvent>().Subscribe(OnLanguageChanged);

                System.Diagnostics.Debug.WriteLine($"SettingsWindow initialized with ViewModel: {_viewModel != null}");
                if (_viewModel != null)
                {
                    System.Diagnostics.Debug.WriteLine($"Current language index: {_viewModel.SelectedLanguageIndex}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"設定画面の初期化エラー: {ex.Message}");
                MessageBox.Show($"設定画面の初期化中にエラーが発生しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnLanguageChanged()
        {
            try
            {
                // 保存したらウィンドウを閉じる
                Dispatcher.Invoke(() =>
                {
                    System.Diagnostics.Debug.WriteLine("Language changed, closing window");
                    Close();
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in OnLanguageChanged: {ex.Message}");
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("SettingsWindow: SaveButton_Click called");
                if (_viewModel != null && _viewModel.SaveCommand.CanExecute())
                {
                    System.Diagnostics.Debug.WriteLine("SettingsWindow: Executing SaveCommand");
                    _viewModel.SaveCommand.Execute();
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("SettingsWindow: ViewModel is null or SaveCommand cannot execute");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in SaveButton_Click: {ex.Message}");
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
