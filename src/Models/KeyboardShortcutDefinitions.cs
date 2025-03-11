using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Illustra.Functions;

namespace Illustra.Models
{
    public static class KeyboardShortcutDefinitions
    {
        public static List<KeyboardShortcutMetadata> AllShortcuts =
        [
            new() { FunctionId = FuncId.NavigateUp.Value, ResourceKey = "FuncId_NavigateUp", DefaultKeys = new List<Key> { Key.Up } },
            new() { FunctionId = FuncId.NavigateDown.Value, ResourceKey = "FuncId_NavigateDown", DefaultKeys = new List<Key> { Key.Down } },
            new() { FunctionId = FuncId.NavigateLeft.Value, ResourceKey = "FuncId_NavigateLeft", DefaultKeys = new List<Key> { Key.Left } },
            new() { FunctionId = FuncId.NavigateRight.Value, ResourceKey = "FuncId_NavigateRight", DefaultKeys = new List<Key> { Key.Right } },
            new() { FunctionId = FuncId.Rating0.Value, ResourceKey = "FuncId_Rating0", DefaultKeys = new List<Key> { Key.D0, Key.NumPad0 } },
            new() { FunctionId = FuncId.Rating1.Value, ResourceKey = "FuncId_Rating1", DefaultKeys = new List<Key> { Key.D1, Key.NumPad1 } },
            new() { FunctionId = FuncId.Rating2.Value, ResourceKey = "FuncId_Rating2", DefaultKeys = new List<Key> { Key.D2, Key.NumPad2 } },
            new() { FunctionId = FuncId.Rating3.Value, ResourceKey = "FuncId_Rating3", DefaultKeys = new List<Key> { Key.D3, Key.NumPad3 } },
            new() { FunctionId = FuncId.Rating4.Value, ResourceKey = "FuncId_Rating4", DefaultKeys = new List<Key> { Key.D4, Key.NumPad4 } },
            new() { FunctionId = FuncId.Rating5.Value, ResourceKey = "FuncId_Rating5", DefaultKeys = new List<Key> { Key.D5, Key.NumPad5 } },
            new() { FunctionId = FuncId.Delete.Value, ResourceKey = "FuncId_Delete", DefaultKeys = new List<Key> { Key.Delete, Key.D } },
            new() { FunctionId = FuncId.ToggleViewer.Value, ResourceKey = "FuncId_ToggleViewer", DefaultKeys = new List<Key> { Key.Enter, Key.Return } },
            new() { FunctionId = FuncId.SelectAll.Value, ResourceKey = "FuncId_SelectAll", DefaultKeys = new List<Key> { Key.A }, DefaultModifiers = new Dictionary<Key, ModifierKeys> { { Key.A, ModifierKeys.Control } } },
        ];

        // 初期設定生成
        public static ObservableCollection<KeyboardShortcutSetting> GetDefaultSettings()
        {
            return new ObservableCollection<KeyboardShortcutSetting>(
                AllShortcuts.Select(meta => new KeyboardShortcutSetting
                {
                    FunctionId = meta.FunctionId,
                    Keys = meta.DefaultKeys,
                    Modifiers = meta.DefaultModifiers
                })
            );
        }

        // 表示用の文言取得
        public static string GetFunctionName(string functionId)
        {
            var meta = AllShortcuts.FirstOrDefault(m => m.FunctionId == functionId);
            var stringId = meta?.ResourceKey;
            return stringId != null ? (string)Application.Current.Resources[stringId] : functionId;
        }
    }
}
