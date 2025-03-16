using System.Threading;
using System.Threading.Tasks;
using Illustra.Views;

namespace Illustra.Helpers
{
    /// <summary>
    /// デフォルトのサムネイル処理サービス実装
    /// </summary>
    public class DefaultThumbnailProcessor : IThumbnailProcessorService
    {
        private readonly ThumbnailListControl _control;

        public DefaultThumbnailProcessor(ThumbnailListControl control)
        {
            _control = control;
        }

        /// <summary>
        /// 指定されたインデックスのサムネイルを生成します
        /// </summary>
        public async Task CreateThumbnailAsync(int index, CancellationToken cancellationToken)
        {
            // 既存のThumbnailLoaderHelperのCreateThumbnailAsyncメソッドを呼び出す
            await _control.CreateThumbnailAsync(index, cancellationToken);
        }
    }
}