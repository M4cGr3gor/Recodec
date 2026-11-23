using Recodec.Core.Media;

namespace Recodec.Core.Conversion;

public enum ContainerKind { Mp4, Mkv, Mov, WebM, Avi }

public enum VideoMode { Copy, Encode }

/// <summary>Xvid = MPEG-4 Part 2 (compatible con DivX).</summary>
public enum VideoCodec { Hevc, H264, Av1, Vp9, Xvid }

/// <summary>Gpu = tarjeta NVIDIA (NVENC; se llama así por compatibilidad con la configuración guardada), Qsv = Intel Quick Sync, Amf = AMD.</summary>
public enum EncoderBackend { Gpu, Cpu, Qsv, Amf }

public enum DeinterlaceMode { Auto, On, Off }

/// <summary>Reducción de ruido o grano (solo al recodificar).</summary>
public enum DenoiseMode { Off, Light, Medium, Strong, Max }

public enum AudioCodecChoice { Copy, AacStereo, Aac51, Ac3_51, Eac3_51, OpusStereo, Mp3Stereo, Flac }

public enum VolumeMode { Off, Soft, Night }

public enum BurnSourceKind { None, Internal, External }

/// <summary>Todo lo necesario para convertir un archivo. Es una copia independiente: la cola guarda estas copias.</summary>
public sealed class ConversionJob
{
    public required MediaInfo Source { get; init; }
    public required string OutputPath { get; set; }
    public ContainerKind Container { get; set; } = ContainerKind.Mp4;
    public VideoSettings Video { get; set; } = new();
    public List<AudioTrackJob> AudioTracks { get; set; } = new();
    public SubtitleSettings Subtitles { get; set; } = new();
    public VolumeSettings Volume { get; set; } = new();
    public bool KeepChapters { get; set; } = true;
    public bool FastStart { get; set; } = true;

    /// <summary>Solo para la prueba de 30 s.</summary>
    public TimeSpan? TrimStart { get; set; }
    public TimeSpan? TrimDuration { get; set; }

    public TimeSpan EffectiveDuration =>
        TrimDuration ?? (TrimStart.HasValue ? Source.Duration - TrimStart.Value : Source.Duration);

    public ConversionJob Clone() => new()
    {
        Source = Source,
        OutputPath = OutputPath,
        Container = Container,
        Video = Video with { },
        AudioTracks = AudioTracks.Select(a => a with { }).ToList(),
        Subtitles = Subtitles with { Keep = Subtitles.Keep.ToList() },
        Volume = Volume with { },
        KeepChapters = KeepChapters,
        FastStart = FastStart,
        TrimStart = TrimStart,
        TrimDuration = TrimDuration,
    };
}

public sealed record VideoSettings
{
    public VideoMode Mode { get; set; } = VideoMode.Copy;
    public VideoCodec Codec { get; set; } = VideoCodec.Hevc;
    public EncoderBackend Backend { get; set; } = EncoderBackend.Gpu;
    /// <summary>CRF (CPU) o CQ (GPU). Menor = más calidad.</summary>
    public int Quality { get; set; } = 22;
    /// <summary>0 = más rápido ... 4 = más lento y mejor compresión.</summary>
    public int Speed { get; set; } = 2;
    /// <summary>0 = igual que el original.</summary>
    public int MaxHeight { get; set; }
    public bool AutoCrop { get; set; } = true;
    public CropRect? Crop { get; set; }
    public DeinterlaceMode Deinterlace { get; set; } = DeinterlaceMode.Auto;
    public DenoiseMode Denoise { get; set; } = DenoiseMode.Off;
}

public sealed record CropRect(int Width, int Height, int X, int Y)
{
    public override string ToString() => $"{Width}×{Height} (desde {X},{Y})";
}

public sealed record AudioTrackJob
{
    /// <summary>Índice absoluto de la pista en el archivo de origen (si no es externa).</summary>
    public int StreamIndex { get; set; } = -1;
    /// <summary>Archivo de audio externo (.ac3, .m4a, .mka...).</summary>
    public string? ExternalPath { get; set; }
    public int ExternalStreamIndex { get; set; }
    public int DelayMs { get; set; }
    public int SourceChannels { get; set; } = 2;
    public string? SourceChannelLayout { get; set; }
    public string SourceCodec { get; set; } = "";
    public string Language { get; set; } = "und";
    public string? Title { get; set; }
    public AudioCodecChoice Codec { get; set; } = AudioCodecChoice.AacStereo;
    public int BitrateKbps { get; set; } = 192;
    public VolumeMode Volume { get; set; } = VolumeMode.Off;

    public bool IsExternal => ExternalPath is not null;
}

public sealed record SubtitleSettings
{
    /// <summary>Subtítulos que se incluyen como pista (se pueden activar o no en la tele).</summary>
    public List<int> Keep { get; set; } = new();
    public BurnSourceKind BurnKind { get; set; } = BurnSourceKind.None;
    public int BurnStreamIndex { get; set; } = -1;
    public string? BurnExternalPath { get; set; }

    public bool Burns => BurnKind != BurnSourceKind.None;
}

public sealed record VolumeSettings
{
    /// <summary>0..10: cuánto se realzan los diálogos (canal central) al mezclar a estéreo.</summary>
    public int DialogBoost { get; set; } = 7;
    /// <summary>0..10: cuánto se reduce la diferencia entre partes bajas y altas.</summary>
    public int Compression { get; set; } = 6;
    /// <summary>Sonoridad final en LUFS (−16 TV/streaming, −23 estándar europeo de emisión).</summary>
    public double TargetLufs { get; set; } = -16;
}
