using System.Collections.ObjectModel;
using System.IO;
using Recodec.Core.Conversion;
using Recodec.Core.Devices;
using Recodec.Core.Ffmpeg;
using Recodec.Core.Media;
using Recodec.Infrastructure;

namespace Recodec.ViewModels;

public sealed partial class MainViewModel
{
    // ================================================================== Vídeo
    public ObservableCollection<Option<VideoCodec>> VideoCodecOptions { get; } = new();
    public ObservableCollection<Option<EncoderBackend>> BackendOptions { get; } = new();

    private static readonly (int Value, string Name)[] Resolutions =
    {
        (2160, "4K (2160p)"), (1440, "1440p (2K)"), (1080, "1080p (Full HD)"), (720, "720p (HD)"),
        (576, "576p (DVD europeo)"), (480, "480p (SD)"), (360, "360p (móvil)"),
    };

    /// <summary>Solo las resoluciones que reducen este vídeo, con el tamaño exacto que saldrá.</summary>
    public ObservableCollection<Option<int>> ResolutionOptions { get; } = new();
    private (int, int) _resolutionKey = (-1, -1);

    private void RebuildResolutionOptions()
    {
        if (_job is null) return;
        var (w, h) = JobPlanner.CroppedSize(_job);
        if (_resolutionKey == (w, h)) return;
        _resolutionKey = (w, h);
        ResolutionOptions.Clear();
        ResolutionOptions.Add(new Option<int>(0, w > 0 ? $"Igual que el original · {w}×{h}" : "Igual que el original"));
        foreach (var (value, name) in Resolutions)
        {
            var (rw, rh) = JobPlanner.FitResolution(w, h, value);
            if (rw < w || rh < h) ResolutionOptions.Add(new Option<int>(value, $"{name} · {rw}×{rh}"));
        }
        OnPropertyChanged(nameof(SelectedResolution));
    }

    public static IReadOnlyList<Option<DeinterlaceMode>> DeinterlaceOptions { get; } = new[]
    {
        new Option<DeinterlaceMode>(DeinterlaceMode.Auto, "Automático (solo si el vídeo está entrelazado)"),
        new Option<DeinterlaceMode>(DeinterlaceMode.On, "Siempre"),
        new Option<DeinterlaceMode>(DeinterlaceMode.Off, "Nunca"),
    };

    public static IReadOnlyList<Option<DenoiseMode>> DenoiseOptions { get; } = new[]
    {
        new Option<DenoiseMode>(DenoiseMode.Off, "Ninguna"),
        new Option<DenoiseMode>(DenoiseMode.Light, "Ligera · ruido suave (cámaras digitales, series recientes)"),
        new Option<DenoiseMode>(DenoiseMode.Medium, "Media · películas antiguas o TV grabada"),
        new Option<DenoiseMode>(DenoiseMode.Strong, "Fuerte · mucho ruido o grano (pierde algo de detalle)"),
        new Option<DenoiseMode>(DenoiseMode.Max, "Máxima · NLMeans, muy lenta"),
    };

    private static readonly string[] SpeedNames = { "Muy rápido", "Rápido", "Equilibrado", "Lento", "Muy lento" };

    public bool IsCopyMode
    {
        get => _job?.Video.Mode == VideoMode.Copy;
        set { if (_job is not null && value && CanCopy) { _job.Video.Mode = VideoMode.Copy; Revalidate(); } }
    }

    public bool IsEncodeMode
    {
        get => _job?.Video.Mode == VideoMode.Encode;
        set { if (_job is not null && value) { _job.Video.Mode = VideoMode.Encode; Revalidate(); } }
    }

    public bool CanCopy => _job is not null && _info?.Video is { } v && JobPlanner.ForcedEncodeReasons(_job, v).Count == 0;
    public string CopyBlockedText => _job is not null && _info?.Video is { } v ? string.Join(" ", JobPlanner.ForcedEncodeReasons(_job, v)) : "";

    public string VideoModeText => _job is null || _info?.Video is null ? "" : IsCopyMode
        ? "El vídeo se copia tal cual: sin pérdida de calidad y en pocos minutos. Recorte, resolución y desentrelazado solo se aplican al recodificar."
        : SelectedBackend?.Value is { } b && b != EncoderBackend.Cpu
            ? $"Se recodifica el vídeo completo con la gráfica {HardwareEncoders.VendorName(b)}: tardará bastante menos que la duración de la película."
            : "Se recodifica el vídeo completo con el procesador: tardará un buen rato (depende de la velocidad elegida).";

    public Option<VideoCodec>? SelectedVideoCodec
    {
        get => VideoCodecOptions.FirstOrDefault(o => o.Value == _job?.Video.Codec);
        set
        {
            if (_job is null || value is null || value.Value == _job.Video.Codec) return;
            _job.Video.Codec = value.Value;
            // Se recupera el codificador preferido (o la gráfica que admita este códec, o el procesador).
            _job.Video.Backend = _hw.Resolve(value.Value, _settings.Backend);
            _job.Video.Quality = JobPlanner.RecommendedQuality(_job.Video.Codec, _job.Video.Backend);
            _settings.VideoCodec = value.Value; _settings.Save();
            RebuildBackendOptions();
            Revalidate();
        }
    }

