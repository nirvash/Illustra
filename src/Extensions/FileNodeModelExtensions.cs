using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;
using Illustra.Models;

namespace Illustra.Extensions
{
    /// <summary>
    /// FileNodeModelの拡張メソッド
    /// </summary>
    public static class FileNodeModelExtensions
    {
        // サムネイル状態を保持する静的ディクショナリ
        private static readonly ConditionalWeakTable<FileNodeModel, ThumbnailState> _thumbnailStates =
            new ConditionalWeakTable<FileNodeModel, ThumbnailState>();

        // サムネイル状態を保持するクラス
        private class ThumbnailState
        {
            public bool HasThumbnail { get; set; }
            public bool IsLoadingThumbnail { get; set; }
            public BitmapSource? Thumbnail { get; set; }
        }

        /// <summary>
        /// サムネイルが存在するかどうかを取得します
        /// </summary>
        public static bool HasThumbnail(this FileNodeModel model)
        {
            var state = _thumbnailStates.GetOrCreateValue(model);
            return state.HasThumbnail;
        }

        /// <summary>
        /// サムネイルが読み込み中かどうかを取得します
        /// </summary>
        public static bool IsLoadingThumbnail(this FileNodeModel model)
        {
            var state = _thumbnailStates.GetOrCreateValue(model);
            return state.IsLoadingThumbnail;
        }

        /// <summary>
        /// サムネイルが存在するかどうかを設定します
        /// </summary>
        public static void SetHasThumbnail(this FileNodeModel model, bool value)
        {
            var state = _thumbnailStates.GetOrCreateValue(model);
            state.HasThumbnail = value;
        }

        /// <summary>
        /// サムネイルが読み込み中かどうかを設定します
        /// </summary>
        public static void SetIsLoadingThumbnail(this FileNodeModel model, bool value)
        {
            var state = _thumbnailStates.GetOrCreateValue(model);
            state.IsLoadingThumbnail = value;
        }

        /// <summary>
        /// サムネイル画像を取得します
        /// </summary>
        public static BitmapSource? GetThumbnail(this FileNodeModel model)
        {
            var state = _thumbnailStates.GetOrCreateValue(model);
            return state.Thumbnail;
        }

        /// <summary>
        /// サムネイル画像を設定します
        /// </summary>
        public static void SetThumbnail(this FileNodeModel model, BitmapSource? value)
        {
            var state = _thumbnailStates.GetOrCreateValue(model);
            state.Thumbnail = value;
            state.HasThumbnail = value != null;
        }
    }
}