using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Illustra.Functions;

namespace Illustra.Functions
{
    /// <summary>
    /// 型安全なファンクションID
    /// </summary>
    public readonly struct FuncId : IEquatable<FuncId>
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

        // レーティングフィルタ
        public static readonly FuncId FilterRating0 = new FuncId("filter_rating_0");
        public static readonly FuncId FilterRating1 = new FuncId("filter_rating_1");
        public static readonly FuncId FilterRating2 = new FuncId("filter_rating_2");
        public static readonly FuncId FilterRating3 = new FuncId("filter_rating_3");
        public static readonly FuncId FilterRating4 = new FuncId("filter_rating_4");
        public static readonly FuncId FilterRating5 = new FuncId("filter_rating_5");

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
        public static readonly FuncId ToggleViewer = new FuncId("toggle_viewer");

        // 選択操作
        public static readonly FuncId SelectAll = new FuncId("select_all");

        // リスト移動操作
        public static readonly FuncId MoveToStart = new FuncId("move_to_start");
        public static readonly FuncId MoveToEnd = new FuncId("move_to_end");

        // ビューワー操作
        public static readonly FuncId ToggleFullScreen = new FuncId("toggle_fullscreen");
        public static readonly FuncId TogglePropertyPanel = new FuncId("toggle_property_panel");
        public static readonly FuncId CloseViewer = new FuncId("close_viewer");
        public static readonly FuncId PreviousImage = new FuncId("previous_image");
        public static readonly FuncId NextImage = new FuncId("next_image");
        public static readonly FuncId ToggleSlideshow = new FuncId("toggle_slideshow");
        public static readonly FuncId IncreaseSlideshowInterval = new FuncId("increase_slideshow_interval");
        public static readonly FuncId DecreaseSlideshowInterval = new FuncId("decrease_slideshow_interval");

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

namespace Illustra.Models
{
    public static class KeyboardShortcutDefinitions
    {
        public static List<KeyboardShortcutMetadata> AllShortcuts = new()
        {
            // ナビゲーション
            new() { FunctionId = FuncId.NavigateUp.Value, ResourceKey = "String_Function_NavigateUp", DefaultKeys = new List<Key> { Key.Up } },
            new() { FunctionId = FuncId.NavigateDown.Value, ResourceKey = "String_Function_NavigateDown", DefaultKeys = new List<Key> { Key.Down } },
            new() { FunctionId = FuncId.NavigateLeft.Value, ResourceKey = "String_Function_NavigateLeft", DefaultKeys = new List<Key> { Key.Left } },
            new() { FunctionId = FuncId.NavigateRight.Value, ResourceKey = "String_Function_NavigateRight", DefaultKeys = new List<Key> { Key.Right } },

            // レーティング
            new() { FunctionId = FuncId.Rating0.Value, ResourceKey = "String_Function_Rating0", DefaultKeys = new List<Key> { Key.D0, Key.NumPad0, Key.X } },
            new() { FunctionId = FuncId.Rating1.Value, ResourceKey = "String_Function_Rating1", DefaultKeys = new List<Key> { Key.D1, Key.NumPad1 } },
            new() { FunctionId = FuncId.Rating2.Value, ResourceKey = "String_Function_Rating2", DefaultKeys = new List<Key> { Key.D2, Key.NumPad2 } },
            new() { FunctionId = FuncId.Rating3.Value, ResourceKey = "String_Function_Rating3", DefaultKeys = new List<Key> { Key.D3, Key.NumPad3 } },
            new() { FunctionId = FuncId.Rating4.Value, ResourceKey = "String_Function_Rating4", DefaultKeys = new List<Key> { Key.D4, Key.NumPad4 } },
            new() { FunctionId = FuncId.Rating5.Value, ResourceKey = "String_Function_Rating5", DefaultKeys = new List<Key> { Key.D5, Key.NumPad5, Key.Z } },

            // レーティングフィルタ
            new() { FunctionId = FuncId.FilterRating0.Value, ResourceKey = "String_Function_FilterRating0", DefaultKeys = new List<Key>() },
            new() { FunctionId = FuncId.FilterRating1.Value, ResourceKey = "String_Function_FilterRating1", DefaultKeys = new List<Key>() },
            new() { FunctionId = FuncId.FilterRating2.Value, ResourceKey = "String_Function_FilterRating2", DefaultKeys = new List<Key>() },
            new() { FunctionId = FuncId.FilterRating3.Value, ResourceKey = "String_Function_FilterRating3", DefaultKeys = new List<Key>() },
            new() { FunctionId = FuncId.FilterRating4.Value, ResourceKey = "String_Function_FilterRating4", DefaultKeys = new List<Key>() },
            new() { FunctionId = FuncId.FilterRating5.Value, ResourceKey = "String_Function_FilterRating5", DefaultKeys = new List<Key>() },

            // ファイル操作
            new() { FunctionId = FuncId.Delete.Value, ResourceKey = "String_Function_Delete", DefaultKeys = new List<Key> { Key.Delete } },

            // 基本操作
            new() { FunctionId = FuncId.ToggleViewer.Value, ResourceKey = "String_Function_ToggleViewer", DefaultKeys = new List<Key> { Key.Return } },
            new() { FunctionId = FuncId.SelectAll.Value, ResourceKey = "String_Function_SelectAll", DefaultKeys = new List<Key> { Key.A }, DefaultModifiers = new Dictionary<Key, ModifierKeys> { { Key.A, ModifierKeys.Control } } },

            // ビューワー操作
            new() { FunctionId = FuncId.ToggleFullScreen.Value, ResourceKey = "String_Function_ToggleFullScreen", DefaultKeys = new List<Key> { Key.F11 } },
            new() { FunctionId = FuncId.TogglePropertyPanel.Value, ResourceKey = "String_Function_TogglePropertyPanel", DefaultKeys = new List<Key> { Key.P } },
            new() { FunctionId = FuncId.ToggleSlideshow.Value, ResourceKey = "String_Function_ToggleSlideshow", DefaultKeys = new List<Key> { Key.S } },
            new() { FunctionId = FuncId.IncreaseSlideshowInterval.Value, ResourceKey = "String_Function_IncreaseSlideshowInterval", DefaultKeys = new List<Key> { Key.OemPlus, Key.Add } },
            new() { FunctionId = FuncId.DecreaseSlideshowInterval.Value, ResourceKey = "String_Function_DecreaseSlideshowInterval", DefaultKeys = new List<Key> { Key.OemMinus, Key.Subtract } },
            new() { FunctionId = FuncId.CloseViewer.Value, ResourceKey = "String_Function_CloseViewer", DefaultKeys = new List<Key> { Key.Escape, Key.Return } },
            new() { FunctionId = FuncId.PreviousImage.Value, ResourceKey = "String_Function_PreviousImage", DefaultKeys = new List<Key> { Key.Left } },
            new() { FunctionId = FuncId.NextImage.Value, ResourceKey = "String_Function_NextImage", DefaultKeys = new List<Key> { Key.Right } },

            // リスト移動
            new() { FunctionId = FuncId.MoveToStart.Value, ResourceKey = "String_Function_MoveToStart", DefaultKeys = new List<Key> { Key.Home } },
            new() { FunctionId = FuncId.MoveToEnd.Value, ResourceKey = "String_Function_MoveToEnd", DefaultKeys = new List<Key> { Key.End } },
        };

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
