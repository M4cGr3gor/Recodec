using System.Diagnostics;
using System.Text;

namespace Recodec.Core.Ffmpeg;

public sealed record ProcessResult(int ExitCode, string StdOut, string StdErr);

/// <summary>Ejecuta ffmpeg/ffprobe sin ventana y con los argumentos bien escapados.</summary>
public static class ProcessRunner
{
    public static async Task<ProcessResult> RunAsync(
        string exe,
        IEnumerable<string> args,
        Action<string>? onStdOutLine = null,
        Action<string>? onStdErrLine = null,
        CancellationToken ct = default,
        bool captureStdOut = true)
    {
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var outDone = new TaskCompletionSource();
        var errDone = new TaskCompletionSource();

        p.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) { outDone.TrySetResult(); return; }
            if (captureStdOut) lock (stdout) stdout.AppendLine(e.Data);
            onStdOutLine?.Invoke(e.Data);
        };
        p.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) { errDone.TrySetResult(); return; }
            lock (stderr) stderr.AppendLine(e.Data);
            onStdErrLine?.Invoke(e.Data);
        };

        p.Start();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();

        using (ct.Register(() =>
        {
            try { if (!p.HasExited) p.Kill(entireProcessTree: true); } catch { /* ya terminó */ }
        }))
        {
            await p.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            await Task.WhenAll(outDone.Task, errDone.Task).ConfigureAwait(false);
        }

        ct.ThrowIfCancellationRequested();
        return new ProcessResult(p.ExitCode, stdout.ToString(), stderr.ToString());
    }

    /// <summary>Formatea un comando para mostrarlo al usuario (no se usa para ejecutar).</summary>
    public static string FormatCommandLine(string exe, IEnumerable<string> args)
    {
        static string Q(string s) =>
            s.Length == 0 || s.Any(c => char.IsWhiteSpace(c) || c is '"' or '|' or '&' or '<' or '>' or '^' or ';' or '(' or ')')
                ? "\"" + s.Replace("\"", "\\\"") + "\""
                : s;
        return string.Join(' ', new[] { Path.GetFileNameWithoutExtension(exe) }.Concat(args.Select(Q)));
    }
}
