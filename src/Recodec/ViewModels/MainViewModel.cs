using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media.Imaging;
using Recodec.Core.Conversion;
using Recodec.Core.Devices;
using Recodec.Core.Ffmpeg;
using Recodec.Core.Media;
using Recodec.Core.Settings;
using Recodec.Infrastructure;

namespace Recodec.ViewModels;

public interface IDialogs
{
    string? OpenVideo();
    string? OpenFile(string title, string filter);
    string? SaveVideo(string suggestedPath, ContainerKind container);
    bool Confirm(string message, string title);
    void Info(string message, string title);
    void ShowHelp(string topic);
    void ShowQueue();
    void ShowSettings();
    /// <summary>Abre la ventana de sincronización; devuelve true si se pulsa "Usar este retraso".</summary>
    bool ShowSync(SyncViewModel vm);
    void ShowDevices(DevicesViewModel vm);
    /// <summary>Muestra (o trae al frente) la ventana de progreso.</summary>
    void ShowProgress();
    /// <summary>Pide un texto (por ejemplo, el nombre de un perfil). null = cancelado.</summary>
    string? AskText(string title, string prompt, string initial);
    /// <summary>Cuenta atrás antes de suspender o apagar; false si el usuario lo cancela.</summary>
    bool ConfirmPowerAction(WhenDoneAction action);
}

public sealed partial class MainViewModel : ObservableObject
{
    private static readonly CultureInfo Es = new("es-ES");

    private readonly IDialogs _dialogs;
    private readonly AppSettings _settings;
    private readonly FfmpegTools _tools;
    private readonly MediaProber _prober;
    private readonly PreviewService _preview;
    private readonly Converter _converter;
    private readonly Dictionary<int, BitmapImage?> _frameCache = new();
    private CancellationTokenSource? _loadCts;
    private CancellationTokenSource? _runCts;
    /// <summary>Codificadores de tarjeta gráfica que funcionan en este equipo (se detectan al arrancar).</summary>
    private HardwareEncoders _hw = HardwareEncoders.None;