    public Option<EncoderBackend>? SelectedBackend
    {
        get => BackendOptions.FirstOrDefault(o => o.Value == _job?.Video.Backend);
        set
        {
            if (_job is null || value is null || value.Value == _job.Video.Backend) return;
            _job.Video.Backend = value.Value;
            _job.Video.Quality = JobPlanner.RecommendedQuality(_job.Video.Codec, _job.Video.Backend);
            _settings.Backend = value.Value; _settings.Save();
            Revalidate();
        }
    }

    public int Quality
    {
        get => _job?.Video.Quality ?? 22;
        // Mientras se refresca, el control deslizante puede "recortar" el valor al cambiar su rango: se ignora.
        set { if (_job is null || _refreshing) return; _job.Video.Quality = Math.Clamp(value, QualityMin, QualityMax); Revalidate(); }
    }

    public int QualityMin => JobPlanner.QualityRange(_job?.Video.Codec ?? VideoCodec.Hevc).Min;
    public int QualityMax => JobPlanner.QualityRange(_job?.Video.Codec ?? VideoCodec.Hevc).Max;

    public string QualityHint
    {
        get
        {
            if (_job is null) return "";
            var rec = JobPlanner.RecommendedQuality(_job.Video.Codec, _job.Video.Backend);
            var d = _job.Video.Quality - rec;
            var text = d switch
            {
                <= -5 => "Calidad máxima: archivos muy grandes",
                <= -2 => "Calidad muy alta: archivos grandes",
                <= 1 => "Equilibrada (recomendada)",
                <= 4 => "Buena: archivos más pequeños",
                _ => "Calidad baja: se notará en pantallas grandes",
            };
            return $"{text} · recomendado {rec}";
        }
    }

    public int Speed
    {
        get => _job?.Video.Speed ?? 2;
        set { if (_job is null) return; _job.Video.Speed = value; _settings.Speed = value; _settings.Save(); Revalidate(); }
    }

    public string SpeedText => SpeedNames[Math.Clamp(Speed, 0, 4)] + (Speed >= 3 ? " · mejor compresión" : Speed <= 1 ? " · algo menos de compresión" : "");

    public Option<int>? SelectedResolution
    {
        // Un máximo que no reduce este vídeo (p. ej. 720p de un perfil con un vídeo de 480p) equivale a "igual que el original".
        get => ResolutionOptions.FirstOrDefault(o => o.Value == (_job?.Video.MaxHeight ?? 0)) ?? ResolutionOptions.FirstOrDefault();
        set { if (_job is null || value is null || _refreshing) return; _job.Video.MaxHeight = value.Value; Revalidate(); }
    }

    public bool AutoCrop
    {
        get => _job?.Video.AutoCrop ?? true;
        set { if (_job is null) return; _job.Video.AutoCrop = value; Revalidate(); }
    }

    private string _cropText = "";
    public string CropText { get => _cropText; set => Set(ref _cropText, value); }

    public Option<DeinterlaceMode>? SelectedDeinterlace
    {
        get => DeinterlaceOptions.FirstOrDefault(o => o.Value == (_job?.Video.Deinterlace ?? DeinterlaceMode.Auto));
        set { if (_job is null || value is null) return; _job.Video.Deinterlace = value.Value; Revalidate(); }
    }

    public Option<DenoiseMode>? SelectedDenoise
    {
        get => DenoiseOptions.FirstOrDefault(o => o.Value == (_job?.Video.Denoise ?? DenoiseMode.Off));
        set { if (_job is null || value is null) return; _job.Video.Denoise = value.Value; Revalidate(); }
    }

    public string DenoiseText => (_job?.Video.Denoise ?? DenoiseMode.Off) switch
    {
        DenoiseMode.Off => "Quitar el ruido o el grano antes de comprimir hace que se vea más limpio y que el archivo ocupe menos (el ruido es lo que más cuesta comprimir).",
        DenoiseMode.Max when _info?.Video?.BitDepth > 8 => "Este vídeo es de 10 bits y NLMeans solo trabaja en 8: se usa la versión rápida con la máxima intensidad para no perder los 10 bits.",
        DenoiseMode.Max => "NLMeans quita el grano mejor que nada, pero es muy lento: unos 7 fotogramas por segundo en 1080p (una película, varias horas).",
        DenoiseMode.Strong => "Elimina mucho ruido, pero puede dejar las caras y las texturas algo planas. Pruébalo primero con un vídeo corto.",
        _ => "Apenas se nota en el detalle y el archivo ocupa algo menos. No ralentiza la conversión.",
    };

