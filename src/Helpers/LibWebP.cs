using System;
using System.Runtime.InteropServices;

namespace Illustra.Helpers
{
    public static class LibWebP // static クラスに変更
    {
        // DLL名を定数として定義（拡張子は省略可能）
        private const string LIBWEBP_DLL = "libwebp";
        private const string LIBWEBP_DEMUX_DLL = "libwebpdemux";

        // --- Decoder API ---

        // Decoder configuration structure (optional, if needed for advanced features)
        // [StructLayout(LayoutKind.Sequential)]
        // public struct WebPDecoderConfig { ... }

        // Main decoding functions
        [DllImport(LIBWEBP_DLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "WebPGetFeaturesInternal")]
        public static extern VP8StatusCode WebPGetFeaturesInternal(IntPtr data, UIntPtr data_size, ref WebPBitstreamFeatures features, int WEBP_DECODER_ABI_VERSION); // Change to public

        public static VP8StatusCode WebPGetFeatures(byte[] data, ref WebPBitstreamFeatures features)
        {
            var handle = GCHandle.Alloc(data, GCHandleType.Pinned);
            try
            {
                IntPtr dataPtr = handle.AddrOfPinnedObject();
                // WEBP_DECODER_ABI_VERSION は通常 0x0209 (libwebp v1.1.0 の場合など)
                // 使用する libwebp.dll のバージョンに合わせて確認・調整が必要
                return WebPGetFeaturesInternal(dataPtr, (UIntPtr)data.Length, ref features, 0x0209);
            }
            finally
            {
                handle.Free();
            }
        }

        // --- Incremental Decoder API ---

        [DllImport(LIBWEBP_DLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "WebPINewDecoder")]
        public static extern IntPtr WebPINewDecoder(ref WebPDecoderOptions options); // Use options if needed

        // Simplified version without options for now
        [DllImport(LIBWEBP_DLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "WebPINewRGB")]
        public static extern IntPtr WebPINewRGB(WEBP_CSP_MODE mode, IntPtr output_buffer, int output_buffer_size, int output_stride);

        [DllImport(LIBWEBP_DLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "WebPIAppend")]
        public static extern VP8StatusCode WebPIAppend(IntPtr idec, IntPtr data, UIntPtr data_size);

        [DllImport(LIBWEBP_DLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "WebPIUpdate")]
        public static extern VP8StatusCode WebPIUpdate(IntPtr idec, IntPtr data, UIntPtr data_size);

        [DllImport(LIBWEBP_DLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "WebPIDecGetRGB")]
        public static extern IntPtr WebPIDecGetRGB(IntPtr idec, out int last_y, out int width, out int height, out int stride);

        [DllImport(LIBWEBP_DLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "WebPIDelete")]
        public static extern void WebPIDelete(IntPtr idec);


        // --- Structures and Enums ---

