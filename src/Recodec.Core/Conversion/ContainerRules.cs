using Recodec.Core.Media;

namespace Recodec.Core.Conversion;

/// <summary>Qué admite cada contenedor. Evita combinaciones que harían fallar a FFmpeg.</summary>
public static class ContainerRules
{
    public static string Extension(ContainerKind c) => c switch
    {
        ContainerKind.Mp4 => ".mp4",
        ContainerKind.Mkv => ".mkv",
        ContainerKind.Mov => ".mov",
        ContainerKind.WebM => ".webm",
        ContainerKind.Avi => ".avi",
        _ => ".mp4",
    };

    public static string DisplayName(ContainerKind c) => c switch
    {
        ContainerKind.Mp4 => "MP4",
        ContainerKind.Mkv => "MKV",
        ContainerKind.Mov => "MOV",
        ContainerKind.WebM => "WebM",
        ContainerKind.Avi => "AVI",
        _ => c.ToString(),
    };

    public static bool CanHoldVideo(ContainerKind c, string codec) => c switch
    {
        ContainerKind.Mkv => true,
        ContainerKind.Mp4 => codec is "h264" or "hevc" or "av1" or "mpeg4" or "mpeg2video" or "vp9",
        ContainerKind.Mov => codec is "h264" or "hevc" or "mpeg4" or "prores" or "mjpeg",
        ContainerKind.WebM => codec is "vp8" or "vp9" or "av1",
        ContainerKind.Avi => codec is "mpeg4" or "msmpeg4v3" or "msmpeg4v2" or "mjpeg",
        _ => false,
    };

    public static bool CanEncodeVideo(ContainerKind c, VideoCodec codec) => c switch
    {
        ContainerKind.WebM => codec is VideoCodec.Av1 or VideoCodec.Vp9,
        ContainerKind.Mov => codec is VideoCodec.Hevc or VideoCodec.H264 or VideoCodec.Xvid,
        ContainerKind.Avi => codec == VideoCodec.Xvid,
        _ => true,
    };

    /// <summary>Códec de vídeo que se usa si el elegido no cabe en el contenedor.</summary>
    public static VideoCodec FallbackVideoCodec(ContainerKind c) => c switch
    {
        ContainerKind.WebM => VideoCodec.Av1,
        ContainerKind.Avi => VideoCodec.Xvid,
        _ => VideoCodec.Hevc,
    };

    /// <summary>Solo HEVC, H.264 y AV1 tienen codificador en alguna tarjeta gráfica.</summary>
    public static bool HasGpuEncoder(VideoCodec codec) => codec is VideoCodec.Hevc or VideoCodec.H264 or VideoCodec.Av1;

    /// <summary>
    /// Códecs que Recodec sabe codificar con cada gráfica. En Intel y AMD solo HEVC y H.264: su AV1 usa otra escala
    /// de calidad que no se ha podido comprobar.
    /// </summary>
    public static IReadOnlyList<VideoCodec> HardwareCodecs(EncoderBackend b) => b switch
    {
        EncoderBackend.Gpu => new[] { VideoCodec.Hevc, VideoCodec.H264, VideoCodec.Av1 },
        EncoderBackend.Qsv or EncoderBackend.Amf => new[] { VideoCodec.Hevc, VideoCodec.H264 },
        _ => Array.Empty<VideoCodec>(),
    };

    /// <summary>Códecs que solo admiten 8 bits y SDR (un vídeo HDR se convierte a SDR).</summary>
    public static bool IsSdrOnly(VideoCodec codec) => codec is VideoCodec.H264 or VideoCodec.Xvid;

    public static bool CanHoldAudio(ContainerKind c, string codec) => c switch
    {
        ContainerKind.Mkv => true,
        ContainerKind.Mp4 => codec is "aac" or "ac3" or "eac3" or "mp3" or "opus" or "flac" or "alac",
        ContainerKind.Mov => codec is "aac" or "ac3" or "eac3" or "mp3" or "alac" || codec.StartsWith("pcm_"),
        ContainerKind.WebM => codec is "opus" or "vorbis",
        ContainerKind.Avi => codec is "mp3" or "mp2" or "ac3" || codec.StartsWith("pcm_"),
        _ => false,
    };

