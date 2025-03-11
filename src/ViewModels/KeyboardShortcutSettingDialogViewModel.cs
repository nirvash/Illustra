using Prism.Commands;
using Prism.Mvvm;
using System.Windows.Input;

namespace Illustra.ViewModels
{
    public class KeyboardShortcutSettingDialogViewModel : BindableBase
    {
        private bool _isCtrlPressed;
        private bool _isAltPressed;
        private bool _isShiftPressed;
        private bool _isWindowsPressed;
        private Key _selectedKey = Key.None;
        public bool IsCtrlPressed
        {
            get => _isCtrlPressed;
            set
            {
                if (SetProperty(ref _isCtrlPressed, value))
                {
                    RaisePropertyChanged(nameof(ShortcutText));
                }
            }
        }

        public bool IsAltPressed
        {
            get => _isAltPressed;
            set
            {
                if (SetProperty(ref _isAltPressed, value))
                {
                    RaisePropertyChanged(nameof(ShortcutText));
                }
            }
        }

        public bool IsShiftPressed
        {
            get => _isShiftPressed;
            set
            {
                if (SetProperty(ref _isShiftPressed, value))
                {
                    RaisePropertyChanged(nameof(ShortcutText));
                }
            }
        }

        public bool IsWindowsPressed
        {
            get => _isWindowsPressed;
            set
            {
                if (SetProperty(ref _isWindowsPressed, value))
                {
                    RaisePropertyChanged(nameof(ShortcutText));
                }
            }
        }

        public Key SelectedKey
        {
            get => _selectedKey;
            private set
            {
                if (SetProperty(ref _selectedKey, value))
                {
                    RaisePropertyChanged(nameof(ShortcutText));
                    SaveCommand?.RaiseCanExecuteChanged();
                }
            }
        }

        private bool CanSave()
        {
            return SelectedKey != Key.None;
        }

        public string ShortcutText
        {
            get
            {
                var modifiers = new List<string>();
                if (IsCtrlPressed) modifiers.Add("Ctrl");
                if (IsAltPressed) modifiers.Add("Alt");
                if (IsShiftPressed) modifiers.Add("Shift");
                if (IsWindowsPressed) modifiers.Add("Win");

                var keyText = SelectedKey == Key.None ? "None" : SelectedKey.ToString();
                return modifiers.Count > 0
                    ? $"{string.Join(" + ", modifiers)} + {keyText}"
                    : keyText;
            }
        }

        public DelegateCommand<KeyEventArgs> KeyDownCommand { get; private set; }
        public DelegateCommand SaveCommand { get; private set; }
        public DelegateCommand CancelCommand { get; private set; }

        public KeyboardShortcutSettingDialogViewModel()
            : this(Key.None)
        {
        }

        public KeyboardShortcutSettingDialogViewModel(Key initialKey)
        {
            KeyDownCommand = new DelegateCommand<KeyEventArgs>(ExecuteKeyDown);
            SaveCommand = new DelegateCommand(ExecuteSave, CanSave);
            CancelCommand = new DelegateCommand(ExecuteCancel);

            // 初期値の設定
            if (initialKey != Key.None)
            {
                SelectedKey = initialKey;
            }
        }

        private void ExecuteKeyDown(KeyEventArgs e)
        {
            if (e.Key == Key.System)
            {
                // Alt keyが押された場合は、実際のキーはSystemKeyCodeにある
                SelectedKey = e.SystemKey;
            }
            else
            {
                SelectedKey = e.Key == Key.LeftCtrl || e.Key == Key.RightCtrl ||
                             e.Key == Key.LeftAlt || e.Key == Key.RightAlt ||
                             e.Key == Key.LeftShift || e.Key == Key.RightShift ||
                             e.Key == Key.LWin || e.Key == Key.RWin
                             ? Key.None : e.Key;
            }

            e.Handled = true;
        }

        private void ExecuteSave()
        {
            DialogResult = true;
        }

        private void ExecuteCancel()
        {
            DialogResult = false;
        }

        private bool? _dialogResult;
        public bool? DialogResult
        {
            get => _dialogResult;
            private set => SetProperty(ref _dialogResult, value);
        }
    }
}
