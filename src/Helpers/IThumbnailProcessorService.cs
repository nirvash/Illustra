using System.Threading;
using System.Threading.Tasks;

namespace Illustra.Helpers
{
    /// <summary>
    /// サムネイル処理サービスのインターフェース
    /// </summary>
    public interface IThumbnailProcessorService
    {
        /// <summary>
        /// 指定されたインデックスのサムネイルを非同期で作成します
        /// </summary>
        /// <param name="index">サムネイルを作成するアイテムのインデックス</param>
        /// <param name="cancellationToken">キャンセルトークン</param>
        /// <returns>処理の完了を表すタスク</returns>
        Task CreateThumbnailAsync(int index, CancellationToken cancellationToken);
    }
}