        [StructLayout(LayoutKind.Sequential)]
        public struct WebPBitstreamFeatures
        {
            public int width;          // Width in pixels.
            public int height;         // Height in pixels.
            public int has_alpha;      // True if the bitstream contains an alpha channel.
            public int has_animation;  // True if the bitstream is an animation.
            public int format;         // 0 = undefined, 1 = lossy, 2 = lossless
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 5)]
            public uint[] pad; // Padding for later use.
        }

        // Decoder options structure (if needed)
        [StructLayout(LayoutKind.Sequential)]
        public struct WebPDecoderOptions
        {
            public int bypass_filtering;
            public int no_fancy_upsampling;
            public int use_cropping;
            public int crop_left, crop_top, crop_width, crop_height;
            public int use_scaling;
            public int scaled_width, scaled_height;
            public int use_threads;
            public int dithering_strength;
            public int flip;
            public int alpha_dithering_strength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 5)]
            public uint[] pad;
        }

        public enum VP8StatusCode
        {
            VP8_STATUS_OK = 0,
            VP8_STATUS_OUT_OF_MEMORY = 1,
            VP8_STATUS_INVALID_PARAM = 2,
            VP8_STATUS_BITSTREAM_ERROR = 3,
            VP8_STATUS_UNSUPPORTED_FEATURE = 4,
            VP8_STATUS_SUSPENDED = 5,
            VP8_STATUS_USER_ABORT = 6,
            VP8_STATUS_NOT_ENOUGH_DATA = 7
        }

        // Color space modes (for WebPINewRGB)
        public enum WEBP_CSP_MODE
        {
            MODE_RGB = 0, MODE_RGBA = 1,
            MODE_BGR = 2, MODE_BGRA = 3,
            MODE_ARGB = 4, MODE_RGBA_4444 = 5,
            MODE_RGB_565 = 6,
            // RGB-premultiplied variants
            MODE_rgbA = 7, MODE_bgrA = 8,
            MODE_Argb = 9, MODE_rgbA_4444 = 10,
            // YUV modes must come after RGB ones.
            MODE_YUV = 11, MODE_YUVA = 12, // yuv 4:2:0
            MODE_LAST = 13
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct WebPData
        {
            public IntPtr bytes;
            public UIntPtr size;
        }

        // --- Basic Decoder API (for decoding frame fragments) ---

        [DllImport(LIBWEBP_DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern int WebPGetInfo(IntPtr data, UIntPtr data_size, out int width, out int height);

        // Decodes BGRA image data into a pre-allocated buffer
        [DllImport(LIBWEBP_DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr WebPDecodeBGRAInto(IntPtr data, UIntPtr data_size, IntPtr output_buffer, int output_buffer_size, int output_stride);

        // Consider adding other WebPDecodeXXXInto functions if needed (e.g., RGBA, BGR)

        // --- Demux API ---

        public const int WEBP_DEMUX_ABI_VERSION = 0x0107; // Check the version you are using

        [StructLayout(LayoutKind.Sequential)]
        public struct WebPIterator
        {
            public int frame_num;
            public int num_frames; // Correct field name based on header
            public int x_offset, y_offset;
            public int width, height;
            public int duration;
            public WebPMuxAnimDispose dispose_method; // Changed from WEBP_CSP_MODE
            public int complete; // Correct field name based on header
            public WebPData fragment;
            public int has_alpha;
            public int blend_method; // Correct field name based on header
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
            private uint[] pad; // Private to match header
        }

        // Opaque pointer for WebPDemuxer
        // public struct WebPDemuxer { } // Not needed, use IntPtr

        [Flags]
        public enum WebPFeatureFlags : uint
        {
            FRAGMENT_FLAG = 0x00000001, // Not present in demux.h, likely internal or deprecated
            ALPHA_FLAG    = 0x00000010,
            ANIMATION_FLAG = 0x00000002,
            ICCP_FLAG      = 0x00000004,
            EXIF_FLAG      = 0x00000008,
            XMP_FLAG       = 0x00000020,
            ALL_VALID_FLAGS = 0x0000003E // Sum of defined flags above
        }

        // Corresponds to WebPDemuxState
        public enum WebPDemuxState
        {
            WEBP_DEMUX_PARSE_ERROR = -1,
            WEBP_DEMUX_PARSING_HEADER = 0,
            WEBP_DEMUX_PARSED_HEADER = 1,
            WEBP_DEMUX_DONE = 2
        }

        // Dispose method (animation only)
        public enum WebPMuxAnimDispose
        {
            WEBP_MUX_DISPOSE_NONE = 0,       // Do not dispose.
            WEBP_MUX_DISPOSE_BACKGROUND = 1  // Dispose to background color.
        }


        // Internal function, use the wrapper WebPDemux()
        [DllImport(LIBWEBP_DEMUX_DLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "WebPDemuxInternal")]
        private static extern IntPtr WebPDemuxInternal(ref WebPData data, int allow_partial, ref WebPDemuxState state, int version);

        // Wrapper for WebPDemuxInternal
        public static IntPtr WebPDemux(ref WebPData data)
        {
            WebPDemuxState state = default;
            // Use the correct ABI version constant
            return WebPDemuxInternal(ref data, 0, ref state, WEBP_DEMUX_ABI_VERSION);
        }

        [DllImport(LIBWEBP_DEMUX_DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern void WebPDemuxDelete(IntPtr dmux);

        [DllImport(LIBWEBP_DEMUX_DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern uint WebPDemuxGetI(IntPtr dmux, WebPFormatFeature feature);

        // Enum for WebPDemuxGetI features
        public enum WebPFormatFeature
        {
            WEBP_FF_FORMAT_FLAGS = 0,      // bit-wise combination of WebPFeatureFlags
            WEBP_FF_CANVAS_WIDTH = 1,      // Width of the canvas
            WEBP_FF_CANVAS_HEIGHT = 2,     // Height of the canvas
            WEBP_FF_LOOP_COUNT = 3,        // Number of animation loop
            WEBP_FF_BACKGROUND_COLOR = 4,  // Background color of the canvas
            WEBP_FF_FRAME_COUNT = 5        // Number of frames in the animation
        }


        [DllImport(LIBWEBP_DEMUX_DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern int WebPDemuxGetFrame(IntPtr dmux, int frame_number, out WebPIterator iter); // frame_number is 1-based

        [DllImport(LIBWEBP_DEMUX_DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern void WebPDemuxReleaseIterator(ref WebPIterator iter);

        // Add WebPChunkIterator and related functions if needed for chunk access

        // --- WebPAnimDecoder API ---

        [StructLayout(LayoutKind.Sequential, Pack=4)]
        public struct WebPAnimDecoderOptions
        {
            public WEBP_CSP_MODE color_mode;
            public int use_threads;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 7)]
            public uint[] padding;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct WebPAnimInfo
        {
            public int canvas_width;
            public int canvas_height;
            public int loop_count;
            public int bgcolor;
            public int frame_count;
            public int pad1;
            public int pad2;
            public int pad3;
        }

        [DllImport(LIBWEBP_DEMUX_DLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "WebPAnimDecoderOptionsInitInternal")]
        public static extern int WebPAnimDecoderOptionsInit(ref WebPAnimDecoderOptions options);

        [DllImport(LIBWEBP_DEMUX_DLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "WebPAnimDecoderNewInternal")]
        public static extern IntPtr WebPAnimDecoderNew(ref WebPData webpData, ref WebPAnimDecoderOptions options);

        [DllImport(LIBWEBP_DEMUX_DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern void WebPAnimDecoderDelete(IntPtr dec);

        [DllImport(LIBWEBP_DEMUX_DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern int WebPAnimDecoderGetInfo(IntPtr dec, out WebPAnimInfo info);

        [DllImport(LIBWEBP_DEMUX_DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern int WebPAnimDecoderHasMoreFrames(IntPtr dec);

        [DllImport(LIBWEBP_DEMUX_DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern int WebPAnimDecoderGetNext(IntPtr dec, out IntPtr buf, out int timestamp);

        [DllImport(LIBWEBP_DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern void WebPDataInit(ref WebPData webpData);

        [DllImport(LIBWEBP_DEMUX_DLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "WebPAnimDecoderNewInternal")]
        public static extern IntPtr WebPAnimDecoderNewInternal(ref WebPData webpData, ref WebPAnimDecoderOptions options, int abi_version);

    }
}


