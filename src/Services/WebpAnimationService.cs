using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using System.Windows;
using System.Runtime.InteropServices;
using System.Threading;
using System.Diagnostics;
using Illustra.Helpers;
using System.Linq; // Select, ToListのため

namespace Illustra.Services
{
    public class WebpAnimationService : IWebpAnimationService
    {
        // Demuxer instance and related info
        private IntPtr _demuxer = IntPtr.Zero;
        private int _canvasWidth;
        private int _canvasHeight;
        private int _frameCount;
        private int _loopCount;

        // Pinned file data (remains the same)

        // Pinned file data
        private byte[] _fileData;
        private GCHandle _fileDataHandle;
        private IntPtr _pinnedDataPtr = IntPtr.Zero;
        private UIntPtr _dataSize = UIntPtr.Zero;
        private string _currentFilePath = null;

        // Cancellation for old StartDecodingAsync (might be removed later)
        private CancellationTokenSource _cts; // Keep for potential future async operations cancellation? Or remove if unused.
                                              // SemaphoreSlim _decoderSemaphore removed
        public async Task InitializeAsync(string filePath)
        {
            if (filePath == _currentFilePath && _demuxer != IntPtr.Zero)
            {
                return;
            }

            try
            {
                // Clean up existing resources
                DisposeInternal();

                // Load and pin file data
                _currentFilePath = filePath;
                _fileData = await File.ReadAllBytesAsync(filePath);
                _fileDataHandle = GCHandle.Alloc(_fileData, GCHandleType.Pinned);
                _pinnedDataPtr = _fileDataHandle.AddrOfPinnedObject();
                _dataSize = (UIntPtr)_fileData.Length;

                await Task.Run(() =>
                {
                    try
                    {
                        // Initialize WebPData
                        var webpData = new LibWebP.WebPData();
                        webpData.bytes = _pinnedDataPtr;
                        webpData.size = _dataSize;

                        // Create demuxer
                        _demuxer = LibWebP.WebPDemux(ref webpData);
                        if (_demuxer == IntPtr.Zero)
                        {
                            throw new InvalidOperationException("デマルチプレクサの作成に失敗しました。");
                        }

                        // Get basic information
                        var width = LibWebP.WebPDemuxGetI(_demuxer, LibWebP.WebPFormatFeature.WEBP_FF_CANVAS_WIDTH);
                        var height = LibWebP.WebPDemuxGetI(_demuxer, LibWebP.WebPFormatFeature.WEBP_FF_CANVAS_HEIGHT);
                        _canvasWidth = checked((int)width);
                        _canvasHeight = checked((int)height);
                        if (_canvasWidth <= 0 || _canvasHeight <= 0)
                        {
                            throw new InvalidOperationException($"キャンバスサイズの取得に失敗しました。width:{width}, height:{height}");
                        }

                        var frameCount = LibWebP.WebPDemuxGetI(_demuxer, LibWebP.WebPFormatFeature.WEBP_FF_FRAME_COUNT);
                        _frameCount = checked((int)frameCount);
                        if (_frameCount <= 0)
                        {
                            throw new InvalidOperationException($"フレーム数の取得に失敗しました。value:{frameCount}");
                        }

                        var loopCount = LibWebP.WebPDemuxGetI(_demuxer, LibWebP.WebPFormatFeature.WEBP_FF_LOOP_COUNT);
                        _loopCount = checked((int)loopCount);

                        // Get format flags to verify animation support
                        var flags = LibWebP.WebPDemuxGetI(_demuxer, LibWebP.WebPFormatFeature.WEBP_FF_FORMAT_FLAGS);
                        if ((flags & (uint)LibWebP.WebPFeatureFlags.ANIMATION_FLAG) == 0 && _frameCount > 1)
                        {
                            throw new InvalidOperationException($"アニメーション形式ではありません。flags:{flags:X}");
                        }
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidOperationException("WebPデマルチプレクサの初期化中にエラーが発生しました。", ex);
                    }
                });

                LogHelper.LogWithTimestamp(
                    $"WebPアニメーション初期化完了 - {Path.GetFileName(filePath)}, " +
                    $"{_frameCount}フレーム, {_canvasWidth}x{_canvasHeight}, " +
                    $"ループ{_loopCount}回",
                    LogHelper.Categories.Performance
                );
            }
            catch (Exception ex)
            {
                DisposeInternal();
                throw new InvalidOperationException("WebPファイルの読み込みに失敗しました。", ex);
            }
        }
        // Remove extra closing brace