    public string HdrText
    {
        get
        {
            var v = _info?.Video;
            if (_job is null || v is null || v.Hdr == HdrKind.None) return "";
            if (IsCopyMode) return "El vídeo es HDR y se copia: el HDR se conserva intacto.";
            if (ContainerRules.IsSdrOnly(_job.Video.Codec)) return $"{JobPlanner.CodecLabel(_job.Video.Codec)} no admite HDR: se convertirá a SDR ajustando los tonos para que no se vea apagado. Para conservar el HDR elige HEVC, AV1 o VP9.";
            return v.Hdr == HdrKind.DolbyVision
                ? "Dolby Vision no se puede conservar al recodificar: se mantiene como HDR10 (10 bits)."
                : "El HDR se conserva: 10 bits, colores BT.2020 y metadatos de brillo.";
        }
    }

    // ================================================================== Audio
    private LanguageOption _preferredLanguage;
    public static IReadOnlyList<LanguageOption> LanguageOptions => Languages.All;
    public ObservableCollection<AudioTrackViewModel> AudioTracks { get; } = new();
    public ObservableCollection<Option<StreamInfo>> SourceAudioOptions { get; } = new();
    private Option<StreamInfo>? _selectedSourceAudio;
    public Option<StreamInfo>? SelectedSourceAudio { get => _selectedSourceAudio; set => Set(ref _selectedSourceAudio, value); }

    public LanguageOption PreferredLanguage
    {
        get => _preferredLanguage;
        set
        {
            if (value is null || value == _preferredLanguage) return;
            _preferredLanguage = value;
            _settings.PreferredLanguage = value.Code;
            _settings.Save();
            OnPropertyChanged();
            ApplyDefaultsForCurrentFile();
        }
    }

    private void AddSourceTrack(bool original)
    {
        if (_job is null || SelectedSourceAudio is null) return;
        var s = SelectedSourceAudio.Value;
        var codec = original ? AudioCodecChoice.Copy : (_job.Container == ContainerKind.WebM ? AudioCodecChoice.OpusStereo : _settings.AudioCodec);
        _job.AudioTracks.Add(JobPlanner.TrackFromStream(s, codec, original ? VolumeMode.Off : _settings.VolumeMode));
        RebuildAudioTracks();
        Revalidate();
    }

    private async Task AddExternalTrackAsync()
    {
        if (_job is null) return;
        var path = _dialogs.OpenFile("Añadir pista de audio desde archivo",
            "Audio|*.ac3;*.eac3;*.ec3;*.dts;*.aac;*.m4a;*.mka;*.mp3;*.flac;*.wav;*.opus;*.ogg;*.thd;*.mkv;*.mp4|Todos los archivos|*.*");
        if (path is null) return;
        try
        {
            var info = await _prober.ProbeAsync(path);
            var a = info.AudioStreams.FirstOrDefault();
            if (a is null) { _dialogs.Info("Ese archivo no contiene audio.", "Pista externa"); return; }
            var lang = a.Language is null || Languages.Normalize(a.Language) == "und" ? _preferredLanguage.Code : Languages.Normalize(a.Language);
            var codec = _job.Container == ContainerKind.WebM ? AudioCodecChoice.OpusStereo : _settings.AudioCodec;
            _job.AudioTracks.Add(new AudioTrackJob
            {
                ExternalPath = path,
                ExternalStreamIndex = 0,
                SourceChannels = a.Channels,
                SourceChannelLayout = a.ChannelLayout,
                SourceCodec = a.Codec,
                Language = lang,
                Codec = codec,
                BitrateKbps = ContainerRules.DefaultBitrate(codec),
                Volume = _settings.VolumeMode,
            });
            RebuildAudioTracks();
            Revalidate();
        }
        catch (Exception ex) { _dialogs.Info("No se ha podido leer el archivo de audio:\n" + ex.Message, "Pista externa"); }
    }

    private void RemoveTrack(AudioTrackViewModel t)
    {
        _job?.AudioTracks.Remove(t.Model);
        RebuildAudioTracks();
        Revalidate();
    }

    private void MoveTrack(AudioTrackViewModel t, int direction)
    {
        if (_job is null) return;
        var i = _job.AudioTracks.IndexOf(t.Model);
        var j = i + direction;
        if (i < 0 || j < 0 || j >= _job.AudioTracks.Count) return;
        _job.AudioTracks.RemoveAt(i);
        _job.AudioTracks.Insert(j, t.Model);
        RebuildAudioTracks();
        Revalidate();
    }

    /// <summary>Abre la ventana que reproduce un trozo con la pista externa para ajustar su retraso.</summary>
    private void OpenSync(AudioTrackViewModel t)
    {
        if (_info is null || !t.Model.IsExternal) return;
        if (_info.Video is null) { _dialogs.Info("Este archivo no tiene vídeo con el que comparar.", "Sincronización"); return; }
        var vm = new SyncViewModel(_tools, _info, t.Model, PreviewIndex, _hw.Supports(VideoCodec.H264, EncoderBackend.Gpu));
        if (_dialogs.ShowSync(vm)) t.DelayMs = vm.DelayMs;
    }

