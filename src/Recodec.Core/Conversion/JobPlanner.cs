using Recodec.Core.Devices;
using Recodec.Core.Media;

namespace Recodec.Core.Conversion;

public sealed class PlanReport
{
    public List<string> Errors { get; } = new();
    public List<string> Warnings { get; } = new();
    public List<string> Adjustments { get; } = new();
    public bool CanRun => Errors.Count == 0;
}

/// <summary>
/// Reglas de negocio: crea los valores por defecto para un archivo (selección de pistas según el idioma favorito)
/// y corrige combinaciones imposibles antes de convertir.
/// </summary>
public static class JobPlanner
{
    public sealed record Defaults
    {
        public string PreferredLanguage { get; init; } = "spa";
        public ContainerKind Container { get; init; } = ContainerKind.Mp4;
        public VideoCodec Codec { get; init; } = VideoCodec.Hevc;
        public EncoderBackend Backend { get; init; } = EncoderBackend.Gpu;
        public AudioCodecChoice AudioCodec { get; init; } = AudioCodecChoice.AacStereo;
        public VolumeMode VolumeMode { get; init; } = VolumeMode.Off;
        public VolumeSettings Volume { get; init; } = new();
        public string? OutputFolder { get; init; }
        public int Speed { get; init; } = 2;
        /// <summary>Codificadores de tarjeta gráfica que funcionan en este equipo.</summary>
        public HardwareEncoders Hardware { get; init; } = HardwareEncoders.Nvidia;
        /// <summary>Perfil elegido: se aplica a cada vídeo que se abre.</summary>
        public ConversionProfile? Profile { get; init; }
        /// <summary>Si hay una tele elegida, cada vídeo se prepara para que la acepte.</summary>
        public DeviceProfile? Device { get; init; }
    }

    public static ConversionJob CreateDefault(MediaInfo info, Defaults d)
    {
        var backend = d.Hardware.Resolve(d.Codec, d.Backend);
        var job = new ConversionJob
        {
            Source = info,
            OutputPath = "",
            Container = d.Profile?.Container ?? d.Container,
            Volume = d.Volume with { },
            Video = new VideoSettings { Codec = d.Codec, Backend = backend, Quality = RecommendedQuality(d.Codec, backend), Speed = d.Speed },
        };

        var v = info.Video;
        job.Video.Mode = v is not null && CanCopyVideo(job, v) ? VideoMode.Copy : VideoMode.Encode;

        // Audio: la primera pista en el idioma favorito (preferida la marcada por defecto);
        // si no hay, la pista por defecto o la primera. La original NO se añade salvo que el usuario lo pida.
        var audio = info.AudioStreams.ToList();
        var chosen = audio.Where(a => Languages.Same(a.Language, d.PreferredLanguage)).OrderByDescending(a => a.IsDefault).FirstOrDefault()
                     ?? audio.FirstOrDefault(a => a.IsDefault)
                     ?? audio.FirstOrDefault();
        if (chosen is not null) job.AudioTracks.Add(TrackFromStream(chosen, d.AudioCodec, d.VolumeMode));

        // Subtítulos: se incluyen los del idioma favorito. Si el audio elegido es de otro idioma,
        // se queman los subtítulos completos para poder seguir la película.
        var subs = info.SubtitleStreams.Where(s => Languages.Same(s.Language, d.PreferredLanguage)).ToList();
        foreach (var s in subs)
            if (ContainerRules.SoftSubtitleCodec(job.Container, s) is not null) job.Subtitles.Keep.Add(s.Index);

        if (chosen is not null && !Languages.Same(chosen.Language, d.PreferredLanguage) && subs.Count > 0)
        {
            // Los completos: no forzados y, si hay varios, el que más líneas tiene.
            var full = subs.Where(s => !s.IsForced).OrderByDescending(s => s.EventCount ?? 0).FirstOrDefault() ?? subs[0];
            job.Subtitles.BurnKind = BurnSourceKind.Internal;
            job.Subtitles.BurnStreamIndex = full.Index;
        }

        if (d.Profile is not null)
        {
            d.Profile.ApplyTo(job, d.Hardware);
            // "Copiar" en un perfil significa "copiar si se puede".
            if (job.Video.Mode == VideoMode.Copy && v is not null && !CanCopyVideo(job, v)) job.Video.Mode = VideoMode.Encode;
        }
        job.OutputPath = SuggestOutputPath(info.Path, job.Container, d.OutputFolder);
        if (d.Device is not null) TvCompatibility.Adapt(job, d.Device);
        Normalize(job);
        return job;
    }