        // Returns cached features after initialization
        public LibWebP.WebPBitstreamFeatures GetFeatures()
        {
            if (_demuxer == IntPtr.Zero) throw new InvalidOperationException("Service not initialized. Call InitializeAsync first.");
            return new LibWebP.WebPBitstreamFeatures
            {
                width = _canvasWidth,
                height = _canvasHeight,
                has_animation = _frameCount > 1 ? 1 : 0,
                has_alpha = 1, // Assume alpha for now, could check flags if needed
                format = 0 // Format info might require deeper inspection
            };
        }

        // Public Dispose method
        public void Dispose()
        {
            DisposeInternal(); // Call the internal method that handles everything
            GC.SuppressFinalize(this); // Prevent finalizer from running
        }

        // Internal comprehensive dispose method
        private void DisposeInternal()
        {
            try
            {
                // キャンセルトークンの処理
                if (_cts != null)
                {
                    _cts.Cancel();
                    _cts.Dispose();
                    _cts = null;
                }

                // Demuxerの解放
                if (_demuxer != IntPtr.Zero)
                {
                    try
                    {
                        LibWebP.WebPDemuxDelete(_demuxer);
                    }
                    catch (Exception ex)
                    {
                        LogHelper.LogWithTimestamp(
                            $"Demuxerの解放中にエラーが発生: {ex.Message}",
                            LogHelper.Categories.Error
                        );
                    }
                    finally
                    {
                        _demuxer = IntPtr.Zero;
                    }
                }

                // ピン留めされたメモリの解放
                if (_fileDataHandle.IsAllocated)
                {
                    try
                    {
                        _fileDataHandle.Free();
                    }
                    catch (Exception ex)
                    {
                        LogHelper.LogWithTimestamp(
                            $"ピン留めメモリの解放中にエラーが発生: {ex.Message}",
                            LogHelper.Categories.Error
                        );
                    }
                }

                // その他のリソースのクリーンアップ
                _pinnedDataPtr = IntPtr.Zero;
                _dataSize = UIntPtr.Zero;
                _fileData = null;
                _currentFilePath = null;
                // 不要な行を削除
                _canvasWidth = 0;
                _canvasHeight = 0;
                _frameCount = 0;
                _loopCount = 0;

                LogHelper.LogWithTimestamp(
                    "WebPアニメーションサービスのリソースを解放しました。",
                    LogHelper.Categories.Performance
                );
            }
            catch (Exception ex)
            {
                LogHelper.LogWithTimestamp(
                    $"リソース解放中に予期しないエラーが発生: {ex.Message}",
                    LogHelper.Categories.Error
                );
                // 重大なエラーなので再スローする
                throw;
            }
        }

        // Finalizer as a safeguard (optional but good practice with unmanaged resources)
        ~WebpAnimationService()
        {
            DisposeInternal();
        }

        public struct WebPFrameInfo
        {
            public TimeSpan Duration { get; set; }
            public bool IsBlendWithPrevious { get; set; }  // blend_method: true=前フレームに重ねる
            public bool KeepAsBackground { get; set; }     // dispose_method: true=そのまま残す
        }

        public List<WebPFrameInfo> GetFrameInfos()
        {
            if (_demuxer == IntPtr.Zero)
            {
                throw new InvalidOperationException("Service not initialized. Call InitializeAsync first.");
            }

            var frameInfos = new List<WebPFrameInfo>();
            frameInfos.Capacity = _frameCount;

            for (int i = 1; i <= _frameCount; ++i)
            {
                var iter = new LibWebP.WebPIterator();
                var iteratorAcquired = false;

                try
                {
                    if (LibWebP.WebPDemuxGetFrame(_demuxer, i, out iter) == 0)
                    {
                        throw new InvalidOperationException($"フレーム {i} の取得に失敗しました。");
                    }
                    iteratorAcquired = true;

                    frameInfos.Add(new WebPFrameInfo
                    {
                        Duration = TimeSpan.FromMilliseconds(iter.duration),
                        IsBlendWithPrevious = iter.blend_method == 1,
                        KeepAsBackground = iter.dispose_method == 0
                    });
                }
                finally
                {
                    if (iteratorAcquired)
                    {
                        LibWebP.WebPDemuxReleaseIterator(ref iter);
                    }
                }
            }

            return frameInfos;
        }

        // GetFrameDelaysの互換性維持用実装
        public List<TimeSpan> GetFrameDelays() => GetFrameInfos().Select(info => info.Duration).ToList();

