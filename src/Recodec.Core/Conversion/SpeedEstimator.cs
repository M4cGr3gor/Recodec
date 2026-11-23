namespace Recodec.Core.Conversion;

/// <summary>
/// Calcula cuánto tardará una conversión. Parte de unas velocidades de referencia y las corrige con lo que
/// han tardado de verdad las conversiones anteriores en este equipo (se guardan en la configuración).
/// Las velocidades se guardan "normalizadas": fotogramas por segundo equivalentes a 1080p con velocidad 0.
/// </summary>
public static class SpeedEstimator
{
    /// <summary>Velocidad de referencia (fotogramas/s en 1080p, velocidad "muy rápido").</summary>
    public static double DefaultRate(VideoCodec codec, EncoderBackend backend) => (codec, backend) switch
    {
        // Medido: HEVC 4K por NVIDIA (RTX 5070, velocidad equilibrada) va a unas 5 veces el tiempo real.
        (VideoCodec.H264, EncoderBackend.Gpu) => 850,
        (VideoCodec.Hevc, EncoderBackend.Gpu) => 650,
        (VideoCodec.Av1, EncoderBackend.Gpu) => 600,
        (VideoCodec.H264, EncoderBackend.Qsv or EncoderBackend.Amf) => 500,
        (VideoCodec.Hevc, EncoderBackend.Qsv or EncoderBackend.Amf) => 380,
        (VideoCodec.H264, _) => 240,
        (VideoCodec.Hevc, _) => 80,
        (VideoCodec.Xvid, _) => 340,
        (VideoCodec.Vp9, _) => 20,
        _ => 10, // AV1 por procesador
    };

    public static string Key(ConversionJob job) => $"{job.Video.Codec}-{job.Video.Backend}";

    /// <summary>Trabajo por fotograma: la mitad es leer el original y la otra mitad, comprimir el resultado (en 1080p = 1).</summary>
    private static double Pixels(ConversionJob job)
    {
        var (w, h) = JobPlanner.CroppedSize(job);
        var (ow, oh) = JobPlanner.OutputSize(job);
        return Math.Max((w * (double)h + ow * (double)oh) / 2 / (1920.0 * 1080), 0.3);
    }

    private static double SpeedFactor(ConversionJob job) => 1 + 0.35 * Math.Clamp(job.Video.Speed, 0, 4);

    private static double Frames(ConversionJob job)
    {
        var fps = job.Source.Video?.FrameRate is > 0 and var f ? f : 24;
        return job.EffectiveDuration.TotalSeconds * fps;
    }

    /// <summary>Tiempo estimado y si se basa en conversiones anteriores de este equipo.</summary>
    public static (TimeSpan Time, bool Learned) Estimate(ConversionJob job, IReadOnlyDictionary<string, double>? learned)
    {
        var v = job.Source.Video;
        if (v is null || job.Video.Mode == VideoMode.Copy)
        {
            // Copiar: lo marca el disco (unos 150 MB/s) y la conversión del audio (unas 150 veces el tiempo real).
            var seconds = Math.Max(job.Source.SizeBytes / 150e6, job.EffectiveDuration.TotalSeconds / 150);
            return (TimeSpan.FromSeconds(seconds), false);
        }
        var fromHistory = learned is not null && learned.TryGetValue(Key(job), out var rate) && rate > 0;
        var normalized = fromHistory ? learned![Key(job)] : DefaultRate(job.Video.Codec, job.Video.Backend);
        var fps = normalized / Pixels(job) / SpeedFactor(job);
        if (job.Video.Denoise == DenoiseMode.Max && v.BitDepth <= 8) fps = Math.Min(fps, 7 / Pixels(job));
        return (TimeSpan.FromSeconds(Frames(job) / fps), fromHistory);
    }

    /// <summary>Aprende de una conversión terminada (solo recodificaciones de más de un minuto de vídeo).</summary>
    public static void Learn(ConversionJob job, TimeSpan elapsed, IDictionary<string, double> learned)
    {
        if (job.Video.Mode != VideoMode.Encode || job.Source.Video is null || job.EffectiveDuration.TotalSeconds < 60 || elapsed.TotalSeconds < 5) return;
        // El ruido máximo (NLMeans) es mucho más lento y falsearía la velocidad del codificador.
        if (job.Video.Denoise == DenoiseMode.Max) return;
        var measured = Frames(job) / elapsed.TotalSeconds * Pixels(job) * SpeedFactor(job);
        var key = Key(job);
        learned[key] = learned.TryGetValue(key, out var old) && old > 0 ? 0.5 * old + 0.5 * measured : measured;
    }
}
