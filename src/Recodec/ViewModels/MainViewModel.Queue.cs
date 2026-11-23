using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using Recodec.Core.Conversion;
using Recodec.Infrastructure;

namespace Recodec.ViewModels;

public sealed partial class MainViewModel
{
    public ObservableCollection<QueueItemViewModel> Queue { get; } = new();

    /// <summary>Datos de la ventana de progreso.</summary>
    public RunProgressViewModel Run { get; } = new();

    private bool _isRunning;
    private double _progress;
    private string _statusText = "Listo";
    private string _progressText = "";

    public bool IsRunning { get => _isRunning; private set { if (Set(ref _isRunning, value)) { OnPropertyChanged(nameof(IsIdle)); System.Windows.Input.CommandManager.InvalidateRequerySuggested(); } } }
    public bool IsIdle => !IsRunning;
    public double Progress { get => _progress; private set => Set(ref _progress, value); }
    public string StatusText { get => _statusText; private set => Set(ref _statusText, value); }
    public string ProgressText { get => _progressText; private set => Set(ref _progressText, value); }
    public int PendingCount => Queue.Count(q => q.State == QueueState.Pending);
    public string QueueHeader => Queue.Count == 0 ? "Cola" : $"Cola ({PendingCount + Queue.Count(q => q.State == QueueState.Running)})";
    public bool QueueIsEmpty => Queue.Count == 0;

    public static IReadOnlyList<Option<WhenDoneAction>> WhenDoneOptions { get; } = new[]
    {
        new Option<WhenDoneAction>(WhenDoneAction.Nothing, "No hacer nada"),
        new Option<WhenDoneAction>(WhenDoneAction.Sleep, "Suspender el equipo"),
        new Option<WhenDoneAction>(WhenDoneAction.Shutdown, "Apagar el equipo"),
    };

    // No se guarda en la configuración: apagar el equipo tiene que ser una decisión de cada vez.
    private Option<WhenDoneAction> _whenDone = WhenDoneOptions[0];
    public Option<WhenDoneAction> SelectedWhenDone { get => _whenDone; set { if (value is not null) Set(ref _whenDone, value); } }

    private void Enqueue(bool start)
    {
        if (_job is null) return;
        var rep = JobPlanner.Normalize(_job);
        if (!rep.CanRun) { _dialogs.Info(string.Join("\n", rep.Errors), "No se puede convertir"); return; }

        if (Queue.Any(q => q.State is QueueState.Pending or QueueState.Running &&
                           string.Equals(q.OutputPath, _job.OutputPath, StringComparison.OrdinalIgnoreCase)))
        {
            _dialogs.Info("Ya hay un vídeo en la cola que se guardará con ese mismo nombre. Cambia el destino en \"Guardar en\".", "Destino repetido");
            return;
        }
        if (File.Exists(_job.OutputPath) &&
            !_dialogs.Confirm($"Ya existe el archivo:\n{_job.OutputPath}\n\n¿Quieres sustituirlo?", "El archivo ya existe"))
            return;

        AddToQueue(_job.Clone());
        if (start && !IsRunning && !IsUpdating) _ = RunQueueAsync();
        else if (!start) StatusText = $"Añadido a la cola: {_info?.FileName} (botón \"Cola\" de arriba)";
    }

    /// <summary>Si el destino ya existe o lo usa otro vídeo de la cola, añade " (2)", " (3)"…</summary>
    private string UniqueOutputPath(string path)
    {
        bool Taken(string p) => File.Exists(p) || Queue.Any(q => q.State is QueueState.Pending or QueueState.Running &&
                                                                  string.Equals(q.OutputPath, p, StringComparison.OrdinalIgnoreCase));
        if (!Taken(path)) return path;
        var dir = Path.GetDirectoryName(path) ?? "";
        var name = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        for (var n = 2; ; n++)
        {
            var candidate = Path.Combine(dir, $"{name} ({n}){ext}");
            if (!Taken(candidate)) return candidate;
        }
    }

    private void AddToQueue(ConversionJob job)
    {
        var video = job.Source.Video is null ? "sin vídeo"
            : job.Video.Mode == VideoMode.Copy ? "vídeo copiado"
            : $"{JobPlanner.CodecLabel(job.Video.Codec)} {(job.Video.Backend == EncoderBackend.Cpu ? "procesador" : HardwareEncoders.VendorName(job.Video.Backend))} calidad {job.Video.Quality}";
        var audio = job.AudioTracks.Count == 0 ? "sin audio" : string.Join(", ", job.AudioTracks.Select(CommandBuilder.AutoAudioTitle));
        var summary = $"{ContainerRules.DisplayName(job.Container)} · {video} · {audio}" + (job.Subtitles.Burns ? " · subtítulos quemados" : "");
        Queue.Add(new QueueItemViewModel(job, summary,
            q => { Queue.Remove(q); OnPropertyChanged(nameof(QueueHeader)); OnPropertyChanged(nameof(QueueIsEmpty)); },
            q => OpenInExplorer(q.OutputPath)));
        OnPropertyChanged(nameof(QueueHeader));
        OnPropertyChanged(nameof(QueueIsEmpty));
    }

