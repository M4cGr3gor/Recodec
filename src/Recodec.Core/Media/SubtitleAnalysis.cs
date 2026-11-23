using System.Globalization;
using Recodec.Core.Ffmpeg;

namespace Recodec.Core.Media;

/// <summary>
/// Distingue los subtítulos completos (todos los diálogos) de los forzados (solo las partes en otro idioma,
/// carteles, etc.). Muchas películas no marcan los forzados, pero se delatan por el número de líneas:
/// unos subtítulos completos tienen entre 8 y 15 por minuto; unos forzados, de unas pocas a unas decenas en total.
/// </summary>
public static class SubtitleAnalysis
{
    /// <summary>Por debajo de esto (líneas por minuto de película) son forzados.</summary>
    public const double ForcedRatePerMinute = 2.0;
    /// <summary>Si otro subtítulo del archivo tiene muchas líneas, menos de esta fracción se considera forzado.</summary>
    public const double ForcedFractionOfLargest = 0.25;

    public static bool NeedsCounting(MediaInfo info) => info.SubtitleStreams.Any(s => s.EventCount is null);

    /// <summary>
    /// Cuenta las líneas de los subtítulos que no traen ese dato en el archivo. Hay que leer el archivo entero
    /// (sin decodificar nada), así que en una película grande tarda desde unos segundos hasta un par de minutos.
    /// </summary>
    public static async Task CountAsync(FfmpegTools tools, MediaInfo info, CancellationToken ct = default)
    {
        // Se cuentan los paquetes con contenido: MP4 rellena los huecos entre subtítulos con muestras vacías de 2 bytes.
        var r = await ProcessRunner.RunAsync(tools.Ffprobe, new[]
        {
            "-v", "error", "-select_streams", "s", "-show_entries", "packet=stream_index,size", "-of", "csv=p=0", info.Path,
        }, ct: ct);
        if (r.ExitCode != 0) return;
        var counts = new Dictionary<int, long>();
        foreach (var line in r.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var p = line.Split(',');
            if (p.Length < 2 || !int.TryParse(p[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var idx)
                             || !int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var size) || size <= 2) continue;
            counts[idx] = counts.GetValueOrDefault(idx) + 1;
        }
        foreach (var s in info.SubtitleStreams.Where(x => x.EventCount is null))
            s.EventCount = EventsFromPackets(s, counts.GetValueOrDefault(s.Index));
        Classify(info);
    }

    /// <summary>En los subtítulos PGS (Blu-ray) cada línea ocupa dos paquetes: uno para mostrarla y otro para borrarla.</summary>
    public static int EventsFromPackets(StreamInfo s, long packets) =>
        (int)(s.Codec == "hdmv_pgs_subtitle" ? (packets + 1) / 2 : packets);

    /// <summary>Marca como forzados los subtítulos con muy pocas líneas.</summary>
    public static void Classify(MediaInfo info)
    {
        var subs = info.SubtitleStreams.Where(s => s.EventCount is > 0).ToList();
        if (subs.Count == 0) return;
        var largest = subs.Max(s => s.EventCount!.Value);
        var minutes = info.Duration.TotalMinutes;
        foreach (var s in subs)
        {
            var n = s.EventCount!.Value;
            var title = s.Title ?? "";
            // Si el título dice que son completos (o para sordos), se respeta.
            if (title.Contains("complet", StringComparison.OrdinalIgnoreCase) || title.Contains("full", StringComparison.OrdinalIgnoreCase)
                || title.Contains("sdh", StringComparison.OrdinalIgnoreCase) || title.Contains("sordos", StringComparison.OrdinalIgnoreCase))
            {
                s.LooksForced = false;
                continue;
            }
            s.LooksForced = (minutes >= 20 && n / minutes < ForcedRatePerMinute)
                            || (largest >= 300 && n < largest * ForcedFractionOfLargest);
        }
    }
}
