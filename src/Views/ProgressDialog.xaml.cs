using System;
using System.ComponentModel;
using System.Windows;
using MahApps.Metro.Controls;
using System.Threading;

namespace Illustra.Views
{
    public partial class ProgressDialog : MetroWindow, INotifyPropertyChanged
    {
        private string _windowTitle = string.Empty;
        public string WindowTitle
        {
            get => _windowTitle;
            set
            {
                if (_windowTitle != value)
                {
                    _windowTitle = value;
                    OnPropertyChanged(nameof(WindowTitle));
                    Dispatcher.InvokeAsync(() => Title = value);
                }
            }
        }

        private string _message = string.Empty;
        public string Message
        {
            get => _message;
            set
            {
                if (_message != value)
                {
                    _message = value;
                    OnPropertyChanged(nameof(Message));
                }
            }
        }

        private bool _isIndeterminate;
        public bool IsIndeterminate
        {
            get => _isIndeterminate;
            set
            {
                if (_isIndeterminate != value)
                {
                    _isIndeterminate = value;
                    OnPropertyChanged(nameof(IsIndeterminate));
                }
            }
        }

        private double _progress;
        public double Progress
        {
            get => _progress;
            set
            {
                if (_progress != value)
                {
                    _progress = value;
                    OnPropertyChanged(nameof(Progress));
                }
            }
        }

        private bool _cancellationSupported;
        public bool CancellationSupported
        {
            get => _cancellationSupported;
            set
            {
                if (_cancellationSupported != value)
                {
                    _cancellationSupported = value;
                    OnPropertyChanged(nameof(CancellationSupported));
                }
            }
        }

        private CancellationTokenSource _cancellationTokenSource;
        public CancellationToken CancellationToken => _cancellationTokenSource.Token;

        public event EventHandler CancelRequested;
        public event EventHandler StartRequested;
        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public ProgressDialog()
        {
            InitializeComponent();
            DataContext = this;
            _cancellationTokenSource = new CancellationTokenSource();
            Reset();
        }

        public void Reset()
        {
            Dispatcher.InvokeAsync(() =>
            {
                StartButton.IsEnabled = true;
                CancelButton.IsEnabled = false;
                IsIndeterminate = false;
                Progress = 0;
                Message = (string)FindResource("String_Settings_Developer_PreparingCleanup");
            });
        }

        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            Dispatcher.InvokeAsync(() =>
            {
                StartButton.IsEnabled = false;
                CancelButton.IsEnabled = true;
                IsIndeterminate = true;
                Progress = 0;
                StartRequested?.Invoke(this, EventArgs.Empty);
            });
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            CancelButton.IsEnabled = false;
            _cancellationTokenSource.Cancel();
            CancelRequested?.Invoke(this, EventArgs.Empty);
        }

        public void EnableCancel(bool enable)
        {
            Dispatcher.InvokeAsync(() => CancelButton.IsEnabled = enable);
        }

        public void UpdateProgress(string message, double progress)
        {
            Dispatcher.InvokeAsync(() =>
            {
                Message = message;
                Progress = progress;
            });
        }
    }
}
