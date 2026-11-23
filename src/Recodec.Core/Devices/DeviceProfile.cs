using Recodec.Core.Conversion;

namespace Recodec.Core.Devices;

/// <summary>
/// Lo que un reproductor DLNA (una tele) dice que acepta. Se obtiene de su lista "protocolInfo"
/// (acción GetProtocolInfo del servicio ConnectionManager): tipos MIME y perfiles DLNA (DLNA.ORG_PN).
/// </summary>
public sealed record DeviceProfile
{
    public const string GenericId = "generica";

    /// <summary>Identificador estable (UDN del aparato o <see cref="GenericId"/>).</summary>
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string? Manufacturer { get; init; }
    public string? Model { get; init; }
    public string? Address { get; init; }
    public DateTime? ScannedAt { get; init; }

    /// <summary>Tipos MIME anunciados, en minúsculas y sin repetir.</summary>
    public List<string> Mimes { get; init; } = new();
    /// <summary>Perfiles DLNA anunciados (p. ej. AVC_MP4_MP_HD_AAC), en mayúsculas.</summary>
    public List<string> DlnaProfiles { get; init; } = new();

    public bool IsGeneric => Id == GenericId;
    public string DisplayName => IsGeneric ? Name : string.IsNullOrWhiteSpace(Model) || Name.Contains(Model, StringComparison.OrdinalIgnoreCase) ? Name : $"{Name} ({Model})";

    /// <summary>Perfil para una tele cualquiera: lo que aceptan prácticamente todas desde hace años.</summary>
    public static DeviceProfile Generic { get; } = new()
    {
        Id = GenericId,
        Name = "Tele cualquiera (DLNA / \"Reproducir en\" de Windows)",
        Mimes = new() { "video/mp4", "video/hevc", "audio/mp4", "audio/mpeg" },
        DlnaProfiles = new() { "AVC_MP4_HP_HD_AAC", "AVC_MP4_HP_HD_AC3", "MP3" },
    };

