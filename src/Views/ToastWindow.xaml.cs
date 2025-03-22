using System;
using System.ComponentModel;
using System.Windows;
using MahApps.Metro.Controls;

namespace Illustra.Views
{
    public partial class ToastWindow : MetroWindow, INotifyPropertyChanged
    {
        private string _message;
        public string Message
        {
            get => _message;
            set
            {
                _message = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Message)));
            }
        }
        public event PropertyChangedEventHandler? PropertyChanged;

        public ToastWindow(string message)
        {
            InitializeComponent();
            Message = message;
            DataContext = this;
        }
    }
}
