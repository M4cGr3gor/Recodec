using System.Collections.ObjectModel;
using Microsoft.Win32;
using Recodec.Core.Conversion;
using Recodec.Core.Devices;
using Recodec.Infrastructure;

namespace Recodec.ViewModels;

/// <summary>Compatibilidad con la tele: para qué aparato se preparan los vídeos y si lo aceptará.</summary>
public sealed partial class MainViewModel
{
    private const string NoTarget = "";

    public ObservableCollection<Option<string>> TargetOptions { get; } = new();
    public ObservableCollection<SupportRow> TvChecks { get; } = new();

    public DeviceProfile? TargetDevice => _settings.TargetDevice;
    public bool HasTvChecks => TvChecks.Count > 0;
    public bool HasTvProblems => TvChecks.Any(c => c.IsProblem);

    /// <summary>Lo que cambiaría el botón "Aplicar ajustes para la tele" (se calcula sobre una copia, sin tocar nada).</summary>
    private List<string> _pendingTvChanges = new();
    public bool HasPendingTvChanges => _pendingTvChanges.Count > 0;
    public string PendingTvChangesText => _pendingTvChanges.Count == 0 ? "" : "Al aplicar se cambiará: " + string.Join("; ", _pendingTvChanges) + ".";

    public Option<string>? SelectedTarget
    {
        get => TargetOptions.FirstOrDefault(o => o.Value == (_settings.TargetDeviceId ?? NoTarget));
        set
        {
            if (value is null || value.Value == (_settings.TargetDeviceId ?? NoTarget)) return;
            _settings.TargetDeviceId = value.Value == NoTarget ? null : value.Value;
            _settings.Save();
            OnPropertyChanged();
            // Elegir la tele solo comprueba: los cambios se aplican con el botón "Aplicar ajustes para la tele".
            Revalidate();
        }
    }

    public string TvSummary
    {
        get
        {
            var d = TargetDevice;
            if (d is null) return "No se comprueba ningún aparato. Elige tu tele para que Recodec prepare los vídeos de forma que la tele los acepte.";
            if (_job is null) return "";
            var problems = TvChecks.Count(c => c.IsProblem);
            if (problems > 0)
                return $"Hay {(problems == 1 ? "un problema" : $"{problems} problemas")}: la tele rechazaría este vídeo. Pulsa \"Aplicar ajustes para la tele\" si quieres corregirlo.";
            if (HasPendingTvChanges)
                return "La tele lo reproduciría, pero se puede mejorar la compatibilidad con el botón \"Aplicar ajustes para la tele\".";
            return _lastTvChanges.Count > 0 ? $"Ajustado para la tele ({string.Join("; ", _lastTvChanges)})." : "La tele aceptará este vídeo tal como está.";
        }
    }

    private void RebuildTargetOptions()
    {
        TargetOptions.Clear();
        TargetOptions.Add(new Option<string>(NoTarget, "Ninguno (no comprobar)"));
        TargetOptions.Add(new Option<string>(DeviceProfile.GenericId, DeviceProfile.Generic.Name));
        foreach (var d in _settings.Devices.OrderBy(d => d.DisplayName))
            TargetOptions.Add(new Option<string>(d.Id, d.DisplayName + " · leída de la red"));
        OnPropertyChanged(nameof(SelectedTarget));
    }

    private void RefreshTvChecks()
    {
        TvChecks.Clear();
        _pendingTvChanges = new();
        if (_job is not null && TargetDevice is { } d)
        {
            foreach (var c in TvCompatibility.Evaluate(_job, d)) TvChecks.Add(new SupportRow("", c.Level, c.Text));
            _pendingTvChanges = TvCompatibility.Adapt(_job.Clone(), d);
        }
        OnPropertyChanged(nameof(HasPendingTvChanges));
        OnPropertyChanged(nameof(PendingTvChangesText));
        OnPropertyChanged(nameof(HasTvChecks));
        OnPropertyChanged(nameof(HasTvProblems));
        OnPropertyChanged(nameof(TvSummary));
    }

    /// <summary>Lo último que se cambió para la tele (se muestra en el resumen de la pestaña Salida).</summary>
    private List<string> _lastTvChanges = new();

    private void OnTargetDeviceChanged()
    {
        RebuildTargetOptions();
        Revalidate();
    }

    /// <summary>Botón "Aplicar ajustes para la tele": cambia solo lo imprescindible para que la tele acepte el vídeo.</summary>
    private void AdaptToTv()
    {
        if (_job is null || TargetDevice is not { } d) { Revalidate(); return; }
        var changes = TvCompatibility.Adapt(_job, d);
        _lastTvChanges = changes;
        _wasAutoEncode = _job.Video.Mode == VideoMode.Encode;
        RebuildVideoOptions();
        RebuildAudioTracks();
        RebuildSubtitles();
        Revalidate();
        if (changes.Count > 0) StatusText = $"Ajustado para {(d.IsGeneric ? "cualquier tele" : d.DisplayName)}: {string.Join("; ", changes)}";
        _dialogs.Info(changes.Count == 0
                ? $"No hace falta cambiar nada: {(d.IsGeneric ? "cualquier tele" : d.DisplayName)} acepta ya este vídeo."
                : $"Cambios para que lo acepte {(d.IsGeneric ? "cualquier tele" : d.DisplayName)}:\n\n• " + string.Join("\n• ", changes),
            "Ajustar para la tele");
    }

    /// <summary>Tipo MIME que Windows asocia a cada extensión (es el que usa "Reproducir en").</summary>
    private static string WindowsMimeFromRegistry(ContainerKind c)
    {
        try
        {
            using var key = Registry.ClassesRoot.OpenSubKey(ContainerRules.Extension(c));
            if (key?.GetValue("Content Type") is string s && s.Contains('/')) return s.Trim().ToLowerInvariant();
        }
        catch { /* sin acceso al registro */ }
        return DeviceProfile.DefaultWindowsMime(c);
    }
}
