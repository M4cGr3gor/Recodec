using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;

namespace Recodec.Core.Ffmpeg;

public sealed record UpdateCheck(string? Installed, string? Latest, bool UpdateAvailable);

/// <summary>
/// Actualiza FFmpeg desde las compilaciones "essentials" de gyan.dev (las que enlaza ffmpeg.org para Windows).
/// Comprueba la firma SHA-256, conserva solo ffmpeg.exe, ffprobe.exe y la licencia, y vuelve atrás si algo falla.
/// </summary>
public sealed class FfmpegUpdater
{
    public const string VersionUrl = "https://www.gyan.dev/ffmpeg/builds/release-version";
    public const string ZipUrl = "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip";
    public const string ShaUrl = "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip.sha256";

    private static readonly string[] KeepFiles = { "ffmpeg.exe", "ffprobe.exe", "LICENSE" };
    private readonly FfmpegTools _tools;
    private static readonly HttpClient Http = CreateClient();

    public FfmpegUpdater(FfmpegTools tools) => _tools = tools;

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("Recodec/1.0");
        return c;
    }

    public async Task<UpdateCheck> CheckAsync(CancellationToken ct = default)
    {
        var installed = await _tools.GetVersionAsync(ct);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(15));
        var latest = (await Http.GetStringAsync(VersionUrl, cts.Token)).Trim();
        return new UpdateCheck(installed, latest, IsNewer(latest, installed));
    }

    public static bool IsNewer(string? latest, string? installed)
    {
        if (string.IsNullOrWhiteSpace(latest)) return false;
        if (string.IsNullOrWhiteSpace(installed)) return true;
        return Version.TryParse(Pad(latest), out var l) && Version.TryParse(Pad(installed), out var i) ? l > i : !string.Equals(latest, installed, StringComparison.Ordinal);
        static string Pad(string v) => v.Contains('.') ? v : v + ".0";
    }

    /// <summary>Descarga e instala la última versión. Informa del progreso de descarga (0..100).</summary>
    public async Task<string> InstallLatestAsync(IProgress<double>? progress, Action<string>? log, CancellationToken ct = default)
    {
        var dir = _tools.FfmpegDir;
        Directory.CreateDirectory(dir);
        var work = Path.Combine(dir, "_update");
        if (Directory.Exists(work)) Directory.Delete(work, true);
        Directory.CreateDirectory(work);
        var zip = Path.Combine(work, "ffmpeg.zip");
        var staging = Path.Combine(work, "nuevo");
        var backup = Path.Combine(work, "anterior");

        try
        {
            log?.Invoke("Descargando FFmpeg…");
            using (var resp = await Http.GetAsync(ZipUrl, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                resp.EnsureSuccessStatusCode();
                var total = resp.Content.Headers.ContentLength ?? 0;
                await using var src = await resp.Content.ReadAsStreamAsync(ct);
                await using var dst = File.Create(zip);
                var buf = new byte[1 << 20];
                long done = 0;
                int n;
                while ((n = await src.ReadAsync(buf, ct)) > 0)
                {
                    await dst.WriteAsync(buf.AsMemory(0, n), ct);
                    done += n;
                    if (total > 0) progress?.Report(done * 100.0 / total);
                }
            }

            log?.Invoke("Comprobando la firma SHA-256…");
            var expected = (await Http.GetStringAsync(ShaUrl, ct)).Trim().Split(' ', '\t', '\n')[0].Trim();
            string actual;
            await using (var fs = File.OpenRead(zip)) actual = Convert.ToHexString(await SHA256.HashDataAsync(fs, ct));
            if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("La firma del archivo descargado no coincide. No se ha instalado nada.");

            log?.Invoke("Extrayendo ffmpeg.exe y ffprobe.exe…");
            Directory.CreateDirectory(staging);
            using (var archive = ZipFile.OpenRead(zip))
            {
                foreach (var e in archive.Entries)
                {
                    var isBin = e.FullName.Contains("/bin/", StringComparison.OrdinalIgnoreCase) && (e.Name == "ffmpeg.exe" || e.Name == "ffprobe.exe");
                    var isLicense = e.Name == "LICENSE" && e.FullName.Count(c => c == '/') == 1;
                    if (isBin || isLicense) e.ExtractToFile(Path.Combine(staging, e.Name), true);
                }
            }
            if (!File.Exists(Path.Combine(staging, "ffmpeg.exe")) || !File.Exists(Path.Combine(staging, "ffprobe.exe")))
                throw new InvalidOperationException("El archivo descargado no contiene ffmpeg.exe y ffprobe.exe.");

            var stagedVersion = await ProcessRunner.RunAsync(Path.Combine(staging, "ffmpeg.exe"), new[] { "-hide_banner", "-version" }, ct: ct);
            if (stagedVersion.ExitCode != 0) throw new InvalidOperationException("La nueva versión de FFmpeg no arranca. No se ha instalado nada.");

            log?.Invoke("Sustituyendo la versión anterior…");
            Directory.CreateDirectory(backup);
            foreach (var f in KeepFiles)
            {
                var cur = Path.Combine(dir, f);
                if (File.Exists(cur)) File.Move(cur, Path.Combine(backup, f), true);
            }
            try
            {
                foreach (var f in KeepFiles)
                {
                    var src = Path.Combine(staging, f);
                    if (File.Exists(src)) File.Move(src, Path.Combine(dir, f), true);
                }
                var v = await _tools.GetVersionAsync(ct);
                if (v is null) throw new InvalidOperationException("La nueva versión no responde.");
                log?.Invoke($"FFmpeg {v} instalado.");
                return v;
            }
            catch
            {
                log?.Invoke("Algo ha fallado: se restaura la versión anterior.");
                foreach (var f in KeepFiles)
                {
                    var b = Path.Combine(backup, f);
                    if (File.Exists(b)) File.Move(b, Path.Combine(dir, f), true);
                }
                throw;
            }
        }
        finally
        {
            try { Directory.Delete(work, true); } catch { /* se limpiará en la próxima actualización */ }
            CleanUnexpectedFiles(dir);
        }
    }

    /// <summary>Elimina de la carpeta ffmpeg todo lo que no sea necesario (p. ej. ffplay.exe, documentación).</summary>
    public static void CleanUnexpectedFiles(string dir)
    {
        if (!Directory.Exists(dir)) return;
        foreach (var f in Directory.GetFiles(dir))
            if (!KeepFiles.Contains(Path.GetFileName(f), StringComparer.OrdinalIgnoreCase))
                try { File.Delete(f); } catch { }
        foreach (var d in Directory.GetDirectories(dir))
            try { Directory.Delete(d, true); } catch { }
    }
}