    private void RebuildAudioTracks()
    {
        AudioTracks.Clear();
        if (_job is null) return;
        for (var k = 0; k < _job.AudioTracks.Count; k++)
        {
            var t = _job.AudioTracks[k];
            string desc;
            if (t.IsExternal) desc = $"Archivo externo · {Path.GetFileName(t.ExternalPath)} · {CodecNames.Display(t.SourceCodec)}";
            else
            {
                var s = _info!.Streams.First(x => x.Index == t.StreamIndex);
                desc = $"Pista #{s.Index} · {s.Describe()}";
            }
            var vm = new AudioTrackViewModel(t, desc, k, _job.AudioTracks.Count, Revalidate, RemoveTrack, MoveTrack, OpenSync);
            vm.RefreshCodecOptions(_job.Container);
            AudioTracks.Add(vm);
        }
        OnPropertyChanged(nameof(HasAudioTracks));
    }

    public bool HasAudioTracks => AudioTracks.Count > 0;

    /// <summary>Los ajustes de "Corrección del volumen" solo actúan sobre las pistas en "Modo noche".</summary>
    public bool HasNightTracks => _job?.AudioTracks.Any(t => t.Volume == VolumeMode.Night && t.Codec != AudioCodecChoice.Copy) ?? false;

    // ================================================================== Volumen
    private int _dialogBoost;
    private int _compression;
    private Option<double> _targetLufs;

    public static IReadOnlyList<Option<double>> TargetLufsOptions { get; } = new[]
    {
        new Option<double>(-14, "Más fuerte (−14 LUFS)"),
        new Option<double>(-16, "Como Netflix o YouTube (−16 LUFS) · recomendado"),
        new Option<double>(-20, "Más bajo (−20 LUFS)"),
        new Option<double>(-23, "Como la tele europea (−23 LUFS)"),
    };

    public int DialogBoost
    {
        get => _dialogBoost;
        set
        {
            if (!Set(ref _dialogBoost, value)) return;
            _settings.DialogBoost = value; _settings.Save();
            if (_job is not null) _job.Volume.DialogBoost = value;
            Revalidate();
        }
    }

    public int Compression
    {
        get => _compression;
        set
        {
            if (!Set(ref _compression, value)) return;
            _settings.Compression = value; _settings.Save();
            if (_job is not null) _job.Volume.Compression = value;
            Revalidate();
        }
    }

    public Option<double> SelectedTargetLufs
    {
        get => _targetLufs;
        set
        {
            if (value is null || !Set(ref _targetLufs, value)) return;
            _settings.TargetLufs = value.Value; _settings.Save();
            if (_job is not null) _job.Volume.TargetLufs = value.Value;
            Revalidate();
        }
    }

    public string DialogBoostText => DialogBoost switch
    {
        0 => "Nivel 0 · Mezcla normal: voces, música y efectos con el mismo peso que en el original.",
        <= 3 => $"Nivel {DialogBoost} · Ligero: las voces destacan un poco más que la música y los efectos.",
        <= 6 => $"Nivel {DialogBoost} · Notable: las voces se entienden claramente y la música y los efectos quedan algo más bajos.",
        _ => $"Nivel {DialogBoost} · Fuerte: la música y los efectos pasan a segundo plano; las voces mandan.",
    };

    public string CompressionText => Compression switch
    {
        0 => "Nivel 0 · Sin compresión: la película conserva toda su fuerza; solo se ajusta el volumen medio.",
        <= 3 => $"Nivel {Compression} · Ligera: se suavizan solo los golpes más fuertes.",
        <= 6 => $"Nivel {Compression} · Media: las explosiones siguen sonando con fuerza, pero ya no obligan a bajar la tele.",
        _ => $"Nivel {Compression} · Fuerte: casi todo suena a un volumen parecido. Ideal para ver de noche con el volumen bajo.",
    };

    public string TargetLufsText => SelectedTargetLufs.Value switch
    {
        -14 => "La película quedará más fuerte que la mayoría de programas: puede que tengas que bajar la tele.",
        -16 => "La película quedará al mismo volumen medio que Netflix o YouTube: no tendrás que tocar la tele al cambiar de una a otra.",
        -20 => "La película quedará algo más baja de lo habitual: tendrás que subir un poco la tele.",
        _ => "El nivel de las cadenas de televisión europeas. Suena bastante bajo: tendrás que subir la tele.",
    };

    /// <summary>Aviso cuando el realce de diálogos no puede actuar (audio estéreo).</summary>
    public string DialogBoostNote
    {
        get
        {
            if (_job is null) return "";
            var night = _job.AudioTracks.Where(t => t.Volume == VolumeMode.Night).ToList();
            if (night.Count == 0 || night.Any(t => t.SourceChannels >= 6)) return "";
            return "Este audio es estéreo: la voz ya viene mezclada con lo demás y no se puede separar, así que el realce de diálogos no tendrá efecto. La compresión y el volumen final sí funcionan.";
        }
    }