    public static AudioTrackJob TrackFromStream(StreamInfo s, AudioCodecChoice codec, VolumeMode volume) => new()
    {
        StreamIndex = s.Index,
        SourceChannels = s.Channels,
        SourceChannelLayout = s.ChannelLayout,
        SourceCodec = s.Codec,
        Language = Languages.Normalize(s.Language),
        Title = null,
        Codec = codec,
        BitrateKbps = ContainerRules.DefaultBitrate(codec),
        Volume = codec == AudioCodecChoice.Copy ? VolumeMode.Off : volume,
    };

    public static int RecommendedQuality(VideoCodec codec, EncoderBackend backend) => (codec, backend) switch
    {
        (VideoCodec.H264, EncoderBackend.Cpu) => 20,
        (VideoCodec.H264, _) => 23,
        (VideoCodec.Hevc, EncoderBackend.Cpu) => 22,
        (VideoCodec.Hevc, _) => 25,
        (VideoCodec.Av1, EncoderBackend.Cpu) => 30,
        (VideoCodec.Av1, _) => 32,
        (VideoCodec.Vp9, _) => 31,
        (VideoCodec.Xvid, _) => 4,
        _ => 22,
    };

    public static string SuggestOutputPath(string input, ContainerKind container, string? folder)
    {
        var dir = string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder) ? Path.GetDirectoryName(input)! : folder!;
        var name = Path.GetFileNameWithoutExtension(input);
        var path = Path.Combine(dir, name + ContainerRules.Extension(container));
        if (string.Equals(Path.GetFullPath(path), Path.GetFullPath(input), StringComparison.OrdinalIgnoreCase))
            path = Path.Combine(dir, name + " (Recodec)" + ContainerRules.Extension(container));
        return path;
    }

    public static bool CanCopyVideo(ConversionJob job, StreamInfo v) => ForcedEncodeReasons(job, v).Count == 0;

    /// <summary>Motivos por los que el vídeo no se puede copiar tal cual.</summary>
    public static List<string> ForcedEncodeReasons(ConversionJob job, StreamInfo? v)
    {
        var r = new List<string>();
        if (v is null) return r;
        if (job.Subtitles.Burns) r.Add("Quemar subtítulos obliga a recodificar el vídeo.");
        if (!ContainerRules.CanHoldVideo(job.Container, v.Codec))
            r.Add($"El contenedor {ContainerRules.DisplayName(job.Container)} no admite vídeo {v.CodecDisplay}.");
        return r;
    }

    /// <summary>Ajusta las opciones para que la combinación sea válida y devuelve qué se ha cambiado y qué conviene saber.</summary>
    public static PlanReport Normalize(ConversionJob job)
    {
        var rep = new PlanReport();
        var info = job.Source;
        var v = info.Video;

        // --- Vídeo ---
        if (v is not null)
        {
            if (job.Video.Mode == VideoMode.Copy && ForcedEncodeReasons(job, v).Count > 0)
            {
                job.Video.Mode = VideoMode.Encode;
                rep.Adjustments.AddRange(ForcedEncodeReasons(job, v));
            }
            if (job.Video.Mode == VideoMode.Encode && !ContainerRules.CanEncodeVideo(job.Container, job.Video.Codec))
            {
                var newCodec = ContainerRules.FallbackVideoCodec(job.Container);
                rep.Adjustments.Add($"{ContainerRules.DisplayName(job.Container)} no admite {CodecLabel(job.Video.Codec)}: se usará {CodecLabel(newCodec)}.");
                job.Video.Codec = newCodec;
                job.Video.Quality = RecommendedQuality(newCodec, job.Video.Backend);
            }
            if (job.Video.Backend != EncoderBackend.Cpu && !ContainerRules.HardwareCodecs(job.Video.Backend).Contains(job.Video.Codec))
            {
                job.Video.Backend = EncoderBackend.Cpu;
                job.Video.Quality = RecommendedQuality(job.Video.Codec, job.Video.Backend);
            }
            var (qMin, qMax) = QualityRange(job.Video.Codec);
            job.Video.Quality = Math.Clamp(job.Video.Quality, qMin, qMax);
            if (job.Video.Mode == VideoMode.Encode && (v.Hdr != HdrKind.None) && ContainerRules.IsSdrOnly(job.Video.Codec))
                rep.Warnings.Add($"{CodecLabel(job.Video.Codec)} no admite HDR: la imagen se convertirá a SDR (tonos ajustados) para que no se vea apagada.");
            if (job.Video.Mode == VideoMode.Encode && job.Video.Codec == VideoCodec.Xvid && OutputSize(job) is var (xw, xh) && Math.Max(xw, xh) > 1920)
                rep.Warnings.Add("Los reproductores DivX/XviD suelen admitir como máximo 1080p: elige 1080p o menos en \"Resolución máxima\".");
            if (v.Hdr == HdrKind.DolbyVision)
            {
                rep.Warnings.Add(v.DolbyVisionProfile == 5
                    ? "Dolby Vision perfil 5 no tiene capa HDR10: los colores pueden verse mal en teles sin Dolby Vision."
                    : "Dolby Vision no se conserva: se mantiene la capa HDR10 compatible.");
            }
        }
        else
        {
            rep.Warnings.Add("El archivo no tiene vídeo.");
        }

        // --- Audio ---
        var allowed = ContainerRules.AllowedAudioCodecs(job.Container);
        foreach (var t in job.AudioTracks)
        {
            if (t.Codec == AudioCodecChoice.Copy && !ContainerRules.CanHoldAudio(job.Container, t.SourceCodec))
            {
                var nc = ContainerRules.FallbackAudioCodec(job.Container);
                rep.Adjustments.Add($"Audio {CodecNames.Display(t.SourceCodec)} no cabe en {ContainerRules.DisplayName(job.Container)}: se convertirá a {ContainerRules.AudioCodecName(nc)}.");
                t.Codec = nc;
                t.BitrateKbps = ContainerRules.DefaultBitrate(nc);
            }
            else if (!allowed.Contains(t.Codec))
            {
                var nc = ContainerRules.FallbackAudioCodec(job.Container);
                rep.Adjustments.Add($"{ContainerRules.AudioCodecName(t.Codec)} no está permitido en {ContainerRules.DisplayName(job.Container)}: se usará {ContainerRules.AudioCodecName(nc)}.");
                t.Codec = nc;
                t.BitrateKbps = ContainerRules.DefaultBitrate(nc);
            }
            if (t.Codec == AudioCodecChoice.Copy && t.Volume != VolumeMode.Off) t.Volume = VolumeMode.Off;
            if (ContainerRules.UsesBitrate(t.Codec) && t.BitrateKbps <= 0) t.BitrateKbps = ContainerRules.DefaultBitrate(t.Codec);
        }
        if (job.AudioTracks.Count == 0) rep.Warnings.Add("No has elegido ninguna pista de audio: el vídeo saldrá sin sonido.");

        // --- Subtítulos ---
        foreach (var idx in job.Subtitles.Keep.ToList())
        {
            var s = info.Streams.FirstOrDefault(x => x.Index == idx);
            if (s is null || ContainerRules.SoftSubtitleCodec(job.Container, s) is null)
            {
                job.Subtitles.Keep.Remove(idx);
                if (s is not null)
                    rep.Adjustments.Add(job.Container == ContainerKind.Avi
                        ? $"AVI no admite subtítulos como pista ({s.Describe()}); puedes quemarlos en la imagen."
                        : $"Los subtítulos {s.Describe()} son imágenes y {ContainerRules.DisplayName(job.Container)} no los admite como pista; puedes quemarlos en la imagen.");
            }
        }
        if (job.Subtitles.BurnKind == BurnSourceKind.Internal && job.Subtitles.Keep.Contains(job.Subtitles.BurnStreamIndex))
            job.Subtitles.Keep.Remove(job.Subtitles.BurnStreamIndex);
        if (job.Subtitles.BurnKind == BurnSourceKind.External && string.IsNullOrWhiteSpace(job.Subtitles.BurnExternalPath))
            rep.Errors.Add("Elige el archivo de subtítulos que quieres quemar.");

        // --- Salida ---
        var ext = ContainerRules.Extension(job.Container);
        if (string.IsNullOrWhiteSpace(job.OutputPath))
        {
            rep.Errors.Add("Elige dónde guardar el vídeo.");
        }
        else
        {
            if (!job.OutputPath.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) job.OutputPath = Path.ChangeExtension(job.OutputPath, ext);
            // Al cambiar de contenedor (p. ej. de MP4 a MKV) el destino podría acabar siendo el propio original.
            if (SamePath(job.OutputPath, info.Path))
                job.OutputPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(job.OutputPath))!, Path.GetFileNameWithoutExtension(job.OutputPath) + " (Recodec)" + ext);
        }

        return rep;
    }

    // ------------------------------------------------------------------ tamaño de salida

    /// <summary>Recorte de bandas negras que se aplicará (null si no hay o está desactivado).</summary>
    public static CropRect? EffectiveCrop(ConversionJob job) =>
        job.Video.AutoCrop && job.Video.Crop is { } c && job.Source.Video is { } v && (c.Width < v.DisplayWidth || c.Height < v.DisplayHeight) ? c : null;

    /// <summary>Tamaño de la imagen tras girarla y recortar las bandas negras.</summary>
    public static (int Width, int Height) CroppedSize(ConversionJob job)
    {
        var v = job.Source.Video;
        if (v is null) return (0, 0);
        return EffectiveCrop(job) is { } c ? (c.Width, c.Height) : (v.DisplayWidth, v.DisplayHeight);
    }

    /// <summary>Tamaño final del vídeo (al copiar, el original).</summary>
    public static (int Width, int Height) OutputSize(ConversionJob job)
    {
        var v = job.Source.Video;
        if (v is null) return (0, 0);
        if (job.Video.Mode == VideoMode.Copy) return (v.DisplayWidth, v.DisplayHeight);
        var (w, h) = CroppedSize(job);
        return FitResolution(w, h, job.Video.MaxHeight);
    }

    /// <summary>
    /// Ajusta un tamaño a una resolución máxima sin deformar ni ampliar. "1080p" es una caja de 1920×1080
    /// (o 1080×1920 si el vídeo es vertical): un 3840×1600 queda en 1920×800 y un 4:3 de 1440×1080 no cambia.
    /// </summary>
    public static (int Width, int Height) FitResolution(int w, int h, int maxHeight)
    {
        if (maxHeight <= 0 || w <= 0 || h <= 0) return (w, h);
        var maxLong = (int)Math.Round(maxHeight * 16 / 9.0 / 2) * 2;
        var (bw, bh) = w >= h ? (maxLong, maxHeight) : (maxHeight, maxLong);
        if (w <= bw && h <= bh) return (w, h);
        var k = Math.Min((double)bw / w, (double)bh / h);
        static int Even(double x) => Math.Max(2, (int)Math.Round(x / 2) * 2);
        return (Math.Min(Even(w * k), bw), Math.Min(Even(h * k), bh));
    }

    private static bool SamePath(string a, string b)
    {
        try { return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
    }

    public static string CodecLabel(VideoCodec c) => c switch
    {
        VideoCodec.Hevc => "HEVC (H.265)",
        VideoCodec.H264 => "H.264",
        VideoCodec.Av1 => "AV1",
        VideoCodec.Vp9 => "VP9",
        VideoCodec.Xvid => "XviD/DivX",
        _ => c.ToString(),
    };

    /// <summary>Rango del control de calidad (las escalas cambian según el códec).</summary>
    public static (int Min, int Max) QualityRange(VideoCodec c) => c switch
    {
        VideoCodec.Xvid => (2, 16),
        VideoCodec.Vp9 => (15, 50),
        VideoCodec.Av1 => (14, 50),
        _ => (14, 40),
    };
}
