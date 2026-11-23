using System.Globalization;
using System.IO;
using Recodec.Core.Conversion;
using Recodec.Core.Ffmpeg;
using Recodec.Core.Media;
using Recodec.Infrastructure;

namespace Recodec.ViewModels;

/// <summary>
/// Ventana "Comprobar sincronización": crea un clip corto con el vídeo y la pista de audio externa
/// (con el retraso elegido) y lo reproduce para ajustar el retraso de oído y de vista.
/// </summary>
public sealed class SyncViewModel : ObservableObject
{
    public static readonly TimeSpan ClipLength = TimeSpan.FromSeconds(20);

    private readonly FfmpegTools _tools;
    private readonly MediaInfo _info;
    private readonly AudioTrackJob _track;
    private readonly bool _gpu;
    private readonly string _tempDir;
    private readonly List<string> _clips = new();
    private CancellationTokenSource? _cts;
    private int _clipCounter;

    public SyncViewModel(FfmpegTools tools, MediaInfo info, AudioTrackJob track, int previewIndex, bool gpu)
    {
        _tools = tools;
        _info = info;
        _track = track;
        _gpu = gpu;
        _delayMs = track.DelayMs;
        _tempDir = Path.Combine(Path.GetTempPath(), "Recodec");
        TrackName = Path.GetFileName(track.ExternalPath) ?? "";

        for (var i = 0; i < PreviewService.PreviewCount; i++)
        {
            var t = PreviewService.PreviewTime(info, i);
            if (info.Duration > ClipLength && t + ClipLength > info.Duration) t = info.Duration - ClipLength;
            Moments.Add(new Option<TimeSpan>(t, $"Preview {i + 1} · minuto {MainViewModel.FormatTime(t)}"));
        }
        _moment = Moments[Math.Clamp(previewIndex, 0, Moments.Count - 1)];

        GenerateCommand = new RelayCommand(() => _ = GenerateAsync(), () => !IsBusy);
        NudgeCommand = new RelayCommand(p =>
        {
            if (p is string s && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var d)) { DelayMs += d; _ = GenerateAsync(); }
        }, _ => !IsBusy);
        ApplyCommand = new RelayCommand(() => { Applied = true; CloseRequested?.Invoke(); });
    }

    public string TrackName { get; }
    public List<Option<TimeSpan>> Moments { get; } = new();
    public RelayCommand GenerateCommand { get; }
    public RelayCommand NudgeCommand { get; }
    public RelayCommand ApplyCommand { get; }

    /// <summary>Se ha pulsado "Usar este retraso".</summary>
    public bool Applied { get; private set; }

    public event Action<string>? ClipReady;
    public event Action? ReleaseClip;
    public event Action? CloseRequested;

    private Option<TimeSpan> _moment;
    public Option<TimeSpan> SelectedMoment
    {
        get => _moment;
        set { if (value is not null && Set(ref _moment, value)) _ = GenerateAsync(); }
    }

    private int _delayMs;
    public int DelayMs
    {
        get => _delayMs;
        set { if (Set(ref _delayMs, Math.Clamp(value, -60_000, 60_000))) OnPropertyChanged(nameof(DelayText)); }
    }

    public string DelayText => DelayMs switch
    {
        0 => "Sin retraso: el audio empieza a la vez que el vídeo.",
        > 0 => $"El audio suena {DelayMs} ms más tarde que en el archivo.",
        _ => $"El audio suena {-DelayMs} ms antes que en el archivo.",
    };

    private bool _isBusy;
    public bool IsBusy { get => _isBusy; private set { if (Set(ref _isBusy, value)) System.Windows.Input.CommandManager.InvalidateRequerySuggested(); } }

    private double _progress;
    public double Progress { get => _progress; private set => Set(ref _progress, value); }

    private string _status = "";
    public string Status { get => _status; private set => Set(ref _status, value); }

    /// <summary>Crea el clip con el retraso actual y avisa a la ventana para que lo reproduzca.</summary>
    public async Task GenerateAsync()
    {
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        ReleaseClip?.Invoke();
        IsBusy = true;
        Progress = 0;
        Status = $"Preparando {ClipLength.TotalSeconds:0} segundos con un retraso de {DelayMs} ms…";
        try
        {
            Directory.CreateDirectory(_tempDir);
            var output = Path.Combine(_tempDir, $"sincronizacion-{Environment.ProcessId}-{++_clipCounter}.mp4");
            _clips.Add(output);
            var args = CommandBuilder.BuildSyncPreview(_info, _track, DelayMs, SelectedMoment.Value, ClipLength, output, _gpu);
            var total = ClipLength.TotalSeconds;
            var errors = new List<string>();
            var r = await ProcessRunner.RunAsync(_tools.Ffmpeg, args,
                line =>
                {
                    if (line.StartsWith("out_time_us=") && long.TryParse(line[12..], out var us) && us > 0)
                        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => Progress = Math.Min(100, us / 1e6 / total * 100));
                },
                line => { lock (errors) errors.Add(line); }, cts.Token, captureStdOut: false);
            if (cts.IsCancellationRequested) return;
            if (r.ExitCode != 0 || !File.Exists(output))
            {
                Status = "No se ha podido crear el clip: " + (errors.LastOrDefault() ?? $"código {r.ExitCode}");
                return;
            }
            Status = $"Reproduciendo con un retraso de {DelayMs} ms. Fíjate en los labios de alguien que hable de cerca.";
            Progress = 100;
            ClipReady?.Invoke(output);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Status = "No se ha podido crear el clip: " + ex.Message; }
        finally
        {
            if (_cts == cts) IsBusy = false;
        }
    }

    /// <summary>Cancela lo que esté en marcha y borra los clips temporales.</summary>
    public void Cleanup()
    {
        _cts?.Cancel();
        foreach (var c in _clips) { try { File.Delete(c); } catch { /* aún en uso: se borrará en otra ocasión */ } }
        try
        {
            foreach (var old in Directory.EnumerateFiles(_tempDir, "sincronizacion-*.mp4"))
                if (File.GetLastWriteTime(old) < DateTime.Now.AddDays(-1)) File.Delete(old);
        }
        catch { }
    }
}