    public string VolumeAppliesText
    {
        get
        {
            if (_job is null) return "";
            var night = _job.AudioTracks.Count(t => t.Volume == VolumeMode.Night);
            var soft = _job.AudioTracks.Count(t => t.Volume == VolumeMode.Soft);
            if (night + soft == 0) return "Estos ajustes están desactivados porque ninguna pista usa el \"Modo noche\". Para activarlos, elige \"Modo noche\" en el desplegable \"Volumen\" de una pista (arriba).";
            var text = night switch
            {
                0 => "Estos ajustes están desactivados porque ninguna pista está en \"Modo noche\" (para activarlos, elígelo en el desplegable \"Volumen\" de una pista).",
                1 => "Estos tres ajustes se aplican a la pista que tiene el volumen en \"Modo noche\".",
                _ => $"Estos tres ajustes se aplican a las {night} pistas que tienen el volumen en \"Modo noche\".",
            };
            if (soft > 0) text += " Las pistas en \"Suave\" solo se igualan con una compresión ligera y fija.";
            return text + " Las pistas copiadas sin cambios no se tocan.";
        }
    }

    // ================================================================== Subtítulos
    public ObservableCollection<SubtitleViewModel> SubtitleTracks { get; } = new();
    public ObservableCollection<Option<int>> BurnOptions { get; } = new();
    public bool HasSubtitles => SubtitleTracks.Count > 0;

    private string _subtitleCountText = "";
    public string SubtitleCountText { get => _subtitleCountText; private set => Set(ref _subtitleCountText, value); }

    /// <summary>Los subtítulos a quemar los eligió Recodec (no el usuario): se pueden corregir al contar las líneas.</summary>
    private bool _burnChosenAutomatically;

    /// <summary>
    /// Si Recodec decidió quemar unos subtítulos "completos" que al contar resultan ser forzados, cambia a los
    /// completos del mismo idioma (los que más líneas tienen).
    /// </summary>
    private void RefineAutomaticBurn()
    {
        if (_job is null || _info is null || !_burnChosenAutomatically || _job.Subtitles.BurnKind != BurnSourceKind.Internal) return;
        var burned = _info.Streams.FirstOrDefault(s => s.Index == _job.Subtitles.BurnStreamIndex);
        if (burned is null || !burned.IsForced) return;
        var full = _info.SubtitleStreams.Where(s => !s.IsForced && Languages.Same(s.Language, burned.Language))
                                        .OrderByDescending(s => s.EventCount ?? 0).FirstOrDefault();
        if (full is null) return;
        _job.Subtitles.BurnStreamIndex = full.Index;
        if (!_job.Subtitles.Keep.Contains(burned.Index) && ContainerRules.SoftSubtitleCodec(_job.Container, burned) is not null) _job.Subtitles.Keep.Add(burned.Index);
        Log($"Los subtítulos #{burned.Index} solo tienen {burned.EventCount} líneas (son forzados): se queman los completos #{full.Index}.");
    }
    private const int ExternalBurn = -2;

    private void RebuildSubtitles()
    {
        SubtitleTracks.Clear();
        BurnOptions.Clear();
        if (_job is null || _info is null) return;
        foreach (var s in _info.SubtitleStreams)
        {
            SubtitleTracks.Add(new SubtitleViewModel(s, _job.Subtitles.Keep.Contains(s.Index), (vm, inc) =>
            {
                if (inc && !_job.Subtitles.Keep.Contains(vm.Stream.Index)) _job.Subtitles.Keep.Add(vm.Stream.Index);
                if (!inc) _job.Subtitles.Keep.Remove(vm.Stream.Index);
                Revalidate();
            }));
            BurnOptions.Add(new Option<int>(s.Index, s.Describe()));
        }
        BurnOptions.Add(new Option<int>(ExternalBurn, "Archivo de subtítulos externo (.srt, .ass, .sup)…"));
        OnPropertyChanged(nameof(HasSubtitles));
    }

    public bool BurnEnabled
    {
        get => _job?.Subtitles.Burns ?? false;
        set
        {
            if (_job is null) return;
            _burnChosenAutomatically = false;
            if (!value) _job.Subtitles.BurnKind = BurnSourceKind.None;
            else
            {
                var first = _info?.SubtitleStreams.OrderByDescending(s => Languages.Same(s.Language, _preferredLanguage.Code)).ThenByDescending(s => s.IsForced).FirstOrDefault();
                if (first is not null) { _job.Subtitles.BurnKind = BurnSourceKind.Internal; _job.Subtitles.BurnStreamIndex = first.Index; }
                else _job.Subtitles.BurnKind = BurnSourceKind.External;
            }
            Revalidate();
        }
    }