    public MainViewModel(IDialogs dialogs)
    {
        _dialogs = dialogs;
        _tools = FfmpegTools.Locate();
        _settings = AppSettings.Load(Path.Combine(_tools.AppRoot, "Recodec.settings.json"));
        _prober = new MediaProber(_tools);
        _preview = new PreviewService(_tools);
        _converter = new Converter(_tools);
        _updater = new FfmpegUpdater(_tools);

        OpenCommand = new RelayCommand(() => { var p = _dialogs.OpenVideo(); if (p is not null) _ = LoadAsync(p); }, () => !IsLoading && !IsRunning);
        HelpCommand = new RelayCommand(p => _dialogs.ShowHelp(p as string ?? "inicio"));
        PrevFrameCommand = new RelayCommand(() => _ = ShowFrameAsync((PreviewIndex + PreviewService.PreviewCount - 1) % PreviewService.PreviewCount), () => HasFile);
        NextFrameCommand = new RelayCommand(() => _ = ShowFrameAsync((PreviewIndex + 1) % PreviewService.PreviewCount), () => HasFile);
        BrowseOutputCommand = new RelayCommand(BrowseOutput, () => HasFile);
        AddConvertedTrackCommand = new RelayCommand(() => AddSourceTrack(false), () => SelectedSourceAudio is not null);
        AddOriginalTrackCommand = new RelayCommand(() => AddSourceTrack(true), () => SelectedSourceAudio is not null);
        AddExternalTrackCommand = new RelayCommand(() => _ = AddExternalTrackAsync(), () => HasFile);
        BrowseBurnFileCommand = new RelayCommand(BrowseBurnFile, () => HasFile);
        ReselectTracksCommand = new RelayCommand(ApplyDefaultsForCurrentFile, () => HasFile);
        EnqueueCommand = new RelayCommand(() => Enqueue(start: false), () => HasFile && CanRun && !IsRunning);
        ConvertCommand = new RelayCommand(() => Enqueue(start: true), () => HasFile && CanRun && !IsUpdating);
        StartQueueCommand = new RelayCommand(() => _ = RunQueueAsync(), () => !IsRunning && !IsUpdating && Queue.Any(q => q.State == QueueState.Pending));
        ResetQualityCommand = new RelayCommand(() =>
        {
            if (_job is null) return;
            _job.Video.Quality = JobPlanner.RecommendedQuality(_job.Video.Codec, _job.Video.Backend);
            Revalidate();
        });
        CancelCommand = new RelayCommand(() => _runCts?.Cancel(), () => IsRunning);
        ClearFinishedCommand = new RelayCommand(() => { foreach (var q in Queue.Where(q => q.State is QueueState.Done or QueueState.Failed or QueueState.Cancelled).ToList()) Queue.Remove(q); OnPropertyChanged(nameof(QueueHeader)); OnPropertyChanged(nameof(QueueIsEmpty)); });
        CopyLogCommand = new RelayCommand(() => Clipboard.SetText(LogText));
        CopyCommandCommand = new RelayCommand(() => { if (_job is not null) Clipboard.SetText(CommandPreview); }, () => HasFile);
        UpdateFfmpegCommand = new RelayCommand(() => _ = InstallFfmpegUpdateAsync(), () => !IsRunning && !IsUpdating);
        SkipUpdateCommand = new RelayCommand(() => { _settings.SkippedFfmpegVersion = LatestFfmpeg; _settings.Save(); UpdateAvailable = false; });
        CheckUpdatesCommand = new RelayCommand(() => _ = CheckForUpdatesAsync(manual: true), () => !IsUpdating);
        ShowQueueCommand = new RelayCommand(() => _dialogs.ShowQueue());
        ShowProgressCommand = new RelayCommand(() => _dialogs.ShowProgress(), () => IsRunning);
        SettingsCommand = new RelayCommand(() => _dialogs.ShowSettings(), () => !IsRunning);
        ShowDevicesCommand = new RelayCommand(() => _dialogs.ShowDevices(new DevicesViewModel(_settings, OnTargetDeviceChanged)));
        AdaptToTvCommand = new RelayCommand(AdaptToTv, () => HasFile && HasPendingTvChanges && !IsRunning);
        DeviceProfile.WindowsMime = WindowsMimeFromRegistry;
        RebuildTargetOptions();
        InitProfiles();

        _preferredLanguage = Languages.All.FirstOrDefault(l => l.Code == Languages.Normalize(_settings.PreferredLanguage)) ?? Languages.All.First(l => l.Code == "spa");
        _dialogBoost = _settings.DialogBoost;
        _compression = _settings.Compression;
        _targetLufs = TargetLufsOptions.FirstOrDefault(o => Math.Abs(o.Value - _settings.TargetLufs) < 0.1) ?? TargetLufsOptions[1];
        _rememberOutputFolder = _settings.OutputFolder is not null;
        _openFolderWhenDone = _settings.OpenFolderWhenDone;
    }

    public async Task InitializeAsync(string? fileFromCommandLine)
    {
        if (!_tools.IsAvailable)
        {
            FfmpegStatus = "FFmpeg no encontrado";
            if (_dialogs.Confirm("No se ha encontrado FFmpeg en la carpeta del programa (subcarpeta \"ffmpeg\").\n\n¿Quieres descargarlo ahora? Son unos 114 MB desde gyan.dev (la compilación para Windows que enlaza ffmpeg.org).", "Falta FFmpeg"))
                await InstallFfmpegUpdateAsync();
        }
        await DetectHardwareAsync();
        if (fileFromCommandLine is not null && File.Exists(fileFromCommandLine)) await LoadAsync(fileFromCommandLine);
        if (_settings.CheckUpdatesOnStartup) _ = CheckForUpdatesAsync(manual: false);
    }

