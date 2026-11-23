using System.Collections.ObjectModel;
using Recodec.Core.Conversion;
using Recodec.Core.Media;
using Recodec.Infrastructure;

namespace Recodec.ViewModels;

/// <summary>Una pista de audio de salida (convertida, original o de un archivo externo).</summary>
public sealed class AudioTrackViewModel : ObservableObject
{
    private readonly Action _changed;
    public AudioTrackJob Model { get; }
    public string SourceDescription { get; }
    public RelayCommand RemoveCommand { get; }
    public RelayCommand MoveUpCommand { get; }
    public RelayCommand MoveDownCommand { get; }
    public RelayCommand SyncCommand { get; }

    public AudioTrackViewModel(AudioTrackJob model, string sourceDescription, int position, int count, Action changed,
                               Action<AudioTrackViewModel> remove, Action<AudioTrackViewModel, int> move, Action<AudioTrackViewModel> sync)
    {
        Model = model;
        SourceDescription = sourceDescription;
        Position = position;
        Count = count;
        _changed = changed;
        RemoveCommand = new RelayCommand(() => remove(this));
        MoveUpCommand = new RelayCommand(() => move(this, -1), () => Position > 0);
        MoveDownCommand = new RelayCommand(() => move(this, +1), () => Position < Count - 1);
        SyncCommand = new RelayCommand(() => sync(this));
        CodecOptions = new ObservableCollection<Option<AudioCodecChoice>>();
    }

    /// <summary>Posición de la pista en el vídeo convertido (0 = la que suena por defecto).</summary>
    public int Position { get; }
    public int Count { get; }
    public bool CanReorder => Count > 1;
    public string OrderLabel => Count < 2 ? "" : Position == 0 ? "Pista 1 · suena por defecto" : $"Pista {Position + 1}";
    public string OrderToolTip => "Orden de las pistas en el vídeo convertido. La primera es la que suena al empezar; las demás se eligen desde el mando de la tele (botón de audio o idioma).";

    public ObservableCollection<Option<AudioCodecChoice>> CodecOptions { get; }
    public static IReadOnlyList<LanguageOption> LanguageOptions => Languages.All;
    public static IReadOnlyList<int> BitrateOptions { get; } = new[] { 96, 128, 160, 192, 224, 256, 320, 384, 448, 640 };
    public static IReadOnlyList<Option<VolumeMode>> VolumeOptions { get; } = new[]
    {
        new Option<VolumeMode>(VolumeMode.Night, "Modo noche",
            "Modo noche: realza las voces, suaviza las explosiones y deja la película a un volumen medio fijo, con los tres ajustes de \"Corrección del volumen\" (más abajo). Es la opción para no tener que subir y bajar la tele."),
        new Option<VolumeMode>(VolumeMode.Soft, "Suave",
            "Suave: solo iguala el volumen medio con una compresión ligera y fija. Las explosiones conservan casi toda su fuerza. No usa los ajustes de \"Corrección del volumen\"."),
        new Option<VolumeMode>(VolumeMode.Off, "Sin cambios",
            "Sin cambios: el audio se convierte al formato elegido sin tocar el volumen; suena igual que el original."),
    };

    /// <summary>Explicación del modo de volumen elegido para esta pista.</summary>
    public string VolumeModeText => !IsConverted
        ? "Se copia tal cual: no se cambia el formato ni el volumen (útil para una barra de sonido o un equipo 5.1)."
        : SelectedVolume?.Hint ?? "";

    public bool IsExternal => Model.IsExternal;
    public string ExternalFileName => Model.ExternalPath is null ? "" : System.IO.Path.GetFileName(Model.ExternalPath);
    public bool IsConverted => Model.Codec != AudioCodecChoice.Copy;
    public bool HasBitrate => ContainerRules.UsesBitrate(Model.Codec);

    public void RefreshCodecOptions(ContainerKind container)
    {
        CodecOptions.Clear();
        foreach (var c in ContainerRules.AllowedAudioCodecs(container))
            CodecOptions.Add(new Option<AudioCodecChoice>(c, ContainerRules.AudioCodecName(c)));
        OnPropertyChanged(nameof(SelectedCodec));
    }

    public LanguageOption? Language
    {
        get => Languages.All.FirstOrDefault(l => l.Code == Languages.Normalize(Model.Language));
        set { if (value is not null) { Model.Language = value.Code; OnPropertyChanged(); _changed(); } }
    }

