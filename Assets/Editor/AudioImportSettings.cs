using UnityEditor;
using UnityEngine;

namespace Ilves.EditorTools
{
    /// <summary>
    /// 音乐导入规格（AssetPostprocessor 强制，不依赖手改 .meta）。
    ///
    /// 为什么源文件用 FLAC：Unity 6 能直接导入 FLAC（实测 AudioClip 时长正确），
    /// 这样不用在外面装 ffmpeg 转码，素材目录里放什么工程就吃什么；
    /// 构建时 Unity 会按下面这些设置把它重新编码成 Vorbis 塞进包里，
    /// 所以 29MB 的 FLAC 母版不会原样进包。
    ///
    /// 关键取舍：
    /// - Streaming 而不是 DecompressOnLoad：两三分钟的音乐解压进内存要几十 MB，流式解码只缓冲一小段；
    /// - Vorbis q0.7：音乐够用，比 q1.0 省三成体积且听不出差别；
    /// - 采样率 Preserve：不重采样，避免 44.1k 转 48k 带来的那种发闷。
    /// </summary>
    public class AudioImportSettings : AssetPostprocessor
    {
        public const string MusicRoot = "Assets/Resources/music/";

        /// <summary>需要按音乐规矩导入的资源（ContentCheck 会逐个核对）。</summary>
        public static readonly string[] MusicPaths =
        {
            MusicRoot + "title.flac",
            MusicRoot + "story-1.flac",
            MusicRoot + "story-2.flac",
        };

        void OnPreprocessAudio()
        {
            string p = assetPath.Replace('\\', '/');
            if (!p.StartsWith(MusicRoot)) return;

            var ai = (AudioImporter)assetImporter;
            ai.forceToMono = false;
            ai.ambisonic = false;

            var s = ai.defaultSampleSettings;
            s.loadType = AudioClipLoadType.Streaming;
            s.compressionFormat = AudioCompressionFormat.Vorbis;
            s.quality = 0.7f;
            s.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            ai.defaultSampleSettings = s;
        }
    }
}