    /// <summary>Crea el perfil a partir de la lista "Sink" de GetProtocolInfo (entradas "http-get:*:mime:info" separadas por comas).</summary>
    public static DeviceProfile FromProtocolInfo(string sink, string id, string name, string? manufacturer, string? model, string? address)
    {
        var mimes = new SortedSet<string>(StringComparer.Ordinal);
        var pns = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var raw in sink.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = raw.Split(':', 4);
            if (parts.Length < 3) continue;
            var mime = parts[2].Trim().ToLowerInvariant();
            if (mime.Length > 0 && mime != "*") mimes.Add(mime);
            if (parts.Length == 4)
                foreach (var p in parts[3].Split(';'))
                    if (p.StartsWith("DLNA.ORG_PN=", StringComparison.OrdinalIgnoreCase))
                        pns.Add(p["DLNA.ORG_PN=".Length..].Trim().ToUpperInvariant());
        }
        return new DeviceProfile
        {
            Id = id,
            Name = name,
            Manufacturer = manufacturer,
            Model = model,
            Address = address,
            ScannedAt = DateTime.Now,
            Mimes = mimes.ToList(),
            DlnaProfiles = pns.ToList(),
        };
    }

    // ------------------------------------------------------------------ contenedores

    /// <summary>Nombres MIME con los que una tele puede anunciar cada contenedor.</summary>
    public static IReadOnlyList<string> ContainerMimes(ContainerKind c) => c switch
    {
        ContainerKind.Mp4 => new[] { "video/mp4", "video/x-m4v" },
        ContainerKind.Mkv => new[] { "video/x-matroska", "video/x-mkv", "video/mkv", "video/matroska" },
        ContainerKind.Mov => new[] { "video/quicktime" },
        ContainerKind.WebM => new[] { "video/webm" },
        ContainerKind.Avi => new[] { "video/avi", "video/x-msvideo", "video/x-avi", "video/msvideo" },
        _ => Array.Empty<string>(),
    };

    /// <summary>
    /// Tipo MIME con el que Windows identifica cada extensión al enviarla con "Reproducir en".
    /// Se puede sustituir por el que diga el registro de Windows (lo hace la aplicación al arrancar).
    /// </summary>
    public static Func<ContainerKind, string> WindowsMime { get; set; } = DefaultWindowsMime;

    public static string DefaultWindowsMime(ContainerKind c) => c switch
    {
        ContainerKind.Mp4 => "video/mp4",
        ContainerKind.Mkv => "video/x-matroska",
        ContainerKind.Mov => "video/quicktime",
        ContainerKind.WebM => "video/webm",
        ContainerKind.Avi => "video/avi",
        _ => "application/octet-stream",
    };

    /// <summary>La tele anuncia el contenedor con algún nombre (es decir, sabe reproducirlo).</summary>
    public bool PlaysContainer(ContainerKind c) => ContainerMimes(c).Any(Mimes.Contains);

    /// <summary>La tele anuncia el contenedor con el mismo nombre que usa Windows: "Reproducir en" lo enviará.</summary>
    public bool AcceptsFromWindows(ContainerKind c) => Mimes.Contains(WindowsMime(c));

    // ------------------------------------------------------------------ códecs

    private bool AnyMime(params string[] m) => m.Any(Mimes.Contains);
    private bool AnyPn(Func<string, bool> f) => DlnaProfiles.Any(f);

    /// <summary>¿Anuncia este códec de vídeo? (nombres de FFmpeg: h264, hevc, av1, vp9, mpeg4, mpeg2video).</summary>
    public bool SupportsVideo(string codec) => codec switch
    {
        "h264" => AnyPn(p => p.StartsWith("AVC_")) || AnyMime("video/h264", "video/avc"),
        "hevc" => AnyPn(p => p.StartsWith("HEVC")) || AnyMime("video/hevc", "video/h265", "video/x-hevc"),
        "av1" => AnyPn(p => p.Contains("AV1")) || AnyMime("video/av1", "video/x-av1"),
        "vp9" or "vp8" => AnyMime("video/webm", "video/x-vp9", "video/vp9"),
        "mpeg4" or "msmpeg4v3" => AnyPn(p => p.StartsWith("MPEG4_P2")) || AnyMime("video/x-divx", "video/divx", "video/x-xvid"),
        "mpeg2video" or "mpeg1video" => AnyPn(p => p.StartsWith("MPEG_PS") || p.StartsWith("MPEG_TS") || p.StartsWith("MPEG_ES") || p == "MPEG1") || AnyMime("video/mpeg", "video/mpeg2"),
        "vc1" or "wmv3" => AnyPn(p => p.StartsWith("WMV") || p.StartsWith("VC1")) || AnyMime("video/x-ms-wmv"),
        _ => false,
    };

    /// <summary>¿Anuncia este códec de audio? (nombres de FFmpeg: aac, ac3, eac3, mp3, dts, truehd, flac, opus).</summary>
    public bool SupportsAudio(string codec) => codec switch
    {
        "aac" => AnyPn(p => p.Contains("AAC")) || AnyMime("audio/mp4", "audio/aac", "audio/x-aac", "audio/aacp", "audio/3gpp"),
        "ac3" => AnyPn(p => p.Contains("AC3")) || AnyMime("audio/ac3", "audio/x-ac3", "audio/vnd.dolby.dd-raw"),
        "eac3" => AnyPn(p => p.Contains("EAC3") || p.Contains("DDPLUS")) || AnyMime("audio/eac3", "audio/x-eac3", "audio/vnd.dolby.dd-plus"),
        "mp3" or "mp2" => AnyPn(p => p.StartsWith("MP3") || p.Contains("MPEG1_L3") || p.Contains("MPEG2_L2")) || AnyMime("audio/mpeg", "audio/mp3", "audio/x-mpeg"),
        // Cuidado: "ADTS" (AAC_ADTS, audio/vnd.dlna.adts) es AAC, no DTS.
        "dts" => AnyPn(p => System.Text.RegularExpressions.Regex.IsMatch(p, @"(^|_)DTS")) || Mimes.Any(m => m is "audio/dts" or "audio/x-dts" or "audio/vnd.dts" or "audio/vnd.dts.hd"),
        "truehd" or "mlp" => Mimes.Any(m => m.Contains("truehd") || m.Contains("mlp")),
        "flac" => Mimes.Any(m => m.Contains("flac")),
        "opus" => Mimes.Any(m => m.Contains("opus")),
        "vorbis" => Mimes.Any(m => m.Contains("ogg") || m.Contains("vorbis")),
        _ => codec.StartsWith("pcm_") && AnyMime("audio/l16", "audio/wav", "audio/x-wav", "audio/lpcm"),
    };

    public static readonly string[] KnownVideo = { "h264", "hevc", "av1", "vp9", "mpeg4", "mpeg2video" };
    public static readonly string[] KnownAudio = { "aac", "ac3", "eac3", "mp3", "dts", "truehd", "flac", "opus" };
    public static readonly ContainerKind[] KnownContainers = { ContainerKind.Mp4, ContainerKind.Mkv, ContainerKind.Avi, ContainerKind.Mov, ContainerKind.WebM };
}
