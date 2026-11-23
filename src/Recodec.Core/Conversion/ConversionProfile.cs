using System.Text.Json.Serialization;

namespace Recodec.Core.Conversion;

/// <summary>
/// Conjunto de ajustes con nombre ("Película para la tele", "Serie a 720p"…). Los predefinidos vienen con el
/// programa; los del usuario se guardan en la configuración. No incluye nada propio de un archivo concreto
/// (pistas, subtítulos, destino): eso lo sigue eligiendo Recodec según el idioma favorito.
/// </summary>
public sealed record ConversionProfile
{
    public string Name { get; init; } = "";
    [JsonIgnore] public bool IsBuiltIn { get; init; }
    public string? Description { get; init; }

    public ContainerKind Container { get; init; } = ContainerKind.Mp4;
    /// <summary>Copy = copiar el vídeo si se puede (si no, se recodifica con el códec del perfil).</summary>
    public VideoMode VideoMode { get; init; } = VideoMode.Copy;
    public VideoCodec Codec { get; init; } = VideoCodec.Hevc;
    public EncoderBackend Backend { get; init; } = EncoderBackend.Gpu;
    /// <summary>null = la calidad recomendada para el códec.</summary>
    public int? Quality { get; init; }
    public int Speed { get; init; } = 2;
    public int MaxHeight { get; init; }
    public bool AutoCrop { get; init; } = true;
    public DeinterlaceMode Deinterlace { get; init; } = DeinterlaceMode.Auto;
    public DenoiseMode Denoise { get; init; } = DenoiseMode.Off;

    public AudioCodecChoice AudioCodec { get; init; } = AudioCodecChoice.AacStereo;
    /// <summary>0 = la tasa recomendada para el formato.</summary>
    public int AudioBitrate { get; init; }
    public VolumeMode VolumeMode { get; init; } = VolumeMode.Off;
    public int DialogBoost { get; init; } = 7;
    public int Compression { get; init; } = 6;
    public double TargetLufs { get; init; } = -16;

    public bool KeepChapters { get; init; } = true;
    public bool FastStart { get; init; } = true;

    public override string ToString() => Name;

    /// <summary>
    /// Aplica el perfil a un trabajo. Las pistas copiadas sin cambios se respetan. "Gpu" en un perfil significa
    /// "la gráfica que haya": si no hay NVIDIA pero sí Intel o AMD, se usa esa.
    /// </summary>
    public void ApplyTo(ConversionJob job, HardwareEncoders hardware)
    {
        job.Container = Container;
        var v = job.Video;
        v.Mode = VideoMode;
        v.Codec = Codec;
        v.Backend = hardware.Resolve(Codec, Backend);
        v.Quality = Quality ?? JobPlanner.RecommendedQuality(Codec, v.Backend);
        v.Speed = Speed;
        v.MaxHeight = MaxHeight;
        v.AutoCrop = AutoCrop;
        v.Deinterlace = Deinterlace;
        v.Denoise = Denoise;

        foreach (var t in job.AudioTracks.Where(t => t.Codec != AudioCodecChoice.Copy))
        {
            t.Codec = AudioCodec;
            t.BitrateKbps = AudioBitrate > 0 ? AudioBitrate : ContainerRules.DefaultBitrate(AudioCodec);
            t.Volume = AudioCodec == AudioCodecChoice.Copy ? VolumeMode.Off : VolumeMode;
        }
        job.Volume = new VolumeSettings { DialogBoost = DialogBoost, Compression = Compression, TargetLufs = TargetLufs };
        job.KeepChapters = KeepChapters;
        job.FastStart = FastStart;
        if (!string.IsNullOrWhiteSpace(job.OutputPath)) job.OutputPath = Path.ChangeExtension(job.OutputPath, ContainerRules.Extension(Container));
    }