        public class WebPDecodedFrame
        {
            public BitmapSource Bitmap { get; set; }
            public int BlendMethod { get; set; }    // 0:上書き、1:重ねる
            public LibWebP.WebPMuxAnimDispose DisposeMethod { get; set; }  // 0:残す、1:透明化
            public int OffsetX { get; set; }        // フレームのX座標オフセット
            public int OffsetY { get; set; }        // フレームのY座標オフセット
            public int Width { get; set; }          // フレームの幅
            public int Height { get; set; }         // フレームの高さ
        }

        // Decodes a specific frame using Demuxer
        public async Task<WebPDecodedFrame> DecodeFrameAsync(int index)
        {
            if (_demuxer == IntPtr.Zero)
            {
                throw new InvalidOperationException("サービスが初期化されていません。InitializeAsyncを先に呼び出してください。");
            }
            if (index < 0 || index >= _frameCount)
            {
                throw new ArgumentOutOfRangeException(nameof(index), $"フレームインデックスが無効です: {index}");
            }

            return await Task.Run(() =>
            {
                var stopwatch = Stopwatch.StartNew();
                LibWebP.WebPIterator iter = new();
                bool iteratorAcquired = false;
                IntPtr buffer = IntPtr.Zero;
                WebPDecodedFrame frame = null;

                try
                {
                    // フレームの取得（1-basedインデックス）
                    if (LibWebP.WebPDemuxGetFrame(_demuxer, index + 1, out iter) == 0)
                    {
                        throw new InvalidOperationException($"フレーム {index + 1} の取得に失敗しました。");
                    }
                    iteratorAcquired = true;

                    // フレームフラグメントの検証
                    if (iter.fragment.bytes == IntPtr.Zero || iter.fragment.size.ToUInt32() == 0)
                    {
                        throw new InvalidOperationException($"フレーム {index + 1} のデータが無効です。");
                    }

                    // BGRAバッファの準備（フレームサイズ分のみ確保）
                    int stride = iter.width * 4;
                    int bufferSize = iter.height * stride;
                    buffer = Marshal.AllocHGlobal(bufferSize);

                    // フレームのデコード
                    IntPtr decodedPtr = LibWebP.WebPDecodeBGRAInto(
                        iter.fragment.bytes,
                        iter.fragment.size,
                        buffer,
                        bufferSize,
                        stride
                    );

                    if (decodedPtr == IntPtr.Zero)
                    {
                        throw new InvalidOperationException($"フレーム {index + 1} のデコードに失敗しました。");
                    }

                    // BitmapSourceの生成
                    var bitmap = BitmapSource.Create(
                        iter.width,
                        iter.height,
                        96,
                        96,
                        PixelFormats.Bgra32,
                        null,
                        buffer,
                        bufferSize,
                        stride
                    );

                    // パフォーマンス向上のためFreeze
                    bitmap.Freeze();

                    frame = new WebPDecodedFrame
                    {
                        Bitmap = bitmap,
                        BlendMethod = iter.blend_method,
                        DisposeMethod = iter.dispose_method, // Cast to int
                        OffsetX = iter.x_offset,
                        OffsetY = iter.y_offset,
                        Width = iter.width,
                        Height = iter.height
                    };

                    stopwatch.Stop();
                    LogHelper.LogWithTimestamp(
                        $"フレーム {index} のデコード完了: {stopwatch.ElapsedMilliseconds}ms " +
                        $"(blend:{iter.blend_method}, dispose:{iter.dispose_method}, " +
                        $"offset:({iter.x_offset},{iter.y_offset}), size:{iter.width}x{iter.height})",
                        LogHelper.Categories.Performance
                    );

                    return frame;
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        $"フレーム {index + 1} の処理中にエラーが発生しました: {ex.Message}",
                        ex
                    );
                }
                finally
                {
                    if (iteratorAcquired)
                    {
                        LibWebP.WebPDemuxReleaseIterator(ref iter);
                    }
                    if (buffer != IntPtr.Zero)
                    {
                        Marshal.FreeHGlobal(buffer);
                    }
                }
            });
        }


        public Task ResetDecoderAsync()
        {
            // Demuxer API doesn't require explicit reset for random access via GetFrame.
            LogHelper.LogWithTimestamp("ResetDecoderAsync called (no operation needed for Demuxer seek)", LogHelper.Categories.Performance);
            return Task.CompletedTask;
        }

        // ResetDecoderAsync is obsolete
    }
}
