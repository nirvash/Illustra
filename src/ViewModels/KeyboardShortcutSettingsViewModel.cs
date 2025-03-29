using Prism.Commands;
using Prism.Mvvm;
using System.Windows;
using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Controls;
using System.Linq;
using System.Text.Json;
using Illustra.Helpers;
using System.Globalization;
using System.Windows.Data;
using Illustra.Functions;
using Illustra.Models;

namespace Illustra.ViewModels
{
    public class KeyToStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is Key key ? key.ToString() : string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class FuncIdToStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is FuncId funcId ? funcId.Value : string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public interface IRequestClose
    {
        event EventHandler CloseRequested;
    }

    public class KeyboardShortcutSettingsViewModel : BindableBase, IRequestClose
    {
        public event EventHandler CloseRequested;

        public ObservableCollection<KeyboardShortcutModel> Shortcuts { get; private set; }

        public DelegateCommand<object> SaveCommand { get; private set; }
        public DelegateCommand<object> CancelCommand { get; private set; }
        public DelegateCommand<KeyboardShortcutModel> AddKeyCommand { get; private set; }
        public DelegateCommand<KeyWrapper> RemoveKeyCommand { get; private set; }
        public DelegateCommand<KeyWrapper> EditKeyCommand { get; private set; }
        public DelegateCommand ResetToDefaultCommand { get; private set; }

        public KeyboardShortcutSettingsViewModel()
        {
            Shortcuts = [];
            SaveCommand = new DelegateCommand<object>(_ => ExecuteSave());
            CancelCommand = new DelegateCommand<object>(_ => ExecuteCancel());
            AddKeyCommand = new DelegateCommand<KeyboardShortcutModel>(AddKey);
            RemoveKeyCommand = new DelegateCommand<KeyWrapper>(RemoveKey);
            EditKeyCommand = new DelegateCommand<KeyWrapper>(EditKey);
            ResetToDefaultCommand = new DelegateCommand(ExecuteResetToDefault);

            LoadShortcutsFromSettings();
        }

