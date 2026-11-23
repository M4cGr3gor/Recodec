using System.Collections.ObjectModel;
using System.Globalization;
using Recodec.Core.Conversion;
using Recodec.Core.Devices;
using Recodec.Core.Media;
using Recodec.Core.Settings;
using Recodec.Infrastructure;

namespace Recodec.ViewModels;

/// <summary>Ventana "Teles en la red": busca reproductores DLNA y muestra qué formatos aceptan.</summary>
public sealed class DevicesViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly Action _targetChanged;
    private CancellationTokenSource? _cts;

    public DevicesViewModel(AppSettings settings, Action targetChanged)
    {
        _settings = settings;
        _targetChanged = targetChanged;
        ScanCommand = new RelayCommand(() => _ = ScanAsync(), () => !IsScanning);
        Rebuild();
        Status = Devices.Count == 0
            ? "Enciende la tele y pulsa \"Buscar en la red\". El ordenador y la tele deben estar conectados a la misma red (wifi o cable)."
            : "Estas son las teles encontradas la última vez. Pulsa \"Buscar en la red\" para actualizar la lista.";
    }

    public ObservableCollection<DeviceCardViewModel> Devices { get; } = new();
    public RelayCommand ScanCommand { get; }
    public bool HasDevices => Devices.Count > 0;

    private bool _isScanning;
    public bool IsScanning { get => _isScanning; private set { if (Set(ref _isScanning, value)) System.Windows.Input.CommandManager.InvalidateRequerySuggested(); } }

    private string _status = "";
    public string Status { get => _status; private set => Set(ref _status, value); }

    private readonly List<string> _log = new();
    public string LogText => string.Join(Environment.NewLine, _log);

    private async Task ScanAsync()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        IsScanning = true;
        _log.Clear();
        Status = "Buscando teles y reproductores en la red (unos 4 segundos)…";
        try
        {
            var found = await DlnaScanner.ScanAsync(TimeSpan.FromSeconds(4), line => { lock (_log) _log.Add(line); }, _cts.Token);
            foreach (var d in found)
            {
                var i = _settings.Devices.FindIndex(x => x.Id == d.Id);
                if (i >= 0) _settings.Devices[i] = d; else _settings.Devices.Add(d);
            }
            _settings.Save();
            Rebuild();
            Status = found.Count switch
            {
                0 => "No se ha encontrado ninguna tele. Comprueba que está encendida, en la misma red que el ordenador y con la opción de compartir contenido (DLNA) activada.",
                1 => $"Encontrada: {found[0].DisplayName}. Pulsa \"Elegir esta tele\" para comprobar los vídeos con ella.",
                _ => $"Encontrados {found.Count} reproductores. Elige para cuál quieres preparar los vídeos.",
            };
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Status = "No se ha podido buscar: " + ex.Message; }
        finally
        {
            IsScanning = false;
            OnPropertyChanged(nameof(LogText));
        }
    }

    private void Rebuild()
    {
        Devices.Clear();
        foreach (var d in _settings.Devices.OrderBy(d => d.DisplayName))
            Devices.Add(new DeviceCardViewModel(d, d.Id == _settings.TargetDeviceId, Use, Forget));
        OnPropertyChanged(nameof(HasDevices));
    }

    private void Use(DeviceCardViewModel card)
    {
        _settings.TargetDeviceId = card.Profile.Id;
        _settings.Save();
        foreach (var d in Devices) d.IsTarget = d == card;
        Status = $"Elegida: {card.Profile.DisplayName}. Recodec comprobará los vídeos con ella; los cambios se aplican desde la pestaña Salida.";
        _targetChanged();
    }

    private void Forget(DeviceCardViewModel card)
    {
        _settings.Devices.RemoveAll(d => d.Id == card.Profile.Id);
        if (_settings.TargetDeviceId == card.Profile.Id) { _settings.TargetDeviceId = DeviceProfile.GenericId; _targetChanged(); }
        _settings.Save();
        Rebuild();
    }

    public void Cancel() => _cts?.Cancel();
}

public sealed record SupportRow(string Name, CheckLevel Level, string Text)
{
    public string Icon => Level switch { CheckLevel.Ok => "", CheckLevel.Warning => "", _ => "" };
    public bool IsOk => Level == CheckLevel.Ok;
    public bool IsWarning => Level == CheckLevel.Warning;
    public bool IsProblem => Level == CheckLevel.Problem;
}

public sealed class DeviceCardViewModel : ObservableObject
{
    private static readonly CultureInfo Es = new("es-ES");
    private bool _isTarget;

