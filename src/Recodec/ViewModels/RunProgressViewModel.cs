using System.Diagnostics;
using System.Globalization;
using System.Windows.Threading;
using Recodec.Core.Conversion;
using Recodec.Core.Media;
using Recodec.Infrastructure;

namespace Recodec.ViewModels;

/// <summary>Lo que muestra la ventana de progreso mientras se convierte un vídeo de la cola.</summary>
public sealed class RunProgressViewModel : ObservableObject
{
    private static readonly CultureInfo Es = new("es-ES");
    private readonly Stopwatch _clock = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };

    public RunProgressViewModel() => _timer.Tick += (_, _) => OnPropertyChanged(nameof(ElapsedText));

    private string _fileName = "";
    private string _itemText = "";
    private string _videoTask = "";
    private string _audioTask = "";
    private string _subtitleTask = "";
    private double _percent;
    private string _remainingText = "Calculando el tiempo que falta…";
    private string _speedText = "—";
    private string _fpsText = "—";
    private string _positionText = "—";
    private string _writtenText = "—";
    private string _finalSizeText = "—";

    public string FileName { get => _fileName; private set => Set(ref _fileName, value); }
    public string ItemText { get => _itemText; private set => Set(ref _itemText, value); }
    public string VideoTask { get => _videoTask; private set => Set(ref _videoTask, value); }
    public string AudioTask { get => _audioTask; private set => Set(ref _audioTask, value); }
    public string SubtitleTask { get => _subtitleTask; private set => Set(ref _subtitleTask, value); }
    public double Percent { get => _percent; private set { if (Set(ref _percent, value)) OnPropertyChanged(nameof(PercentText)); } }
    public string PercentText => $"{Math.Floor(Percent):0} %";
    public string RemainingText { get => _remainingText; private set => Set(ref _remainingText, value); }
    public string SpeedText { get => _speedText; private set => Set(ref _speedText, value); }
    public string FpsText { get => _fpsText; private set => Set(ref _fpsText, value); }
    public string PositionText { get => _positionText; private set => Set(ref _positionText, value); }
    public string WrittenText { get => _writtenText; private set => Set(ref _writtenText, value); }
    public string FinalSizeText { get => _finalSizeText; private set => Set(ref _finalSizeText, value); }
    public string ElapsedText => Clock(_clock.Elapsed);

    /// <summary>Empieza un vídeo nuevo de la cola.</summary>
    public void Start(ConversionJob job, int index, int total)
    {
        FileName = job.Source.FileName;
        ItemText = total > 1 ? $"Vídeo {index} de {total}" : "Convirtiendo";
        VideoTask = DescribeVideo(job);
        AudioTask = job.AudioTracks.Count == 0 ? "Sin audio" : string.Join(" · ", job.AudioTracks.Select(CommandBuilder.AutoAudioTitle));
        SubtitleTask = DescribeSubtitles(job);
        Percent = 0;
        RemainingText = "Calculando el tiempo que falta…";
        SpeedText = FpsText = WrittenText = FinalSizeText = "—";
        PositionText = $"0:00:00 de {Clock(job.EffectiveDuration)}";
        _clock.Restart();
        _timer.Start();
        OnPropertyChanged(nameof(ElapsedText));
    }

    public void Update(ConversionProgress p)
    {
        Percent = p.Percent;
        RemainingText = p.Remaining is { } r ? "quedan " + Remaining(r) : "Calculando el tiempo que falta…";
        if (p.Speed > 0) SpeedText = p.Speed.ToString("0.0", Es) + "×";
        if (p.Fps > 0) FpsText = p.Fps.ToString("0", Es);
        PositionText = $"{Clock(p.Processed)} de {Clock(p.Total)}";
        if (p.BytesWritten > 0)
        {
            WrittenText = Size(p.BytesWritten);
            // El tamaño final se calcula en cuanto hay algo de vídeo hecho; al principio varía mucho.
            FinalSizeText = p.Percent >= 2 ? "≈ " + Size((long)(p.BytesWritten / (p.Percent / 100))) : "calculando…";
        }
    }

    public void Stop()
    {
        _timer.Stop();
        _clock.Stop();
    }

    private static string DescribeVideo(ConversionJob job)
    {
        var v = job.Source.Video;
        if (v is null) return "Sin vídeo";
        if (job.Video.Mode == VideoMode.Copy) return $"Copiando el vídeo {CodecNames.Display(v.Codec)} sin recodificar (sin pérdida de calidad)";
        var who = job.Video.Backend == EncoderBackend.Cpu ? "el procesador" : "la gráfica " + HardwareEncoders.VendorName(job.Video.Backend);
        var parts = new List<string> { $"Recodificando a {JobPlanner.CodecLabel(job.Video.Codec)} con {who}" };
        var (w, h) = JobPlanner.OutputSize(job);
        if (w > 0) parts.Add($"{w}×{h}");
        if (JobPlanner.EffectiveCrop(job) is not null) parts.Add("sin bandas negras");
        if (job.Video.Denoise != DenoiseMode.Off) parts.Add("quitando ruido");
        if (CommandBuilder.NeedsToneMap(job, v)) parts.Add("HDR pasado a SDR");
        return string.Join(" · ", parts);
    }

    private static string DescribeSubtitles(ConversionJob job)
    {
        var parts = new List<string>();
        if (job.Subtitles.Burns) parts.Add("Quemando subtítulos en la imagen");
        var n = job.Subtitles.Keep.Count;
        if (n > 0) parts.Add(n == 1 ? "1 pista de subtítulos" : $"{n} pistas de subtítulos");
        return parts.Count == 0 ? "Sin subtítulos" : string.Join(" · ", parts);
    }

    private static string Clock(TimeSpan t) => $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}";

    private static string Remaining(TimeSpan r) =>
        r.TotalHours >= 1 ? $"{(int)r.TotalHours} h {r.Minutes} min" : r.TotalMinutes >= 1 ? $"unos {Math.Ceiling(r.TotalMinutes):0} min" : $"{r.Seconds} s";

    private static string Size(long bytes) =>
        bytes >= 1L << 30 ? (bytes / (double)(1L << 30)).ToString("0.0", Es) + " GB" : (bytes / (double)(1L << 20)).ToString("0", Es) + " MB";
}