    public Option<int>? SelectedBurnOption
    {
        get
        {
            if (_job is null || !_job.Subtitles.Burns) return null;
            return _job.Subtitles.BurnKind == BurnSourceKind.External
                ? BurnOptions.FirstOrDefault(o => o.Value == ExternalBurn)
                : BurnOptions.FirstOrDefault(o => o.Value == _job.Subtitles.BurnStreamIndex);
        }
        set
        {
            if (_job is null || value is null) return;
            _burnChosenAutomatically = false;
            if (value.Value == ExternalBurn)
            {
                _job.Subtitles.BurnKind = BurnSourceKind.External;
                if (string.IsNullOrEmpty(_job.Subtitles.BurnExternalPath)) BrowseBurnFile();
            }
            else
            {
                _job.Subtitles.BurnKind = BurnSourceKind.Internal;
                _job.Subtitles.BurnStreamIndex = value.Value;
            }
            Revalidate();
        }
    }

    public bool BurnIsExternal => _job?.Subtitles.BurnKind == BurnSourceKind.External;
    public string BurnExternalPath => _job?.Subtitles.BurnExternalPath ?? "Ningún archivo elegido";

    private void BrowseBurnFile()
    {
        if (_job is null) return;
        var p = _dialogs.OpenFile("Subtítulos para quemar en la imagen", "Subtítulos|*.srt;*.ass;*.ssa;*.vtt;*.sup|Todos los archivos|*.*");
        if (p is null) return;
        _job.Subtitles.BurnKind = BurnSourceKind.External;
        _job.Subtitles.BurnExternalPath = p;
        Revalidate();
    }

    // ================================================================== Salida
    public static IReadOnlyList<Option<ContainerKind>> ContainerOptions { get; } = new[]
    {
        new Option<ContainerKind>(ContainerKind.Mp4, "MP4", "El más compatible: teles, móviles, \"Reproducir en\" de Windows."),
        new Option<ContainerKind>(ContainerKind.Mkv, "MKV", "Admite cualquier pista y subtítulo, pero no todos los dispositivos lo aceptan."),
        new Option<ContainerKind>(ContainerKind.Mov, "MOV", "Formato de Apple (iPhone, Mac, Final Cut)."),
        new Option<ContainerKind>(ContainerKind.WebM, "WebM", "Para la web: solo vídeo AV1 o VP9 y audio Opus."),
        new Option<ContainerKind>(ContainerKind.Avi, "AVI", "El clásico de DivX/XviD: para reproductores antiguos. Sin subtítulos como pista."),
    };

    public Option<ContainerKind>? SelectedContainer
    {
        get => ContainerOptions.FirstOrDefault(o => o.Value == _job?.Container);
        set
        {
            if (_job is null || value is null || value.Value == _job.Container) return;
            _job.Container = value.Value;
            _settings.Container = value.Value; _settings.Save();
            // Al cambiar de contenedor se vuelven a ofrecer los subtítulos del idioma favorito.
            foreach (var s in _info!.SubtitleStreams.Where(s => Languages.Same(s.Language, _preferredLanguage.Code)))
                if (!_job.Subtitles.Keep.Contains(s.Index) && ContainerRules.SoftSubtitleCodec(_job.Container, s) is not null)
                    _job.Subtitles.Keep.Add(s.Index);
            if (_job.Video.Mode == VideoMode.Encode && _info.Video is { } v && JobPlanner.ForcedEncodeReasons(_job, v).Count == 0 && ContainerRules.CanHoldVideo(_job.Container, v.Codec) && _wasAutoEncode)
                _job.Video.Mode = VideoMode.Copy;
            RebuildVideoOptions();
            foreach (var t in AudioTracks) t.RefreshCodecOptions(_job.Container);
            Revalidate();
            RebuildSubtitles();
            OnPropertyChanged(nameof(SelectedContainer));
        }
    }

    private bool _wasAutoEncode;

    public string ContainerHint => SelectedContainer?.Hint ?? "";

    public string OutputPath
    {
        get => _job?.OutputPath ?? "";
        set
        {
            if (_job is null) return;
            _job.OutputPath = value;
            if (_rememberOutputFolder) SaveOutputFolder();
            Revalidate();
        }
    }

    private void BrowseOutput()
    {
        if (_job is null) return;
        var p = _dialogs.SaveVideo(_job.OutputPath, _job.Container);
        if (p is null) return;
        OutputPath = p;
        OnPropertyChanged(nameof(OutputPath));
    }

    public bool KeepChapters
    {
        get => _job?.KeepChapters ?? true;
        set { if (_job is null) return; _job.KeepChapters = value; Revalidate(); }
    }

    public bool FastStart
    {
        get => _job?.FastStart ?? true;
        set { if (_job is null) return; _job.FastStart = value; Revalidate(); }
    }