        private void ExecuteResetToDefault()
        {
            var result = MessageBox.Show(
                (string)Application.Current.FindResource("String_Settings_ResetConfirm"),
                (string)Application.Current.FindResource("String_Settings_ResetConfirmTitle"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                Shortcuts.Clear();
                var defaultShortcuts = KeyboardShortcutModel.GetDefaultShortcuts();
                foreach (var shortcut in defaultShortcuts)
                {
                    Shortcuts.Add(shortcut);
                }
                SaveShortcutsToSettings();
            }
        }

        private void AddKey(KeyboardShortcutModel shortcut)
        {
            if (shortcut != null)
            {
                var dialog = new Views.KeyboardShortcutSettingDialog(Key.None)
                {
                    Owner = Application.Current.MainWindow
                };

                if (dialog.ShowDialog() == true)
                {
                    var viewModel = dialog.DataContext as KeyboardShortcutSettingDialogViewModel;
                    if (viewModel != null && dialog.SelectedKey != Key.None)
                    {
                        var modifiers = ModifierKeys.None;
                        if (viewModel.IsCtrlPressed) modifiers |= ModifierKeys.Control;
                        if (viewModel.IsAltPressed) modifiers |= ModifierKeys.Alt;
                        if (viewModel.IsShiftPressed) modifiers |= ModifierKeys.Shift;
                        if (viewModel.IsWindowsPressed) modifiers |= ModifierKeys.Windows;

                        var newKey = dialog.SelectedKey;

                        // 重複チェックは対象グループごとに行う必要がある。今はチェックしない
                        /*
                        if (IsDuplicate(newKey, modifiers, shortcut))
                        {
                            MessageBox.Show(
                                string.Format((string)Application.Current.FindResource("String_Settings_ShortcutDuplicateWarning"), $"{modifiers}+{newKey}"),
                                (string)Application.Current.FindResource("String_WarningTitle"),
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning);
                            return; // 重複があれば追加しない
                        }
                        */
                        // 重複がなければ追加
                        shortcut.Keys.Add(newKey);
                        shortcut.UpdateModifiers(newKey, modifiers);
                        SaveShortcutsToSettings();
                    }
                }
            }
        }

        private void RemoveKey(KeyWrapper wrapper)
        {
            if (wrapper != null && wrapper.ParentShortcut.Keys.Contains(wrapper.Key))
            {
                wrapper.ParentShortcut.Modifiers.Remove(wrapper.Key);
                wrapper.ParentShortcut.Keys.Remove(wrapper.Key);
                SaveShortcutsToSettings();
            }
        }

        private void EditKey(KeyWrapper wrapper)
        {
            if (wrapper != null)
            {
                var dialog = new Views.KeyboardShortcutSettingDialog(wrapper.Key)
                {
                    Owner = Application.Current.MainWindow
                };

                // 現在の修飾キーを設定
                var viewModel = dialog.DataContext as KeyboardShortcutSettingDialogViewModel;
                if (viewModel != null)
                {
                    var modifiers = wrapper.Modifiers;
                    viewModel.IsCtrlPressed = (modifiers & ModifierKeys.Control) == ModifierKeys.Control;
                    viewModel.IsAltPressed = (modifiers & ModifierKeys.Alt) == ModifierKeys.Alt;
                    viewModel.IsShiftPressed = (modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
                    viewModel.IsWindowsPressed = (modifiers & ModifierKeys.Windows) == ModifierKeys.Windows;
                }

                if (dialog.ShowDialog() == true)
                {
                    var modifiers = ModifierKeys.None;
                    if (viewModel.IsCtrlPressed) modifiers |= ModifierKeys.Control;
                    if (viewModel.IsAltPressed) modifiers |= ModifierKeys.Alt;
                    if (viewModel.IsShiftPressed) modifiers |= ModifierKeys.Shift;
                    if (viewModel.IsWindowsPressed) modifiers |= ModifierKeys.Windows;

                    var shortcut = wrapper.ParentShortcut;
                    var oldKey = wrapper.Key;
                    var newKey = dialog.SelectedKey;
                    // 重複チェックは対象グループごとに行う必要がある。今はチェックしない
                    /*
                                        // 重複チェック (編集前のキーは除外)
                                        if (IsDuplicate(newKey, modifiers, shortcut, oldKey))
                                        {
                                            MessageBox.Show(
                                                string.Format((string)Application.Current.FindResource("String_Settings_ShortcutDuplicateWarning"), $"{modifiers}+{newKey}"),
                                                (string)Application.Current.FindResource("String_WarningTitle"),
                                                MessageBoxButton.OK,
                                                MessageBoxImage.Warning);
                                            return; // 重複があれば編集しない
                                        }
                    */
                    // 重複がなければ編集
                    shortcut.Modifiers.Remove(oldKey);
                    shortcut.ReplaceKey(oldKey, newKey);
                    shortcut.UpdateModifiers(newKey, modifiers);
                    SaveShortcutsToSettings();
                }
            }
        }

        private void SaveShortcutsToSettings()
        {
            // 保存とハンドラーへの通知
            KeyboardShortcutModel.SaveShortcuts(Shortcuts);
            KeyboardShortcutHandler.Instance.ReloadShortcuts();
        }

        private void LoadShortcutsFromSettings()
        {
            Shortcuts.Clear();
            foreach (var shortcut in KeyboardShortcutModel.LoadShortcuts())
            {
                Shortcuts.Add(shortcut);
            }
        }

        private void ExecuteSave()
        {
            SaveShortcutsToSettings();
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }

        private bool IsDuplicate(Key key, ModifierKeys modifiers, KeyboardShortcutModel currentShortcut, Key? keyToRemove = null)
        {
            foreach (var shortcut in Shortcuts)
            {
                foreach (var existingKey in shortcut.Keys)
                {
                    // EditKeyの場合、編集前のキーはチェック対象外
                    if (keyToRemove.HasValue && shortcut == currentShortcut && existingKey == keyToRemove.Value)
                    {
                        continue;
                    }

                    shortcut.Modifiers.TryGetValue(existingKey, out var existingModifiers); // default is None

                    if (existingKey == key && existingModifiers == modifiers)
                    {
                        return true; // 重複あり
                    }
                }
            }
            return false; // 重複なし
        }


        private void ExecuteCancel()
        {
            LoadShortcutsFromSettings();
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }
    }
}
