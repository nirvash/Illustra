using System;
using System.Collections.Generic;
using System.Windows.Media.Imaging;
using Illustra.Models;

namespace Illustra.Helpers.Interfaces
{
    /// <summary>
    /// 画像キャッシュのインターフェース
    /// </summary>
    public interface IImageCache
    {
        /// <summary>
        /// 画像を取得する。キャッシュにない場合は読み込んでキャッシュに追加する。
        /// </summary>
        /// <param name="path">画像ファイルのパス</param>
        /// <returns>画像データ</returns>
        BitmapSource GetImage(string path);

        /// <summary>
        /// キャッシュに画像が存在するか確認
        /// </summary>
        /// <param name="path">画像ファイルのパス</param>
        /// <returns>キャッシュに存在する場合はtrue</returns>
        bool HasImage(string path);

        /// <summary>
        /// キャッシュの更新（ウィンドウの移動）
        /// </summary>
        /// <param name="files">ファイルノードのリスト</param>
        /// <param name="currentIndex">現在の画像のインデックス</param>
        void UpdateCache(List<FileNodeModel> files, int currentIndex);

        /// <summary>
        /// キャッシュのクリア
        /// </summary>
        void Clear();

        /// <summary>
        /// 現在のキャッシュ状態
        /// </summary>
        IReadOnlyDictionary<string, BitmapSource> CachedItems { get; }
    }
}