    // ------------------------------------------------------------------ comandos
    public RelayCommand OpenCommand { get; }
    public RelayCommand HelpCommand { get; }
    public RelayCommand PrevFrameCommand { get; }
    public RelayCommand NextFrameCommand { get; }
    public RelayCommand BrowseOutputCommand { get; }
    public RelayCommand AddConvertedTrackCommand { get; }
    public RelayCommand AddOriginalTrackCommand { get; }
    public RelayCommand AddExternalTrackCommand { get; }
    public RelayCommand BrowseBurnFileCommand { get; }
    public RelayCommand ReselectTracksCommand { get; }
    public RelayCommand EnqueueCommand { get; }
    public RelayCommand ConvertCommand { get; }
    public RelayCommand StartQueueCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand ClearFinishedCommand { get; }
    public RelayCommand CopyLogCommand { get; }
    public RelayCommand CopyCommandCommand { get; }
    public RelayCommand UpdateFfmpegCommand { get; }
    public RelayCommand SkipUpdateCommand { get; }
    public RelayCommand CheckUpdatesCommand { get; }
    public RelayCommand ShowQueueCommand { get; }
    public RelayCommand ShowProgressCommand { get; }
    public RelayCommand SettingsCommand { get; }
    public RelayCommand ShowDevicesCommand { get; }
    public RelayCommand ResetQualityCommand { get; }
    public RelayCommand AdaptToTvCommand { get; }

    // ------------------------------------------------------------------ estado general
    private MediaInfo? _info;
    private ConversionJob? _job;
    private bool _isLoading;
    private string _loadingText = "";
    private string _ffmpegStatus = "";

    public MediaInfo? Info { get => _info; private set { if (Set(ref _info, value)) OnPropertyChanged(nameof(HasFile)); } }
    public bool HasFile => _info is not null && _job is not null;
    public bool IsLoading { get => _isLoading; set => Set(ref _isLoading, value); }
    public string LoadingText { get => _loadingText; set => Set(ref _loadingText, value); }
    public string FfmpegStatus { get => _ffmpegStatus; set => Set(ref _ffmpegStatus, value); }
    public string SettingsFilePath => _settings.FilePath ?? "";
    public string WindowTitle => _info is null ? "Recodec" : $"{_info.FileName} — Recodec";

    // ------------------------------------------------------------------ carga de archivos
    public async Task OpenFilesAsync(IReadOnlyList<string> paths)
    {
        var videos = paths.Where(File.Exists).ToList();
        if (videos.Count == 0) return;
        if (videos.Count == 1) { await LoadAsync(videos[0]); return; }

        // Varios archivos: se añaden a la cola con los ajustes por defecto; el primero se abre para revisarlo.
        var added = 0;
        foreach (var p in videos)
        {
            try
            {
                var info = await _prober.ProbeAsync(p);
                if (info.Video is null && !info.AudioStreams.Any()) continue;
                var job = JobPlanner.CreateDefault(info, CurrentDefaults());
                job.OutputPath = UniqueOutputPath(job.OutputPath);
                AddToQueue(job);
                added++;
            }
            catch (Exception ex) { Log($"No se ha podido analizar {Path.GetFileName(p)}: {ex.Message}"); }
        }
        await LoadAsync(videos[0]);
        _dialogs.ShowQueue();
        _dialogs.Info($"Se han añadido {added} vídeos a la cola con tus ajustes por defecto.\nPuedes revisarlos en la ventana Cola y pulsar \"Iniciar cola\".", "Varios vídeos");
    }

