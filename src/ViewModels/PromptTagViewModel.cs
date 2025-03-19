using System;
using System;
using System.Windows.Input;
using System.Windows.Media;
using Illustra.Commands;
using Prism.Mvvm;

namespace Illustra.ViewModels
{
    public class PromptTagViewModel : BindableBase
    {
        private string _text;
        private bool _isEnabled = true;
        private TagCategoryViewModel _category;
        private int _order;
        private Brush _textColor;
        private Brush _backgroundColor;

        public string Text
        {
            get => _text;
            set => SetProperty(ref _text, value);
        }

        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                if (SetProperty(ref _isEnabled, value))
                {
                    UpdateColors();
                }
            }
        }

        public TagCategoryViewModel Category
        {
            get => _category;
            set
            {
                if (SetProperty(ref _category, value))
                {
                    UpdateColors();
                }
            }
        }

        public int Order
        {
            get => _order;
            set => SetProperty(ref _order, value);
        }

        public Brush TextColor
        {
            get => _textColor;
            private set => SetProperty(ref _textColor, value);
        }

        public Brush BackgroundColor
        {
            get => _backgroundColor;
            private set => SetProperty(ref _backgroundColor, value);
        }

        public ICommand DeleteCommand { get; }
        public ICommand ToggleEnableCommand { get; }

        private PromptEditorViewModel _owner;

        public PromptTagViewModel(PromptEditorViewModel owner = null)
        {
            _owner = owner;

            // コマンドの初期化
            DeleteCommand = new RelayCommand(ExecuteDelete);
            ToggleEnableCommand = new RelayCommand(ExecuteToggleEnable);

            // デフォルトの色を設定
            TextColor = Brushes.Black;
            BackgroundColor = Brushes.White;
        }

        public void SetOwner(PromptEditorViewModel owner)
        {
            _owner = owner;
        }

        private void ExecuteDelete()
        {
            // 削除イベントを発火
            Deleted?.Invoke(this, EventArgs.Empty);
        }

        private void ExecuteToggleEnable()
        {
            IsEnabled = !IsEnabled;
        }

        private void UpdateColors()
        {
            if (Category != null)
            {
                TextColor = IsEnabled ? Brushes.Black : Brushes.Gray;
                BackgroundColor = IsEnabled ? Category.BackgroundColor : Brushes.LightGray;
            }
            else
            {
                TextColor = IsEnabled ? Brushes.Black : Brushes.Gray;
                BackgroundColor = IsEnabled ? Brushes.White : Brushes.LightGray;
            }
        }

        // 削除イベント
        public event EventHandler Deleted;
    }
}