    public DeviceCardViewModel(DeviceProfile p, bool isTarget, Action<DeviceCardViewModel> use, Action<DeviceCardViewModel> forget)
    {
        Profile = p;
        _isTarget = isTarget;
        UseCommand = new RelayCommand(() => use(this));
        ForgetCommand = new RelayCommand(() => forget(this));

        var details = new List<string>();
        if (!string.IsNullOrWhiteSpace(p.Manufacturer)) details.Add(p.Manufacturer!);
        if (!string.IsNullOrWhiteSpace(p.Model)) details.Add("modelo " + p.Model);
        if (!string.IsNullOrWhiteSpace(p.Address)) details.Add("IP " + p.Address);
        if (p.ScannedAt is { } at) details.Add("leída el " + at.ToString("d 'de' MMMM, HH:mm", Es));
        Subtitle = string.Join(" · ", details);

        foreach (var c in DeviceProfile.KnownContainers)
        {
            var name = ContainerRules.DisplayName(c);
            if (p.AcceptsFromWindows(c)) Containers.Add(new(name, CheckLevel.Ok, ""));
            else if (p.PlaysContainer(c))
                Containers.Add(new(name, CheckLevel.Warning,
                    $"La tele lo reproduce, pero lo anuncia como \"{DeviceProfile.ContainerMimes(c).First(p.Mimes.Contains)}\" y Windows lo llama \"{DeviceProfile.WindowsMime(c)}\": \"Reproducir en\" no lo enviará. Desde un USB o un servidor DLNA sí funciona."));
            else Containers.Add(new(name, CheckLevel.Problem, "No lo anuncia"));
        }
        foreach (var v in DeviceProfile.KnownVideo)
            VideoCodecs.Add(new(CodecNames.Display(v), p.SupportsVideo(v) ? CheckLevel.Ok : CheckLevel.Problem, p.SupportsVideo(v) ? "" : "No lo anuncia"));
        foreach (var a in DeviceProfile.KnownAudio)
            AudioCodecs.Add(new(CodecNames.Display(a), p.SupportsAudio(a) ? CheckLevel.Ok : CheckLevel.Problem, p.SupportsAudio(a) ? "" : "No lo anuncia"));

        Recommendation = BuildRecommendation(p);
    }

    public DeviceProfile Profile { get; }
    public string Title => Profile.DisplayName;
    public string Subtitle { get; }
    public List<SupportRow> Containers { get; } = new();
    public List<SupportRow> VideoCodecs { get; } = new();
    public List<SupportRow> AudioCodecs { get; } = new();
    public string Recommendation { get; }
    public RelayCommand UseCommand { get; }
    public RelayCommand ForgetCommand { get; }

    public bool IsTarget { get => _isTarget; set { if (Set(ref _isTarget, value)) OnPropertyChanged(nameof(IsNotTarget)); } }
    public bool IsNotTarget => !IsTarget;

    private static string BuildRecommendation(DeviceProfile p)
    {
        var container = new[] { ContainerKind.Mp4, ContainerKind.Mkv, ContainerKind.Mov }.FirstOrDefault(p.AcceptsFromWindows, ContainerKind.Mp4);
        var videos = new[] { "hevc", "h264" }.Where(p.SupportsVideo).Select(v => CodecNames.Display(v)).ToList();
        var parts = new List<string>
        {
            $"Contenedor {ContainerRules.DisplayName(container)}",
            videos.Count == 0 ? "vídeo H.264" : $"vídeo {string.Join(" o ", videos)} (si el original ya es así, se copia sin recodificar)",
            p.SupportsAudio("aac") ? "audio AAC estéreo con el modo noche" : p.SupportsAudio("ac3") ? "audio AC3" : "audio MP3",
        };
        if (p.SupportsAudio("ac3")) parts.Add("y, si tienes barra de sonido, una segunda pista AC3 5.1");
        var text = "Recomendado: " + string.Join(", ", parts) + ".";
        var avoid = new List<string>();
        if (p.PlaysContainer(ContainerKind.Mkv) && !p.AcceptsFromWindows(ContainerKind.Mkv)) avoid.Add("MKV (Windows no lo envía a esta tele)");
        else if (!p.PlaysContainer(ContainerKind.Mkv)) avoid.Add("MKV");
        if (!p.SupportsAudio("dts")) avoid.Add("audio DTS");
        if (!p.SupportsAudio("truehd")) avoid.Add("Dolby TrueHD");
        if (!p.SupportsVideo("av1")) avoid.Add("vídeo AV1");
        if (avoid.Count > 0) text += " Evita: " + string.Join(", ", avoid) + ". Si eliges esta tele, Recodec te avisa y puedes aplicar los cambios con un botón.";
        return text;
    }
}
