using System.Globalization;
using System.Text.RegularExpressions;
using Recodec.Core.Conversion;
using Recodec.Core.Ffmpeg;

namespace Recodec.Core.Media;

/// <summary>
/// Fotogramas de vista previa y detección de bandas negras.
/// 10 puntos repartidos por la película y un recorte conservador
/// (solo se recorta lo que es negro en todos los puntos).
/// </summary>
public sealed class PreviewService
{
    public const int PreviewCount = 10;
    private readonly FfmpegTools _tools;
    public PreviewService(FfmpegTools tools) => _tools = tools;

    public static TimeSpan PreviewTime(MediaInfo info, int i)
    {
        var d = info.Duration > TimeSpan.Zero ? info.Duration : TimeSpan.FromSeconds(60);
        return TimeSpan.FromTicks(d.Ticks / (PreviewCount + 1) * (i + 1));
    }

    /// <summary>Extrae un fotograma como PNG. Los vídeos HDR se ajustan a SDR para que se vean con colores correctos en pantalla.</summary>
    public async Task<byte[]?> GetFrameAsync(MediaInfo info, TimeSpan at, int maxWidth = 640, CancellationToken ct = default)
    {
        var v = info.Video;
        if (v is null) return null;
        var tmp = Path.Combine(Path.GetTempPath(), $"recodec_{Guid.NewGuid():N}.png");
        // Primero se reduce el tamaño: ajustar el HDR de un fotograma 4K en coma flotante cuesta mucho más.
        var vf = new List<string> { $"scale=w='min(iw,{maxWidth})':h=-2" };
        if (v.Hdr != HdrKind.None)
            vf.Add("zscale=t=linear:npl=100,format=gbrpf32le,zscale=p=bt709,tonemap=tonemap=hable:desat=0,zscale=t=bt709:m=bt709:r=tv,format=yuv420p");
        try
        {
            var r = await ProcessRunner.RunAsync(_tools.Ffmpeg, new[]
            {
                "-hide_banner", "-loglevel", "error", "-ss", Sec(at), "-i", info.Path, "-map", $"0:{v.Index}",
                "-frames:v", "1", "-vf", string.Join(",", vf), "-y", tmp,
            }, ct: ct);
            if (r.ExitCode != 0 || !File.Exists(tmp)) return null;
            return await File.ReadAllBytesAsync(tmp, ct);
        }
        finally
        {
            try { File.Delete(tmp); } catch { }
        }
    }

    public async Task<CropRect?> DetectCropAsync(MediaInfo info, CancellationToken ct = default)
    {
        var v = info.Video;
        if (v is null || v.Width == 0) return null;
        // cropdetect trabaja con la imagen ya girada: se compara con el tamaño tal como se ve.
        int vw = v.DisplayWidth, vh = v.DisplayHeight;

        // Cada análisis decodifica vídeo (un 4K HEVC pesa): como mucho 4 a la vez para no bloquear el equipo.
        using var gate = new SemaphoreSlim(Math.Clamp(Environment.ProcessorCount / 4, 2, 4));
        var tasks = Enumerable.Range(0, PreviewCount).Select(async i =>
        {
            await gate.WaitAsync(ct);
            ProcessResult r;
            try
            {
                r = await ProcessRunner.RunAsync(_tools.Ffmpeg, new[]
                {
                    "-hide_banner", "-ss", Sec(PreviewTime(info, i)), "-i", info.Path, "-map", $"0:{v.Index}",
                    "-frames:v", "4", "-vf", "cropdetect=limit=0.094:round=2:reset=0", "-f", "null", "-",
                }, ct: ct);
            }
            finally { gate.Release(); }
            var matches = Regex.Matches(r.StdErr, @"crop=(\d+):(\d+):(\d+):(\d+)");
            if (matches.Count == 0) return null;
            var m = matches[^1];
            int P(int g) => int.Parse(m.Groups[g].Value, CultureInfo.InvariantCulture);
            return new CropRect(P(1), P(2), P(3), P(4));
        });

        var results = (await Task.WhenAll(tasks)).Where(c => c is not null && c.Width > 0 && c.Height > 0).Cast<CropRect>().ToList();
        // Fotogramas completamente negros (fundidos) dan rectángulos absurdos: se descartan.
        results = results.Where(c => c.Width >= vw / 3 && c.Height >= vh / 3).ToList();
        if (results.Count < 3) return null;

        // Desplazamientos pares: en vídeo 4:2:0 un recorte impar descoloca el color media línea.
        var left = results.Min(c => c.X) / 2 * 2;
        var top = results.Min(c => c.Y) / 2 * 2;
        var right = results.Max(c => c.X + c.Width);
        var bottom = results.Max(c => c.Y + c.Height);
        var w = (right - left) / 2 * 2;
        var h = (bottom - top) / 2 * 2;

        // Si apenas hay nada que recortar (<1 %), no se recorta.
        if (w >= vw * 0.99 && h >= vh * 0.99) return null;
        return new CropRect(w, h, left, top);
    }

    private static string Sec(TimeSpan t) => t.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture);
}
