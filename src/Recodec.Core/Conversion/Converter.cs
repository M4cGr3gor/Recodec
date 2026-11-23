using System.Globalization;
using Recodec.Core.Ffmpeg;

namespace Recodec.Core.Conversion;

/// <param name="Fps">Fotogramas por segundo que se están procesando.</param>
/// <param name="BytesWritten">Lo que ya se ha escrito en el archivo de salida.</param>
public sealed record ConversionProgress(double Percent, TimeSpan Processed, TimeSpan Total, double Speed, TimeSpan? Remaining, double Fps = 0, long BytesWritten = 0);

public sealed record ConversionResult(bool Success, bool Cancelled, string? Error, string CommandLine);

/// <summary>Ejecuta una conversión e informa del progreso leyendo la salida "-progress" de ffmpeg.</summary>
public sealed class Converter
{
    private readonly FfmpegTools _tools;
    public Converter(FfmpegTools tools) => _tools = tools;

    public async Task<ConversionResult> RunAsync(ConversionJob job, IProgress<ConversionProgress>? progress, Action<string>? log, CancellationToken ct)
    {
        var args = CommandBuilder.Build(job);
        var cmd = ProcessRunner.FormatCommandLine(_tools.Ffmpeg, args);
        log?.Invoke("> " + cmd);

        var dir = Path.GetDirectoryName(job.OutputPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var total = job.EffectiveDuration;
        var outTime = TimeSpan.Zero;
        double speed = 0;
        double fps = 0;
        long bytes = 0;
        var errors = new List<string>();

        void OnOut(string line)
        {
            var eq = line.IndexOf('=');
            if (eq <= 0) return;
            var key = line[..eq];
            var val = line[(eq + 1)..].Trim();
            switch (key)
            {
                case "out_time_us" when long.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out var us) && us >= 0:
                    outTime = TimeSpan.FromTicks(us * 10);
                    break;
                case "speed" when double.TryParse(val.TrimEnd('x'), NumberStyles.Float, CultureInfo.InvariantCulture, out var sp):
                    speed = sp;
                    break;
                case "fps" when double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var f):
                    fps = f;
                    break;
                case "total_size" when long.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out var b) && b > 0:
                    bytes = b;
                    break;
                case "progress":
                    var pct = total.TotalSeconds > 0 ? Math.Clamp(outTime.TotalSeconds / total.TotalSeconds * 100, 0, 100) : 0;
                    if (val == "end") pct = 100;
                    TimeSpan? rem = speed > 0.01 && total > outTime ? TimeSpan.FromSeconds((total - outTime).TotalSeconds / speed) : null;
                    progress?.Report(new ConversionProgress(pct, outTime, total, speed, rem, fps, bytes));
                    break;
            }
        }

        void OnErr(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            lock (errors) { errors.Add(line); if (errors.Count > 200) errors.RemoveAt(0); }
            log?.Invoke(line);
        }

        try
        {
            var r = await ProcessRunner.RunAsync(_tools.Ffmpeg, args, OnOut, OnErr, ct, captureStdOut: false);
            if (r.ExitCode == 0) return new ConversionResult(true, false, null, cmd);
            TryDelete(job.OutputPath);
            var last = errors.LastOrDefault(e => !e.StartsWith("[") || e.Contains("rror")) ?? errors.LastOrDefault() ?? $"ffmpeg terminó con código {r.ExitCode}";
            return new ConversionResult(false, false, last, cmd);
        }
        catch (OperationCanceledException)
        {
            await Task.Delay(300, CancellationToken.None);
            TryDelete(job.OutputPath);
            return new ConversionResult(false, true, null, cmd);
        }
    }

    private static void TryDelete(string path)
    {
        for (var i = 0; i < 5; i++)
        {
            try { if (File.Exists(path)) File.Delete(path); return; }
            catch { Thread.Sleep(200); }
        }
    }
}