    private async Task RunQueueAsync()
    {
        if (IsRunning) return;
        IsRunning = true;
        var done = 0;
        var failed = 0;
        var cancelled = false;
        string? lastOutput = null;
        Power.KeepAwake(true); // que Windows no suspenda el equipo a mitad de una película
        var position = 0;
        var batchTotal = Queue.Count(q => q.State == QueueState.Pending);
        try
        {
            while (Queue.FirstOrDefault(q => q.State == QueueState.Pending) is { } item)
            {
                _runCts?.Dispose();
                _runCts = new CancellationTokenSource();
                item.State = QueueState.Running;
                position++;
                // Si se añaden vídeos a la cola mientras tanto, el total crece.
                batchTotal = Math.Max(batchTotal, position + PendingCount);
                Run.Start(item.Job, position, batchTotal);
                OnPropertyChanged(nameof(QueueHeader));
                StatusText = $"Convirtiendo {item.FileName}" + (PendingCount > 0 ? $" · quedan {PendingCount} en la cola" : "");
                Log($"=== Inicio: {item.FileName} → {item.OutputPath}");

                var progress = new Progress<ConversionProgress>(p =>
                {
                    item.Progress = p.Percent;
                    Progress = p.Percent;
                    Run.Update(p);
                    var rem = p.Remaining is { } r ? $" · quedan {FormatRemaining(r)}" : "";
                    var spd = p.Speed > 0 ? $" · {p.Speed:0.0}× velocidad" : "";
                    ProgressText = $"{p.Percent:0} %";
                    item.Detail = $"{FormatTime(p.Processed)} de {FormatTime(p.Total)}{spd}{rem}";
                    StatusText = $"Convirtiendo {item.FileName}{spd}{rem}";
                });

                var clock = System.Diagnostics.Stopwatch.StartNew();
                var res = await _converter.RunAsync(item.Job, progress, Log, _runCts.Token);
                if (res.Success)
                {
                    // Lo que ha tardado de verdad mejora las próximas estimaciones.
                    SpeedEstimator.Learn(item.Job, clock.Elapsed, _settings.EncodeSpeeds);
                    _settings.Save();
                    OnPropertyChanged(nameof(EstimateText));
                    item.State = QueueState.Done;
                    item.Progress = 100;
                    var size = new FileInfo(item.OutputPath).Length;
                    item.Detail = $"Terminado · {FormatSize(size)} (antes {FormatSize(item.Job.Source.SizeBytes)})";
                    Log($"=== Terminado: {item.OutputPath}");
                    lastOutput = item.OutputPath;
                    done++;
                }
                else if (res.Cancelled)
                {
                    item.State = QueueState.Cancelled;
                    item.Detail = "Cancelado por el usuario (se ha borrado el archivo a medias)";
                    Log("=== Cancelado");
                    cancelled = true;
                    // Al cancelar se detiene toda la cola.
                    break;
                }
                else
                {
                    item.State = QueueState.Failed;
                    item.Detail = "Error: " + res.Error + " (detalles en la pestaña Registro de la ventana principal)";
                    Log("=== Error: " + res.Error);
                    failed++;
                }
                OnPropertyChanged(nameof(QueueHeader));
            }
        }
        finally
        {
            Power.KeepAwake(false);
            Run.Stop();
            IsRunning = false;
            Progress = 0;
            ProgressText = "";
            _runCts?.Dispose();
            _runCts = null;
            OnPropertyChanged(nameof(QueueHeader));
        }

        StatusText = done + failed == 0 ? "Conversión detenida"
            : failed == 0 ? (done == 1 ? "Conversión terminada" : $"{done} conversiones terminadas")
            : $"{done} terminadas, {failed} con errores (mira la pestaña Registro)";
        var action = SelectedWhenDone.Value;
        if (action != WhenDoneAction.Nothing && !cancelled && done > 0)
        {
            SelectedWhenDone = WhenDoneOptions[0];
            if (_dialogs.ConfirmPowerAction(action)) { Log($"=== {(action == WhenDoneAction.Sleep ? "Suspendiendo" : "Apagando")} el equipo"); Power.Run(action); }
            return;
        }
        if (OpenFolderWhenDone && lastOutput is not null) OpenInExplorer(lastOutput);
    }

    private static string FormatRemaining(TimeSpan r) =>
        r.TotalHours >= 1 ? $"{(int)r.TotalHours} h {r.Minutes} min" : r.TotalMinutes >= 1 ? $"{(int)r.TotalMinutes} min {r.Seconds:00} s" : $"{r.Seconds} s";
}
