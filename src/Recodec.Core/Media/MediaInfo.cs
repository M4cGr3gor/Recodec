namespace Recodec.Core.Media;

public enum StreamKind { Video, Audio, Subtitle, Attachment, Data, Unknown }

public enum HdrKind { None, Hdr10, Hlg, DolbyVision }

/// <summary>Información de un archivo multimedia obtenida con ffprobe.</summary>
public sealed class MediaInfo
{
    public required string Path { get; init; }
    public string FormatName { get; init; } = "";
    public string FormatLongName { get; init; } = "";
    public long SizeBytes { get; init; }
    public TimeSpan Duration { get; init; }
    public long BitRate { get; init; }
    public string? Title { get; init; }
    public int ChapterCount { get; init; }
    public List<StreamInfo> Streams { get; init; } = new();

    /// <summary>Metadatos HDR10 estáticos (solo si el vídeo es HDR10).</summary>
    public HdrMetadata? HdrMetadata { get; set; }

    public StreamInfo? Video => Streams.FirstOrDefault(s => s.Kind == StreamKind.Video && !s.AttachedPic);
    public IEnumerable<StreamInfo> AudioStreams => Streams.Where(s => s.Kind == StreamKind.Audio);
    public IEnumerable<StreamInfo> SubtitleStreams => Streams.Where(s => s.Kind == StreamKind.Subtitle);

    public string FileName => System.IO.Path.GetFileName(Path);

    /// <summary>Posición (0..n-1) de un subtítulo entre los subtítulos del archivo, que es lo que usa el filtro "subtitles".</summary>
    public int SubtitleRelativeIndex(int absoluteIndex) =>
        SubtitleStreams.Select((s, i) => (s, i)).First(x => x.s.Index == absoluteIndex).i;

    public int AudioRelativeIndex(int absoluteIndex) =>
        AudioStreams.Select((s, i) => (s, i)).First(x => x.s.Index == absoluteIndex).i;
}

public sealed class StreamInfo
{
    public int Index { get; init; }
    public StreamKind Kind { get; init; }
    public string Codec { get; init; } = "";
    public string CodecLongName { get; init; } = "";
    public string? Profile { get; init; }
    public string? Language { get; init; }
    public string? Title { get; init; }
    public bool IsDefault { get; init; }
    /// <summary>Marcado como forzado en el archivo (o su título lo dice).</summary>
    public bool ForcedFlag { get; init; }
    /// <summary>Número de líneas (subtítulos): del propio archivo o contadas por <see cref="SubtitleAnalysis"/>.</summary>
    public int? EventCount { get; set; }
    /// <summary>Tiene tan pocas líneas que son subtítulos forzados aunque no estén marcados.</summary>
    public bool LooksForced { get; set; }
    /// <summary>Solo para las partes en otro idioma, carteles…</summary>
    public bool IsForced => ForcedFlag || LooksForced;
    public bool AttachedPic { get; init; }
    public long BitRate { get; init; }

    // Vídeo
    public int Width { get; init; }
    public int Height { get; init; }
    public string? PixelFormat { get; init; }
    public string? ColorTransfer { get; init; }
    public string? ColorPrimaries { get; init; }
    public string? ColorSpace { get; init; }
    public double FrameRate { get; init; }
    public string? FieldOrder { get; init; }
    public int? DolbyVisionProfile { get; init; }
    /// <summary>Giro guardado en los metadatos (vídeos de móvil): 0, 90, 180 o 270 grados.</summary>
    public int Rotation { get; init; }

    /// <summary>Tamaño tal como se ve (FFmpeg gira la imagen al decodificar, así que filtros y recortes trabajan con este).</summary>
    public bool IsRotated90 => Rotation is 90 or 270;
    public int DisplayWidth => IsRotated90 ? Height : Width;
    public int DisplayHeight => IsRotated90 ? Width : Height;

    // Audio
    public int Channels { get; init; }
    public string? ChannelLayout { get; init; }
    public int SampleRate { get; init; }

