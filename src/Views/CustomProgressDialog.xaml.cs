using System;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using MahApps.Metro.Controls; // MetroWindow を使うために追加
using MahApps.Metro.Controls.Dialogs;

namespace Illustra.Views
{
    public partial class CustomProgressDialog : BaseMetroDialog
    {
        // Dependency Properties for Binding
        public static readonly DependencyProperty CountTextProperty =
            DependencyProperty.Register("CountText", typeof(string), typeof(CustomProgressDialog), new PropertyMetadata("0/0"));

        public string CountText
        {
            get { return (string)GetValue(CountTextProperty); }
            set { SetValue(CountTextProperty, value); }
        }

        public static readonly DependencyProperty FileNameTextProperty =
            DependencyProperty.Register("FileNameText", typeof(string), typeof(CustomProgressDialog), new PropertyMetadata("Processing..."));

        public string FileNameText
        {
            get { return (string)GetValue(FileNameTextProperty); }
            set { SetValue(FileNameTextProperty, value); }
        }

        public static readonly DependencyProperty ProgressValueProperty =
            DependencyProperty.Register("ProgressValue", typeof(double), typeof(CustomProgressDialog), new PropertyMetadata(0.0));

        public double ProgressValue
        {
            get { return (double)GetValue(ProgressValueProperty); }
            set { SetValue(ProgressValueProperty, value); }
        }

        public static readonly DependencyProperty IsCancelableProperty =
            DependencyProperty.Register("IsCancelable", typeof(bool), typeof(CustomProgressDialog), new PropertyMetadata(false));

        public bool IsCancelable
        {
            get { return (bool)GetValue(IsCancelableProperty); }
            set
            {
                SetValue(IsCancelableProperty, value);
                // Directly control the button's IsEnabled state based on the property
                // Ensure this runs on the UI thread if set from another thread
                Dispatcher.Invoke(() => CancelButton.IsEnabled = value && !_isCancelled);
            }
        }

        // Cancellation Token Source
        private CancellationTokenSource _cancellationTokenSource;

        private bool _isCancelled = false;
        public bool IsCancelled => _isCancelled; // Public property to check cancellation status
        // Constructor requires MetroWindow
        public CustomProgressDialog(MetroWindow parentWindow, MetroDialogSettings settings = null)
            : base(parentWindow, settings ?? parentWindow?.MetroDialogOptions) // parentWindow can be null, handle it
        {
            InitializeComponent();
            // Ensure parentWindow is not null before accessing MetroDialogOptions if settings is null
            if (parentWindow == null && settings == null)
            {
                // Handle error or use default settings if parentWindow is null
                // For now, let's assume settings will be provided or parentWindow is not null
                // Or throw new ArgumentNullException(nameof(parentWindow), "Parent window cannot be null if settings are not provided.");
            }
        }


        // Public method to update progress
        public void UpdateProgress(string countText, string fileNameText, double progressValue)
        {
            // Don't update UI if cancellation has been requested
            if (_isCancelled) return;

            // Ensure updates happen on the UI thread
            Dispatcher.Invoke(() =>
            {
                CountText = countText;
                FileNameText = fileNameText;
                ProgressValue = progressValue;
            });
        }

        // Method to set the CancellationTokenSource (optional)
        public void SetCancellationTokenSource(CancellationTokenSource cts)
        {
            _cancellationTokenSource = cts;
            IsCancelable = _cancellationTokenSource != null; // Enable button if CTS is provided
        }

        private async void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isCancelled)
            {
                // First click: Cancel the operation
                _isCancelled = true;
                IsCancelable = false; // Disable further cancellation attempts via property binding

                // Request cancellation if possible
                try
                {
                    _cancellationTokenSource?.Cancel();
                }
                catch (ObjectDisposedException)
                {
                    // Ignore if already disposed
                }

                // Update UI to reflect cancellation
                FileNameText = (string)Application.Current.FindResource("String_Dialog_FileOperationCancelled"); // Use resource key
                // Optionally make progress bar indeterminate or hide it
                // ProgressBar.IsIndeterminate = true;
                // CancelButton.Content = Application.Current.FindResource("String_Common_Close"); // No need to change Cancel button text
                CancelButton.IsEnabled = false; // Disable Cancel button after click
                CloseButton.IsEnabled = true; // Enable Close button
                // CloseButton.Visibility = Visibility.Visible; // Already visible
                // return; // No need to return here anymore
            }
            // Removed the 'else' block as the button now only cancels
        }

        private async void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            // Simply close the dialog
            var dialogCoordinator = DialogCoordinator.Instance;
            var context = this.OwningWindow?.DataContext;
            if (context != null)
            {
                await dialogCoordinator.HideMetroDialogAsync(context, this);
            }
            else
            {
                await this.RequestCloseAsync();
            }
        }

        // Ensure the dialog is ready for updates after loading
        private void BaseMetroDialog_Loaded(object sender, RoutedEventArgs e)
        {
            // You might need initial setup here if required
        }

        // Override OnClose to prevent closing if cancellation is not supported or not requested?
        // Or handle cancellation logic elsewhere.
    }
}
