using System;
using System.Collections.Generic;
using System.Windows.Input;
using System.Collections.ObjectModel;
using Illustra.ViewModels;
using Illustra.Helpers;
using System.Text.Json;
using Illustra.Models;

namespace Illustra
{
    namespace Functions
    {
        /// <summary>
        /// 型安全なファンクションID
        /// </summary>
        public readonly struct FuncId : System.IEquatable<FuncId>
        {
            public string Value { get; }

            public FuncId(string value) => Value = value;
            public static readonly FuncId None = new FuncId(string.Empty);

            // ナビゲーション
            public static readonly FuncId NavigateUp = new FuncId("nav_up");
            public static readonly FuncId NavigateDown = new FuncId("nav_down");
            public static readonly FuncId NavigateLeft = new FuncId("nav_left");
            public static readonly FuncId NavigateRight = new FuncId("nav_right");

            // レーティング
            public static readonly FuncId Rating0 = new FuncId("rating_0");
            public static readonly FuncId Rating1 = new FuncId("rating_1");
            public static readonly FuncId Rating2 = new FuncId("rating_2");
            public static readonly FuncId Rating3 = new FuncId("rating_3");
            public static readonly FuncId Rating4 = new FuncId("rating_4");
            public static readonly FuncId Rating5 = new FuncId("rating_5");

            // レーティングID一覧
            public static readonly IReadOnlyDictionary<int, FuncId> Ratings = new Dictionary<int, FuncId>
            {
                { 0, Rating0 },
                { 1, Rating1 },
                { 2, Rating2 },
                { 3, Rating3 },
                { 4, Rating4 },
                { 5, Rating5 }
            };

            // ファイル操作
            public static readonly FuncId Delete = new FuncId("delete");

            // 決定操作
            public static readonly FuncId ToggleViewer = new FuncId("select");

            // 選択操作
            public static readonly FuncId SelectAll = new FuncId("select_all");

            // リスト移動操作
            public static readonly FuncId MoveToStart = new FuncId("move_to_start");
            public static readonly FuncId MoveToEnd = new FuncId("move_to_end");

            // 暗黙変換
            public static implicit operator string(FuncId id) => id.Value;
            public static explicit operator FuncId(string value) => new FuncId(value);

            // Equals と HashCode
            public bool Equals(FuncId other) => Value == other.Value;
            public override bool Equals(object? obj) => obj is FuncId other && Equals(other);
            public override int GetHashCode() => Value.GetHashCode();
            public override string ToString() => Value;
        }
    }

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
