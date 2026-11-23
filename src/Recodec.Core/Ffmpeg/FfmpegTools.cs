using System.Text.RegularExpressions;

namespace Recodec.Core.Ffmpeg;

/// <summary>
/// Localiza ffmpeg.exe y ffprobe.exe dentro de la carpeta del programa (versión portable).
/// No se usa nada instalado en el sistema.
/// </summary>
public sealed class FfmpegTools
{
    public string AppRoot { get; }
    public string FfmpegDir => Path.Combine(AppRoot, "ffmpeg");
    public string Ffmpeg => Path.Combine(FfmpegDir, "ffmpeg.exe");
    public string Ffprobe => Path.Combine(FfmpegDir, "ffprobe.exe");
    public bool IsAvailable => File.Exists(Ffmpeg) && File.Exists(Ffprobe);

    private FfmpegTools(string appRoot) => AppRoot = appRoot;

    /// <summary>
    /// Carpeta raíz del programa: la del .exe. Durante el desarrollo se busca hacia arriba
    /// una carpeta "Recodec\ffmpeg" y, si no hay, se usa el programa instalado en
    /// "C:\Program Files\Recodec" (así las compilaciones de desarrollo no necesitan su propia copia de FFmpeg).
    /// </summary>
    public static FfmpegTools Locate()
    {
        var exeDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        if (File.Exists(Path.Combine(exeDir, "ffmpeg", "ffmpeg.exe"))) return new FfmpegTools(exeDir);

        var dir = new DirectoryInfo(exeDir);
        for (var i = 0; i < 8 && dir is not null; i++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "Recodec");
            if (File.Exists(Path.Combine(candidate, "ffmpeg", "ffmpeg.exe"))) return new FfmpegTools(candidate);
        }
        var installed = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Recodec");
        if (File.Exists(Path.Combine(installed, "ffmpeg", "ffmpeg.exe"))) return new FfmpegTools(installed);
        return new FfmpegTools(exeDir);
    }

    public static FfmpegTools At(string appRoot) => new(appRoot);

    /// <summary>Versión instalada, p. ej. "9.1" (null si no hay FFmpeg).</summary>
    public async Task<string?> GetVersionAsync(CancellationToken ct = default)
    {
        if (!File.Exists(Ffmpeg)) return null;
        var r = await ProcessRunner.RunAsync(Ffmpeg, new[] { "-hide_banner", "-version" }, ct: ct);
        var m = Regex.Match(r.StdOut, @"ffmpeg version (?:n)?([0-9][0-9.]*)");
        return m.Success ? m.Groups[1].Value.TrimEnd('.') : null;
    }

    private bool? _nvenc;

    /// <summary>Olvida el resultado de <see cref="HasNvencAsync"/> (p. ej. tras actualizar FFmpeg).</summary>
    public void ResetCapabilities() => _nvenc = null;

    /// <summary>Comprueba si la tarjeta gráfica puede codificar con NVENC (prueba real de 5 fotogramas).</summary>
    public async Task<bool> HasNvencAsync(CancellationToken ct = default)
    {
        if (_nvenc.HasValue) return _nvenc.Value;
        if (!IsAvailable) return false;
        var r = await ProcessRunner.RunAsync(Ffmpeg, new[]
        {
            "-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i", "color=black:s=640x360",
            "-frames:v", "5", "-c:v", "hevc_nvenc", "-f", "null", "-",
        }, ct: ct);
        _nvenc = r.ExitCode == 0;
        return _nvenc.Value;
    }
}