    private bool _rememberOutputFolder;
    public bool RememberOutputFolder
    {
        get => _rememberOutputFolder;
        set
        {
            if (!Set(ref _rememberOutputFolder, value)) return;
            if (value) SaveOutputFolder();
            else { _settings.OutputFolder = null; _settings.Save(); }
        }
    }

    private void SaveOutputFolder()
    {
        var dir = Path.GetDirectoryName(_job?.OutputPath);
        if (string.IsNullOrEmpty(dir)) return;
        _settings.OutputFolder = dir;
        _settings.Save();
    }

    private bool _openFolderWhenDone = true;
    public bool OpenFolderWhenDone
    {
        get => _openFolderWhenDone;
        set { if (Set(ref _openFolderWhenDone, value)) { _settings.OpenFolderWhenDone = value; _settings.Save(); } }
    }

    public bool CheckUpdatesOnStartup
    {
        get => _settings.CheckUpdatesOnStartup;
        set { _settings.CheckUpdatesOnStartup = value; _settings.Save(); OnPropertyChanged(); }
    }

    // ================================================================== Validación y avisos
    public ObservableCollection<string> Notes { get; } = new();
    private bool _canRun;
    public bool CanRun { get => _canRun; private set => Set(ref _canRun, value); }
    private string _errorText = "";
    public string ErrorText { get => _errorText; private set => Set(ref _errorText, value); }
    public bool HasNotes => Notes.Count > 0;

    public string CommandPreview => _job is null ? "" : ProcessRunner.FormatCommandLine(_tools.Ffmpeg, CommandBuilder.Build(_job));

    private void RebuildVideoOptions()
    {
        VideoCodecOptions.Clear();
        if (_job is null) return;
        foreach (var c in new[] { VideoCodec.Hevc, VideoCodec.H264, VideoCodec.Av1, VideoCodec.Vp9, VideoCodec.Xvid })
        {
            if (!ContainerRules.CanEncodeVideo(_job.Container, c)) continue;
            VideoCodecOptions.Add(c switch
            {
                VideoCodec.Hevc => new Option<VideoCodec>(c, "HEVC / H.265 · 10 bits (recomendado)"),
                VideoCodec.H264 => new Option<VideoCodec>(c, "H.264 · máxima compatibilidad"),
                VideoCodec.Av1 => new Option<VideoCodec>(c, "AV1 · archivos más pequeños"),
                VideoCodec.Vp9 => new Option<VideoCodec>(c, "VP9 · el de YouTube (solo CPU, lento)"),
                _ => new Option<VideoCodec>(c, "XviD / DivX · para reproductores antiguos"),
            });
        }
        RebuildBackendOptions();
    }

    private void RebuildBackendOptions()
    {
        BackendOptions.Clear();
        var codec = _job?.Video.Codec ?? VideoCodec.Hevc;
        foreach (var b in _hw.BackendsFor(codec).Where(b => b != EncoderBackend.Cpu))
            BackendOptions.Add(new Option<EncoderBackend>(b, b switch
            {
                EncoderBackend.Gpu => "Tarjeta gráfica NVIDIA (NVENC) · muy rápido",
                EncoderBackend.Qsv => "Gráfica Intel (Quick Sync) · muy rápido",
                _ => "Gráfica AMD (AMF) · muy rápido",
            }));
        var cpuName = codec switch
        {
            VideoCodec.Hevc => "x265",
            VideoCodec.H264 => "x264",
            VideoCodec.Av1 => "AOM",
            VideoCodec.Vp9 => "libvpx",
            _ => "Xvid",
        };
        BackendOptions.Add(new Option<EncoderBackend>(EncoderBackend.Cpu, BackendOptions.Count > 0
            ? $"Procesador ({cpuName}) · lento, algo más eficiente"
            : ContainerRules.HasGpuEncoder(codec) && !_hw.Any
                ? $"Procesador ({cpuName}) · no hay una gráfica que pueda codificar vídeo"
                : $"Procesador ({cpuName}) · este códec no se puede hacer con la gráfica"));
        OnPropertyChanged(nameof(SelectedBackend));
    }

    private void RebuildTrackLists()
    {
        RebuildVideoOptions();
        RebuildAudioTracks();
        RebuildSubtitles();
        SourceAudioOptions.Clear();
        if (_info is not null)
            foreach (var s in _info.AudioStreams) SourceAudioOptions.Add(new Option<StreamInfo>(s, $"#{s.Index} · {s.Describe()}"));
        SelectedSourceAudio = SourceAudioOptions.FirstOrDefault();
        _wasAutoEncode = _job?.Video.Mode == VideoMode.Encode;
    }

    private bool _revalidating;