    public async Task LoadAsync(string path)
    {
        _loadCts?.Cancel();
        var cts = _loadCts = new CancellationTokenSource();
        IsLoading = true;
        LoadingText = "Analizando " + Path.GetFileName(path) + "…";
        try
        {
            var info = await _prober.ProbeAsync(path, cts.Token);
            if (info.Video is null && !info.AudioStreams.Any())
            {
                _dialogs.Info("Este archivo no contiene vídeo ni audio que se pueda convertir.", "Archivo no válido");
                return;
            }
            _frameCache.Clear();
            PreviewImage = null;
            _job = null;
            _resolutionKey = (-1, -1);
            Info = info;
            ApplyDefaultsForCurrentFile();
            OnPropertyChanged(nameof(WindowTitle));
            BuildDetails();
            SelectedTab = TabVideo;
            _ = ShowFrameAsync(2);
            _ = DetectCropAsync(info, cts.Token);
            if (SubtitleAnalysis.NeedsCounting(info)) _ = CountSubtitlesAsync(info, cts.Token);
            else SubtitleCountText = "";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _dialogs.Info("No se ha podido abrir el archivo:\n" + ex.Message, "Error");
        }
        finally
        {
            if (_loadCts == cts) IsLoading = false;
        }
    }

    private JobPlanner.Defaults CurrentDefaults() => _settings.ToDefaults() with { Hardware = _hw };

    /// <summary>
    /// Prueba NVIDIA, Intel y AMD. RECODEC_SIN_GPU=1 simula un equipo sin gráfica que codifique vídeo.
    /// </summary>
    private async Task DetectHardwareAsync()
    {
        _hw = !_tools.IsAvailable || Environment.GetEnvironmentVariable("RECODEC_SIN_GPU") == "1" ? HardwareEncoders.None : await HardwareEncoders.DetectAsync(_tools);
        if (!_tools.IsAvailable) return;
        var vendors = _hw.Vendors.Select(HardwareEncoders.VendorName).ToList();
        FfmpegStatus = "FFmpeg " + (await _tools.GetVersionAsync() ?? "?") +
                       (vendors.Count > 0 ? $" · gráfica {string.Join(" y ", vendors)} disponible" : " · sin gráfica compatible (se usará el procesador)");
        Log("Codificadores por gráfica: " + (vendors.Count > 0 ? string.Join(", ", HardwareEncoders.Order.SelectMany(b =>
            ContainerRules.HardwareCodecs(b).Where(c => _hw.Supports(c, b)).Select(c => $"{JobPlanner.CodecLabel(c)} {HardwareEncoders.VendorName(b)}"))) : "ninguno"));
    }

    /// <summary>Crea la selección inicial (pistas en el idioma favorito, etc.) para el archivo abierto.</summary>
    private void ApplyDefaultsForCurrentFile()
    {
        if (_info is null) return;
        var crop = _job?.Video.Crop;
        var output = _job?.OutputPath;
        var job = JobPlanner.CreateDefault(_info, CurrentDefaults());
        job.Video.Crop = crop;
        if (!string.IsNullOrEmpty(output)) job.OutputPath = Path.ChangeExtension(output, ContainerRules.Extension(job.Container));
        _job = job;
        _lastTvChanges = new();
        _burnChosenAutomatically = job.Subtitles.Burns;
        SyncVolumeFromJob();
        OnPropertyChanged(nameof(HasFile));
        RebuildTrackLists();
        RefreshAll();
    }

    /// <summary>Cuenta las líneas de los subtítulos (para distinguir completos de forzados) sin bloquear la ventana.</summary>
    private async Task CountSubtitlesAsync(MediaInfo info, CancellationToken ct)
    {
        SubtitleCountText = "Contando las líneas de cada subtítulo para distinguir los completos de los forzados (lee el archivo entero, puede tardar un poco)…";
        try
        {
            await SubtitleAnalysis.CountAsync(_tools, info, ct);
            if (_info != info || _job is null) return;
            SubtitleCountText = "";
            RefineAutomaticBurn();
            RebuildSubtitles();
            Revalidate();
        }
        catch (OperationCanceledException) { }
        catch { if (_info == info) SubtitleCountText = "No se han podido contar las líneas de los subtítulos."; }
    }