    /// <summary>Crea un perfil con los ajustes actuales de un trabajo.</summary>
    public static ConversionProfile FromJob(string name, ConversionJob job)
    {
        var v = job.Video;
        var converted = job.AudioTracks.FirstOrDefault(t => t.Codec != AudioCodecChoice.Copy);
        return new ConversionProfile
        {
            Name = name.Trim(),
            Container = job.Container,
            VideoMode = v.Mode,
            Codec = v.Codec,
            Backend = v.Backend,
            Quality = v.Quality == JobPlanner.RecommendedQuality(v.Codec, v.Backend) ? null : v.Quality,
            Speed = v.Speed,
            MaxHeight = v.MaxHeight,
            AutoCrop = v.AutoCrop,
            Deinterlace = v.Deinterlace,
            Denoise = v.Denoise,
            AudioCodec = converted?.Codec ?? AudioCodecChoice.AacStereo,
            AudioBitrate = converted is null || converted.BitrateKbps == ContainerRules.DefaultBitrate(converted.Codec) ? 0 : converted.BitrateKbps,
            VolumeMode = converted?.Volume ?? VolumeMode.Off,
            DialogBoost = job.Volume.DialogBoost,
            Compression = job.Volume.Compression,
            TargetLufs = job.Volume.TargetLufs,
            KeepChapters = job.KeepChapters,
            FastStart = job.FastStart,
        };
    }

    /// <summary>Perfiles que vienen con el programa.</summary>
    public static IReadOnlyList<ConversionProfile> BuiltIn { get; } = new[]
    {
        new ConversionProfile
        {
            Name = "Película para la tele", IsBuiltIn = true,
            Description = "MP4 copiando el vídeo si se puede (si no, HEVC 10 bits) y audio AAC estéreo con el modo noche. Lo que mejor funciona con \"Reproducir en\".",
            VolumeMode = VolumeMode.Night,
        },
        new ConversionProfile
        {
            Name = "Serie a 720p (ocupa poco)", IsBuiltIn = true,
            Description = "HEVC a 720p con calidad algo más baja: un capítulo de una hora ocupa unos 300-500 MB. Audio AAC estéreo sin tocar el volumen.",
            VideoMode = VideoMode.Encode, MaxHeight = 720, Quality = 28,
        },
        new ConversionProfile
        {
            Name = "Película antigua o con grano", IsBuiltIn = true,
            Description = "Recodifica a HEVC quitando ruido y grano (nivel medio), con desentrelazado automático. Ocupa bastante menos que el original.",
            VideoMode = VideoMode.Encode, Denoise = DenoiseMode.Medium,
        },
        new ConversionProfile
        {
            Name = "Máxima calidad (archivo grande)", IsBuiltIn = true,
            Description = "HEVC por procesador, lento y con calidad muy alta, y audio AAC 5.1 sin corrección de volumen. Para guardar una copia buena.",
            VideoMode = VideoMode.Encode, Backend = EncoderBackend.Cpu, Quality = 18, Speed = 3,
            AudioCodec = AudioCodecChoice.Aac51, VolumeMode = VolumeMode.Off,
        },
        new ConversionProfile
        {
            Name = "Solo pasar a MP4 (rápido)", IsBuiltIn = true,
            Description = "Copia vídeo y audio sin recodificar (el audio se convierte solo si MP4 no lo admite). Tarda muy poco y no pierde calidad.",
            AudioCodec = AudioCodecChoice.Copy, VolumeMode = VolumeMode.Off,
        },
        new ConversionProfile
        {
            Name = "DivX/XviD para reproductores antiguos", IsBuiltIn = true,
            Description = "AVI con XviD a 720p y audio MP3: lo leen los reproductores de DVD con DivX y las teles antiguas.",
            Container = ContainerKind.Avi, VideoMode = VideoMode.Encode, Codec = VideoCodec.Xvid, Backend = EncoderBackend.Cpu,
            MaxHeight = 720, AudioCodec = AudioCodecChoice.Mp3Stereo,
        },
    };
}