    /// <summary>Formato de audio que se usa si el elegido no cabe en el contenedor.</summary>
    public static AudioCodecChoice FallbackAudioCodec(ContainerKind c) => c switch
    {
        ContainerKind.WebM => AudioCodecChoice.OpusStereo,
        ContainerKind.Avi => AudioCodecChoice.Mp3Stereo,
        _ => AudioCodecChoice.AacStereo,
    };

    public static IReadOnlyList<AudioCodecChoice> AllowedAudioCodecs(ContainerKind c) => c switch
    {
        ContainerKind.WebM => new[] { AudioCodecChoice.Copy, AudioCodecChoice.OpusStereo },
        ContainerKind.Avi => new[] { AudioCodecChoice.Copy, AudioCodecChoice.Mp3Stereo, AudioCodecChoice.Ac3_51 },
        ContainerKind.Mov => new[] { AudioCodecChoice.Copy, AudioCodecChoice.AacStereo, AudioCodecChoice.Aac51, AudioCodecChoice.Ac3_51, AudioCodecChoice.Eac3_51, AudioCodecChoice.Mp3Stereo },
        _ => new[] { AudioCodecChoice.Copy, AudioCodecChoice.AacStereo, AudioCodecChoice.Aac51, AudioCodecChoice.Ac3_51, AudioCodecChoice.Eac3_51, AudioCodecChoice.OpusStereo, AudioCodecChoice.Mp3Stereo, AudioCodecChoice.Flac },
    };

    /// <summary>Códec con el que se guardará un subtítulo de texto en este contenedor (null = no cabe).</summary>
    public static string? SoftSubtitleCodec(ContainerKind c, StreamInfo s)
    {
        if (c == ContainerKind.Avi) return null; // AVI no admite subtítulos como pista
        if (s.IsBitmapSubtitle) return c == ContainerKind.Mkv ? "copy" : null;
        return c switch
        {
            ContainerKind.Mkv => s.Codec == "mov_text" ? "srt" : "copy",
            ContainerKind.Mp4 or ContainerKind.Mov => "mov_text",
            ContainerKind.WebM => "webvtt",
            _ => null,
        };
    }

    public static string AudioCodecName(AudioCodecChoice a) => a switch
    {
        AudioCodecChoice.Copy => "Copiar sin cambios",
        AudioCodecChoice.AacStereo => "AAC estéreo",
        AudioCodecChoice.Aac51 => "AAC 5.1",
        AudioCodecChoice.Ac3_51 => "AC3 5.1 (Dolby Digital)",
        AudioCodecChoice.Eac3_51 => "E-AC3 5.1 (Dolby Digital+)",
        AudioCodecChoice.OpusStereo => "Opus estéreo",
        AudioCodecChoice.Mp3Stereo => "MP3 estéreo",
        AudioCodecChoice.Flac => "FLAC (sin pérdida)",
        _ => a.ToString(),
    };

    public static int DefaultBitrate(AudioCodecChoice a) => a switch
    {
        AudioCodecChoice.AacStereo => 192,
        AudioCodecChoice.Aac51 => 384,
        AudioCodecChoice.Ac3_51 => 448,
        AudioCodecChoice.Eac3_51 => 640,
        AudioCodecChoice.OpusStereo => 160,
        AudioCodecChoice.Mp3Stereo => 192,
        _ => 0, // FLAC no usa tasa de bits: es sin pérdida
    };

    public static int OutputChannels(AudioCodecChoice a, int sourceChannels) => a switch
    {
        AudioCodecChoice.AacStereo or AudioCodecChoice.OpusStereo or AudioCodecChoice.Mp3Stereo => 2,
        AudioCodecChoice.Aac51 or AudioCodecChoice.Ac3_51 or AudioCodecChoice.Eac3_51 => Math.Min(Math.Max(sourceChannels, 1), 6),
        AudioCodecChoice.Flac => Math.Clamp(sourceChannels, 1, 8),
        _ => sourceChannels,
    };

    public static bool UsesBitrate(AudioCodecChoice a) => a is not (AudioCodecChoice.Copy or AudioCodecChoice.Flac);
}