    /// <summary>Aplica las reglas (JobPlanner.Normalize) y refresca todo lo que dependa de ellas.</summary>
    private void Revalidate()
    {
        if (_job is null || _revalidating) return;
        _revalidating = true;
        try
        {
            var codecsBefore = _job.AudioTracks.Select(t => t.Codec).ToList();
            var keepBefore = _job.Subtitles.Keep.ToList();
            var rep = JobPlanner.Normalize(_job);

            Notes.Clear();
            foreach (var n in rep.Adjustments.Concat(rep.Warnings).Distinct()) Notes.Add(n);
            ErrorText = string.Join(" ", rep.Errors);
            RefreshTvChecks();
            if (TvChecks.Any(c => c.Level == CheckLevel.Problem))
                Notes.Add((TargetDevice!.IsGeneric ? "Muchas teles no aceptarán este vídeo tal como está." : $"{TargetDevice.DisplayName} no aceptará este vídeo tal como está.")
                          + " Puedes ajustarlo en la pestaña Salida (\"Aplicar ajustes para la tele\").");
            CanRun = rep.CanRun && _tools.IsAvailable;

            if (!codecsBefore.SequenceEqual(_job.AudioTracks.Select(t => t.Codec))) foreach (var t in AudioTracks) t.Refresh();
            if (!keepBefore.SequenceEqual(_job.Subtitles.Keep))
                foreach (var s in SubtitleTracks) s.SetIncludeSilently(_job.Subtitles.Keep.Contains(s.Stream.Index));
            foreach (var s in SubtitleTracks)
            {
                var codec = ContainerRules.SoftSubtitleCodec(_job.Container, s.Stream);
                var burning = _job.Subtitles.BurnKind == BurnSourceKind.Internal && _job.Subtitles.BurnStreamIndex == s.Stream.Index;
                s.CanInclude = codec is not null && !burning;
                s.Status = burning ? "Se quemará en la imagen"
                    : codec is null ? (_job.Container == ContainerKind.Avi ? "AVI no admite subtítulos como pista: solo se pueden quemar" : $"{ContainerRules.DisplayName(_job.Container)} no admite subtítulos de imagen: solo se pueden quemar")
                    : codec == "copy" ? "Se guardará tal cual"
                    : $"Se guardará como {codec switch { "mov_text" => "texto MP4", "webvtt" => "WebVTT", "srt" => "SRT", _ => codec }}";
            }
            RefreshAll();
        }
        finally { _revalidating = false; }
    }

    private bool _refreshing;

    private void RefreshAll()
    {
        _refreshing = true;
        try { RefreshAllCore(); } finally { _refreshing = false; }
        System.Windows.Input.CommandManager.InvalidateRequerySuggested();
    }

    private void RefreshAllCore()
    {
        RebuildResolutionOptions();
        foreach (var name in new[]
        {
            nameof(QualityMin), nameof(QualityMax), nameof(HasFile), nameof(IsCopyMode), nameof(IsEncodeMode), nameof(CanCopy), nameof(CopyBlockedText), nameof(VideoModeText),
            nameof(SelectedVideoCodec), nameof(SelectedBackend), nameof(Quality), nameof(QualityHint), nameof(Speed), nameof(SpeedText),
            nameof(SelectedResolution), nameof(AutoCrop), nameof(SelectedDeinterlace), nameof(HdrText), nameof(SelectedDenoise), nameof(DenoiseText),
            nameof(SelectedProfile), nameof(ProfileDescription),
            nameof(PreferredLanguage), nameof(HasAudioTracks), nameof(DialogBoost), nameof(Compression), nameof(DialogBoostText),
            nameof(CompressionText), nameof(SelectedTargetLufs), nameof(VolumeAppliesText), nameof(BurnEnabled), nameof(SelectedBurnOption),
            nameof(BurnIsExternal), nameof(BurnExternalPath), nameof(HasSubtitles), nameof(SelectedContainer), nameof(ContainerHint),
            nameof(OutputPath), nameof(KeepChapters), nameof(FastStart), nameof(HasNotes), nameof(CommandPreview), nameof(EstimateText),
            nameof(TargetLufsText), nameof(DialogBoostNote), nameof(HasNightTracks), nameof(SelectedTarget), nameof(HasTvChecks), nameof(TvSummary),
        })
            OnPropertyChanged(name);
    }

    /// <summary>Estimación del tiempo de conversión (mejora con cada conversión hecha en este equipo).</summary>
    public string EstimateText
    {
        get
        {
            if (_job is null || _info is null) return "";
            var (t, learned) = SpeedEstimator.Estimate(_job, _settings.EncodeSpeeds);
            var copy = _job.Video.Mode == VideoMode.Copy || _info.Video is null;
            var text = t.TotalMinutes < 1.5 ? "un par de minutos"
                : t.TotalHours >= 1 ? $"unas {Math.Round(t.TotalHours, 1).ToString(Es)} horas"
                : $"unos {Math.Round(t.TotalMinutes)} minutos";
            return $"Tiempo estimado: {text}" + (copy ? " (el vídeo se copia)." : learned ? " (según tus conversiones anteriores)." : " (orientativo: se ajustará con tus conversiones).");
        }
    }
}
