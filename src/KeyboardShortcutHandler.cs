using System;
using System.Collections.Generic;
using System.Windows.Input;
using System.Collections.ObjectModel;
using Illustra.ViewModels;
using Illustra.Helpers;
using System.Text.Json;
using Illustra.Models;
using Illustra.Functions;

namespace Illustra
{
    using Illustra.Models;

    public class KeyboardShortcutHandler
    {
        // シングルトンインスタンス
        private static KeyboardShortcutHandler _instance;
        public static KeyboardShortcutHandler Instance => _instance ?? (_instance = new KeyboardShortcutHandler());

        private ObservableCollection<KeyboardShortcutModel> _shortcuts;

        private KeyboardShortcutHandler()
        {
            Initialize();
        }

        public void Initialize()
        {
            _shortcuts = Models.KeyboardShortcutModel.LoadShortcuts();
        }

        // キー入力を処理するメソッド
        public bool IsShortcutMatch(Functions.FuncId functionId, Key key)
        {
            // 該当する機能IDのショートカットを検索
            var shortcut = _shortcuts.FirstOrDefault(s => s.FunctionId == functionId);

            if (shortcut == null)
                return false;

            // 現在の修飾キーの状態を取得
            var currentModifiers = GetCurrentModifiers();

            // キーと修飾キーの組み合わせが一致するか確認
            foreach (var registeredKey in shortcut.Keys)
            {
                if (registeredKey == key)
                {
                    var expectedModifiers = shortcut.Modifiers.TryGetValue(registeredKey, out var mods) ? mods : ModifierKeys.None;
                    return expectedModifiers == currentModifiers;
                }
            }

            return false;
        }

        public bool IsModifierKey(Key key)
        {
            return key == Key.LeftCtrl || key == Key.RightCtrl ||
                   key == Key.LeftAlt || key == Key.RightAlt ||
                   key == Key.LeftShift || key == Key.RightShift ||
                   key == Key.LWin || key == Key.RWin;
        }

        private ModifierKeys GetCurrentModifiers()
        {
            // 現在の修飾キーの状態を取得
            var currentModifiers = ModifierKeys.None;
            if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl))
                currentModifiers |= ModifierKeys.Control;
            if (Keyboard.IsKeyDown(Key.LeftAlt) || Keyboard.IsKeyDown(Key.RightAlt))
                currentModifiers |= ModifierKeys.Alt;
            if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift))
                currentModifiers |= ModifierKeys.Shift;
            if (Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin))
                currentModifiers |= ModifierKeys.Windows;

            return currentModifiers;
        }


        public string GetShortcutText(FuncId functionId)
        {
            var shortcut = _shortcuts.FirstOrDefault(s => s.FunctionId == functionId);
            if (shortcut == null || !shortcut.Keys.Any())
                return string.Empty;

            var key = shortcut.Keys.First(); // 最初のキーを使用
            var modifiers = shortcut.Modifiers.TryGetValue(key, out var mods) ? mods : ModifierKeys.None;

            var text = new List<string>();
            if (modifiers.HasFlag(ModifierKeys.Control)) text.Add("Ctrl");
            if (modifiers.HasFlag(ModifierKeys.Alt)) text.Add("Alt");
            if (modifiers.HasFlag(ModifierKeys.Shift)) text.Add("Shift");
            if (modifiers.HasFlag(ModifierKeys.Windows)) text.Add("Win");

            text.Add(key.ToString());

            return string.Join("+", text);
        }

        // ショートカットの設定が変更されたときに呼び出されるメソッド
        public void ReloadShortcuts()
        {
            _shortcuts.Clear();
            var loadedShortcuts = Models.KeyboardShortcutModel.LoadShortcuts();
            foreach (var shortcut in loadedShortcuts)
            {
                _shortcuts.Add(shortcut);
            }
        }
    }
}