    private async Task DetectCropAsync(MediaInfo info, CancellationToken ct)
    {
        CropText = "Buscando bandas negras…";
        try
        {
            var crop = await _preview.DetectCropAsync(info, ct);
            if (_info != info || _job is null) return;
            _job.Video.Crop = crop;
            CropText = crop is null ? "No hay bandas negras que recortar." : $"Se recortará a {crop.Width}×{crop.Height} (bandas negras detectadas en los 10 puntos de muestra).";
            Revalidate();
        }
        catch (OperationCanceledException) { }
        catch { CropText = "No se ha podido analizar el recorte."; }
    }

    // ------------------------------------------------------------------ vista previa
    private BitmapImage? _previewImage;
    private int _previewIndex;
    private string _previewLabel = "";
    private string _previewTimeText = "";
    private bool _previewLoading;

    public BitmapImage? PreviewImage { get => _previewImage; set => Set(ref _previewImage, value); }
    public int PreviewIndex { get => _previewIndex; set => Set(ref _previewIndex, value); }
    public string PreviewLabel { get => _previewLabel; set => Set(ref _previewLabel, value); }
    public string PreviewTimeText { get => _previewTimeText; set => Set(ref _previewTimeText, value); }
    public bool PreviewLoading { get => _previewLoading; set => Set(ref _previewLoading, value); }

    private async Task ShowFrameAsync(int index)
    {
        var info = _info;
        if (info is null) return;
        PreviewIndex = index;
        var t = PreviewService.PreviewTime(info, index);
        PreviewLabel = $"Preview {index + 1} de {PreviewService.PreviewCount}";
        PreviewTimeText = $"Momento de la película: {FormatTime(t)}";
        if (_frameCache.TryGetValue(index, out var cached)) { PreviewImage = cached; return; }
        PreviewLoading = true;
        try
        {
            var bytes = await _preview.GetFrameAsync(info, t, 640);
            if (_info != info) return;
            BitmapImage? img = null;
            if (bytes is not null)
            {
                img = new BitmapImage();
                img.BeginInit();
                img.CacheOption = BitmapCacheOption.OnLoad;
                img.StreamSource = new MemoryStream(bytes);
                img.EndInit();
                img.Freeze();
            }
            _frameCache[index] = img;
            if (PreviewIndex == index) PreviewImage = img;
        }
        finally { PreviewLoading = false; }
    }

    // ------------------------------------------------------------------ resumen del archivo
    public ObservableCollection<string> Chips { get; } = new();
    public ObservableCollection<KeyValuePair<string, string>> Details { get; } = new();