    public Option<AudioCodecChoice>? SelectedCodec
    {
        get => CodecOptions.FirstOrDefault(o => o.Value == Model.Codec);
        set
        {
            if (value is null || value.Value == Model.Codec) return;
            Model.Codec = value.Value;
            Model.BitrateKbps = ContainerRules.DefaultBitrate(value.Value);
            if (value.Value == AudioCodecChoice.Copy) Model.Volume = VolumeMode.Off;
            Refresh();
            _changed();
        }
    }

    public int Bitrate
    {
        get => Model.BitrateKbps;
        set { Model.BitrateKbps = value; OnPropertyChanged(); _changed(); }
    }

    public Option<VolumeMode>? SelectedVolume
    {
        get => VolumeOptions.FirstOrDefault(o => o.Value == Model.Volume);
        set { if (value is not null) { Model.Volume = value.Value; OnPropertyChanged(); OnPropertyChanged(nameof(VolumeModeText)); _changed(); } }
    }

    public int DelayMs
    {
        get => Model.DelayMs;
        set { Model.DelayMs = value; OnPropertyChanged(); _changed(); }
    }

    public void Refresh()
    {
        OnPropertyChanged(nameof(SelectedCodec));
        OnPropertyChanged(nameof(Bitrate));
        OnPropertyChanged(nameof(SelectedVolume));
        OnPropertyChanged(nameof(IsConverted));
        OnPropertyChanged(nameof(HasBitrate));
        OnPropertyChanged(nameof(Language));
        OnPropertyChanged(nameof(VolumeModeText));
        OnPropertyChanged(nameof(DelayMs));
    }
}

/// <summary>Un subtítulo del archivo original.</summary>
public sealed class SubtitleViewModel : ObservableObject
{
    private readonly Action<SubtitleViewModel, bool> _toggled;
    private bool _include;
    private string _status = "";
    private bool _canInclude = true;

    public StreamInfo Stream { get; }
    public string Description { get; }

    public SubtitleViewModel(StreamInfo stream, bool include, Action<SubtitleViewModel, bool> toggled)
    {
        Stream = stream;
        Description = $"#{stream.Index} · {stream.Describe()}";
        _include = include;
        _toggled = toggled;
    }

    public bool Include
    {
        get => _include;
        set { if (Set(ref _include, value)) _toggled(this, value); }
    }

    public void SetIncludeSilently(bool v) { _include = v; OnPropertyChanged(nameof(Include)); }

    public string Status { get => _status; set => Set(ref _status, value); }
    public bool CanInclude { get => _canInclude; set => Set(ref _canInclude, value); }
}

public enum QueueState { Pending, Running, Done, Failed, Cancelled }

public sealed class QueueItemViewModel : ObservableObject
{
    private QueueState _state = QueueState.Pending;
    private double _progress;
    private string _detail = "En espera";

    public ConversionJob Job { get; }
    public string FileName => Job.Source.FileName;
    public string OutputPath => Job.OutputPath;
    public string Summary { get; }
    public RelayCommand RemoveCommand { get; }
    public RelayCommand OpenFolderCommand { get; }

    public QueueItemViewModel(ConversionJob job, string summary, Action<QueueItemViewModel> remove, Action<QueueItemViewModel> openFolder)
    {
        Job = job;
        Summary = summary;
        RemoveCommand = new RelayCommand(() => remove(this), () => State != QueueState.Running);
        OpenFolderCommand = new RelayCommand(() => openFolder(this), () => State == QueueState.Done);
    }

    public QueueState State
    {
        get => _state;
        set { if (Set(ref _state, value)) { OnPropertyChanged(nameof(StateLabel)); OnPropertyChanged(nameof(IsActive)); } }
    }

    public bool IsActive => State == QueueState.Running;

    public string StateLabel => State switch
    {
        QueueState.Pending => "En espera",
        QueueState.Running => "Convirtiendo",
        QueueState.Done => "Terminado",
        QueueState.Failed => "Error",
        QueueState.Cancelled => "Cancelado",
        _ => "",
    };

    public double Progress { get => _progress; set => Set(ref _progress, value); }
    public string Detail { get => _detail; set => Set(ref _detail, value); }
}