    public int BitDepth
    {
        get
        {
            var p = PixelFormat ?? "";
            if (p.Contains("12")) return 12;
            if (p.Contains("10")) return 10;
            return 8;
        }
    }

    public HdrKind Hdr
    {
        get
        {
            if (DolbyVisionProfile.HasValue) return HdrKind.DolbyVision;
            return ColorTransfer switch
            {
                "smpte2084" => HdrKind.Hdr10,
                "arib-std-b67" => HdrKind.Hlg,
                _ => HdrKind.None,
            };
        }
    }

    public bool IsInterlaced => FieldOrder is "tt" or "bb" or "tb" or "bt";

    /// <summary>Subtítulos de imagen (Blu-ray, DVD, DVB). No se pueden convertir a texto.</summary>
    public bool IsBitmapSubtitle => Codec is "hdmv_pgs_subtitle" or "dvd_subtitle" or "dvb_subtitle" or "xsub";

    public string LanguageName => Languages.DisplayName(Language);

    public string ChannelDescription => Channels switch
    {
        1 => "mono",
        2 => "estéreo",
        6 => "5.1",
        7 => "6.1",
        8 => "7.1",
        0 => "",
        _ => $"{Channels} canales",
    };

    public string CodecDisplay => CodecNames.Display(Codec, Profile);

    /// <summary>Descripción corta para listas: "Español · E-AC3 5.1 · Castellano 5.1".</summary>
    public string Describe()
    {
        var parts = new List<string> { LanguageName, CodecDisplay };
        if (Kind == StreamKind.Audio && Channels > 0) parts[1] += " " + ChannelDescription;
        if (!string.IsNullOrWhiteSpace(Title) && !string.Equals(Title, LanguageName, StringComparison.OrdinalIgnoreCase)) parts.Add(Title!);
        if (IsForced) parts.Add(ForcedFlag ? "forzados" : "forzados (por el número de líneas)");
        if (Kind == StreamKind.Subtitle && EventCount is { } n) parts.Add($"{n:N0} líneas");
        return string.Join(" · ", parts);
    }
}

/// <summary>Metadatos HDR10 estáticos, en el formato que espera x265.</summary>
public sealed class HdrMetadata
{
    /// <summary>Ej.: G(13250,34500)B(7500,3000)R(34000,16000)WP(15635,16450)L(10000000,1)</summary>
    public string? MasterDisplay { get; init; }
    /// <summary>Ej.: 1000,400</summary>
    public string? MaxCll { get; init; }
}

public static class CodecNames
{
    public static string Display(string codec, string? profile = null) => codec switch
    {
        "hevc" => "HEVC",
        "h264" => "H.264",
        "av1" => "AV1",
        "vp9" => "VP9",
        "vp8" => "VP8",
        "mpeg4" => "MPEG-4 (XviD/DivX)",
        "mpeg2video" => "MPEG-2",
        "vc1" => "VC-1",
        "wmv3" => "WMV",
        "aac" => profile is not null && profile.StartsWith("HE") ? "HE-AAC" : "AAC",
        "ac3" => "AC3 (Dolby Digital)",
        "eac3" => "E-AC3 (Dolby Digital+)",
        "dts" => profile is not null && profile.Contains("MA") ? "DTS-HD MA" : "DTS",
        "truehd" => "TrueHD",
        "flac" => "FLAC",
        "mp3" => "MP3",
        "opus" => "Opus",
        "vorbis" => "Vorbis",
        "pcm_s16le" or "pcm_s24le" or "pcm_s32le" => "PCM",
        "subrip" => "SRT",
        "ass" or "ssa" => "ASS",
        "mov_text" => "Texto MP4",
        "webvtt" => "WebVTT",
        "hdmv_pgs_subtitle" => "PGS (imagen)",
        "dvd_subtitle" => "VobSub (imagen)",
        "dvb_subtitle" => "DVB (imagen)",
        _ => codec.ToUpperInvariant(),
    };
}