    private void BuildDetails()
    {
        Chips.Clear();
        Details.Clear();
        var i = _info!;
        var v = i.Video;
        Chips.Add(ContainerFromFormat(i));
        Chips.Add(FormatSize(i.SizeBytes));
        if (i.Duration > TimeSpan.Zero) Chips.Add(FormatDuration(i.Duration));
        if (v is not null)
        {
            Chips.Add($"{v.CodecDisplay} {v.BitDepth}-bit");
            Chips.Add($"{v.DisplayWidth}×{v.DisplayHeight}");
            if (v.DisplayHeight > v.DisplayWidth) Chips.Add("Vertical");
            if (v.Hdr != HdrKind.None) Chips.Add(v.Hdr switch { HdrKind.Hdr10 => "HDR10", HdrKind.Hlg => "HLG", _ => $"Dolby Vision {v.DolbyVisionProfile}" });
            if (v.FrameRate > 0) Chips.Add(v.FrameRate.ToString("0.###", Es) + " fps");
            if (v.IsInterlaced) Chips.Add("Entrelazado");
        }
        var a = i.AudioStreams.Count();
        var s = i.SubtitleStreams.Count();
        Chips.Add(a == 1 ? "1 audio" : $"{a} audios");
        Chips.Add(s == 1 ? "1 subtítulo" : $"{s} subtítulos");

        Details.Add(new("Archivo", i.Path));
        Details.Add(new("Formato", $"{i.FormatLongName} ({i.FormatName})"));
        Details.Add(new("Tamaño", $"{FormatSize(i.SizeBytes)} · {i.BitRate / 1000:N0} kb/s en total"));
        Details.Add(new("Duración", FormatDuration(i.Duration) + (i.ChapterCount > 0 ? $" · {i.ChapterCount} capítulos" : "")));
        foreach (var st in i.Streams)
        {
            var key = st.Kind switch { StreamKind.Video => "Vídeo", StreamKind.Audio => "Audio", StreamKind.Subtitle => "Subtítulo", StreamKind.Attachment => "Adjunto", _ => "Datos" } + $" #{st.Index}";
            var text = st.Kind switch
            {
                StreamKind.Video => $"{st.CodecDisplay} {st.Profile} · {st.DisplayWidth}×{st.DisplayHeight}{(st.Rotation != 0 ? $" (girado {st.Rotation}°)" : "")} · {st.BitDepth} bits · {st.FrameRate.ToString("0.###", Es)} fps · color {st.ColorPrimaries ?? "?"}/{st.ColorTransfer ?? "?"}" +
                                    (st.BitRate > 0 ? $" · {st.BitRate / 1000:N0} kb/s" : "") + (st.AttachedPic ? " · carátula" : ""),
                StreamKind.Audio => $"{st.Describe()} · {st.ChannelLayout} · {st.SampleRate / 1000.0:0.#} kHz" + (st.BitRate > 0 ? $" · {st.BitRate / 1000:N0} kb/s" : "") + (st.IsDefault ? " · por defecto" : ""),
                StreamKind.Subtitle => st.Describe() + (st.IsDefault ? " · por defecto" : ""),
                _ => st.CodecLongName,
            };
            Details.Add(new(key, text));
        }
        if (i.HdrMetadata is { } md) Details.Add(new("HDR", $"Masterizado {md.MasterDisplay} · MaxCLL {md.MaxCll}"));
    }

    // ------------------------------------------------------------------ pestañas
    public const int TabVideo = 0, TabAudio = 1, TabSubtitles = 2, TabOutput = 3, TabLog = 4;
    private int _selectedTab;
    public int SelectedTab { get => _selectedTab; set => Set(ref _selectedTab, value); }

    // ------------------------------------------------------------------ utilidades de formato
    public static string FormatSize(long bytes) =>
        bytes >= 1L << 30 ? (bytes / (double)(1L << 30)).ToString("0.0", Es) + " GB" : (bytes / (double)(1L << 20)).ToString("0", Es) + " MB";

    public static string FormatDuration(TimeSpan t) => t.TotalHours >= 1 ? $"{(int)t.TotalHours} h {t.Minutes:00} min" : $"{t.Minutes} min {t.Seconds:00} s";
    public static string FormatTime(TimeSpan t) => $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}";

    private static string ContainerFromFormat(MediaInfo i)
    {
        var ext = Path.GetExtension(i.Path).TrimStart('.').ToUpperInvariant();
        return string.IsNullOrEmpty(ext) ? i.FormatName.Split(',')[0].ToUpperInvariant() : ext;
    }

    // ------------------------------------------------------------------ registro
    private readonly StringBuilder _log = new();
    private string _logText = "";
    public string LogText { get => _logText; private set => Set(ref _logText, value); }

    public void Log(string line)
    {
        void Append()
        {
            _log.AppendLine($"[{DateTime.Now:HH:mm:ss}] {line}");
            if (_log.Length > 400_000) _log.Remove(0, _log.Length - 300_000);
            LogText = _log.ToString();
        }
        if (Application.Current?.Dispatcher.CheckAccess() == false) Application.Current.Dispatcher.BeginInvoke(Append);
        else Append();
    }

    private static void OpenInExplorer(string file)
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{file}\"") { UseShellExecute = true }); } catch { }
    }
}
