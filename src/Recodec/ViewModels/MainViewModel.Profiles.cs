using System.Collections.ObjectModel;
using Recodec.Core.Conversion;
using Recodec.Core.Devices;
using Recodec.Infrastructure;

namespace Recodec.ViewModels;

/// <summary>Perfiles: conjuntos de ajustes con nombre, predefinidos o creados por el usuario.</summary>
public sealed partial class MainViewModel
{
    private const string LastUsed = "";

    public ObservableCollection<Option<string>> ProfileOptions { get; } = new();
    public RelayCommand SaveProfileCommand { get; private set; } = null!;
    public RelayCommand DeleteProfileCommand { get; private set; } = null!;

    private void InitProfiles()
    {
        SaveProfileCommand = new RelayCommand(SaveProfile, () => HasFile);
        DeleteProfileCommand = new RelayCommand(DeleteProfile, () => _settings.Profile is { IsBuiltIn: false });
        RebuildProfileOptions();
    }

    private void RebuildProfileOptions()
    {
        ProfileOptions.Clear();
        ProfileOptions.Add(new Option<string>(LastUsed, "Mis últimos ajustes", "Cada vídeo se abre con las últimas opciones que elegiste."));
        foreach (var p in ConversionProfile.BuiltIn) ProfileOptions.Add(new Option<string>(p.Name, p.Name, p.Description));
        foreach (var p in _settings.Profiles.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase))
            ProfileOptions.Add(new Option<string>(p.Name, p.Name + " · mío", p.Description ?? "Perfil creado por ti."));
        if (_settings.ProfileName is not null && _settings.Profile is null) { _settings.ProfileName = null; _settings.Save(); }
        OnPropertyChanged(nameof(SelectedProfile));
        OnPropertyChanged(nameof(ProfileDescription));
    }

    public Option<string>? SelectedProfile
    {
        get => ProfileOptions.FirstOrDefault(o => o.Value == (_settings.ProfileName ?? LastUsed));
        set
        {
            if (value is null || value.Value == (_settings.ProfileName ?? LastUsed)) return;
            _settings.ProfileName = value.Value == LastUsed ? null : value.Value;
            _settings.Save();
            OnPropertyChanged();
            OnPropertyChanged(nameof(ProfileDescription));
            if (_settings.Profile is { } p) ApplyProfileToCurrent(p);
        }
    }

    public string ProfileDescription => SelectedProfile?.Hint ?? "";

    /// <summary>Aplica un perfil al vídeo abierto (los siguientes que se abran ya lo usan solos).</summary>
    private void ApplyProfileToCurrent(ConversionProfile p)
    {
        if (_job is null || _info is null) return;
        p.ApplyTo(_job, _hw);
        if (_job.Video.Mode == VideoMode.Copy && _info.Video is { } v && !JobPlanner.CanCopyVideo(_job, v)) _job.Video.Mode = VideoMode.Encode;
        _wasAutoEncode = _job.Video.Mode == VideoMode.Encode;
        SyncVolumeFromJob();
        RebuildVideoOptions();
        RebuildAudioTracks();
        RebuildSubtitles();
        Revalidate();
        StatusText = $"Perfil aplicado: {p.Name}";
    }

    /// <summary>Los controles de volumen muestran los valores del trabajo (que pueden venir de un perfil).</summary>
    private void SyncVolumeFromJob()
    {
        if (_job is null) return;
        _dialogBoost = _job.Volume.DialogBoost;
        _compression = _job.Volume.Compression;
        _targetLufs = TargetLufsOptions.FirstOrDefault(o => Math.Abs(o.Value - _job.Volume.TargetLufs) < 0.1) ?? TargetLufsOptions[1];
        OnPropertyChanged(nameof(DialogBoost));
        OnPropertyChanged(nameof(Compression));
        OnPropertyChanged(nameof(SelectedTargetLufs));
    }

    private void SaveProfile()
    {
        if (_job is null) return;
        var current = _settings.Profile is { IsBuiltIn: false } mine ? mine.Name : "";
        var name = _dialogs.AskText("Guardar perfil",
            "Nombre del perfil. Se guardan el contenedor, las opciones de vídeo, el formato y el volumen del audio y las opciones de salida (no las pistas ni el destino, que dependen de cada vídeo).",
            current)?.Trim();
        if (string.IsNullOrEmpty(name)) return;
        if (ConversionProfile.BuiltIn.Any(b => string.Equals(b.Name, name, StringComparison.CurrentCultureIgnoreCase)))
        {
            _dialogs.Info($"\"{name}\" es un perfil que viene con el programa. Elige otro nombre.", "Guardar perfil");
            return;
        }
        var existing = _settings.Profiles.FindIndex(p => string.Equals(p.Name, name, StringComparison.CurrentCultureIgnoreCase));
        if (existing >= 0 && !_dialogs.Confirm($"Ya tienes un perfil llamado \"{_settings.Profiles[existing].Name}\". ¿Quieres sustituirlo por los ajustes actuales?", "Guardar perfil"))
            return;

        var profile = ConversionProfile.FromJob(name, _job) with { Description = DescribeProfile(_job) };
        if (existing >= 0) _settings.Profiles[existing] = profile; else _settings.Profiles.Add(profile);
        _settings.ProfileName = profile.Name;
        _settings.Save();
        RebuildProfileOptions();
        StatusText = $"Perfil guardado: {profile.Name}";
    }

    private void DeleteProfile()
    {
        if (_settings.Profile is not { IsBuiltIn: false } p) return;
        if (!_dialogs.Confirm($"¿Borrar el perfil \"{p.Name}\"? No afecta a los vídeos que ya están en la cola.", "Borrar perfil")) return;
        _settings.Profiles.RemoveAll(x => x.Name == p.Name);
        _settings.ProfileName = null;
        _settings.Save();
        RebuildProfileOptions();
    }

    /// <summary>Resumen legible de un perfil creado por el usuario.</summary>
    private static string DescribeProfile(ConversionJob job)
    {
        var v = job.Video;
        var video = v.Mode == VideoMode.Copy ? "vídeo copiado si se puede" : $"{JobPlanner.CodecLabel(v.Codec)} calidad {v.Quality}";
        if (v.MaxHeight > 0) video += $" a {v.MaxHeight}p";
        if (v.Denoise != DenoiseMode.Off) video += ", quitando ruido";
        var a = job.AudioTracks.FirstOrDefault(t => t.Codec != AudioCodecChoice.Copy);
        var audio = a is null ? "audio copiado" : ContainerRules.AudioCodecName(a.Codec) + a.Volume switch { VolumeMode.Night => " con modo noche", VolumeMode.Soft => " suave", _ => "" };
        return $"{ContainerRules.DisplayName(job.Container)} · {video} · {audio}.";
    }
}
