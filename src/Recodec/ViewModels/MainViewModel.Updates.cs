using Recodec.Core.Ffmpeg;

namespace Recodec.ViewModels;

public sealed partial class MainViewModel
{
    private readonly FfmpegUpdater _updater;
    private bool _updateAvailable;
    private bool _isUpdating;
    private string _latestFfmpeg = "";
    private double _updateProgress;
    private string _updateText = "";

    public bool UpdateAvailable { get => _updateAvailable; set => Set(ref _updateAvailable, value); }
    public bool IsUpdating { get => _isUpdating; set => Set(ref _isUpdating, value); }
    public string LatestFfmpeg { get => _latestFfmpeg; set => Set(ref _latestFfmpeg, value); }
    public double UpdateProgress { get => _updateProgress; set => Set(ref _updateProgress, value); }
    public string UpdateText { get => _updateText; set => Set(ref _updateText, value); }

    /// <summary>Comprueba si hay una versión nueva de FFmpeg (como mucho una vez al día, salvo que se pida a mano).</summary>
    private async Task CheckForUpdatesAsync(bool manual)
    {
        if (!manual && _settings.LastUpdateCheck is { } last && DateTime.Now - last < TimeSpan.FromDays(1)) return;
        try
        {
            var r = await _updater.CheckAsync();
            _settings.LastUpdateCheck = DateTime.Now;
            _settings.Save();
            LatestFfmpeg = r.Latest ?? "";
            if (r.UpdateAvailable && (manual || r.Latest != _settings.SkippedFfmpegVersion))
            {
                UpdateText = $"Hay una versión nueva de FFmpeg: {r.Latest} (tienes la {r.Installed ?? "?"}).";
                UpdateAvailable = true;
            }
            else if (manual)
            {
                _dialogs.Info($"Tienes la última versión de FFmpeg ({r.Installed}).", "FFmpeg al día");
            }
        }
        catch (Exception ex)
        {
            Log("No se ha podido comprobar si hay actualizaciones de FFmpeg: " + ex.Message);
            if (manual) _dialogs.Info("No se ha podido comprobar si hay actualizaciones (¿sin conexión?).\n" + ex.Message, "Actualizar FFmpeg");
        }
    }

    private async Task InstallFfmpegUpdateAsync()
    {
        if (IsRunning || IsUpdating) return;
        IsUpdating = true;
        UpdateAvailable = true;
        UpdateText = "Descargando FFmpeg…";
        try
        {
            var v = await _updater.InstallLatestAsync(
                new Progress<double>(p => { UpdateProgress = p; UpdateText = $"Descargando FFmpeg… {p:0} %"; }),
                msg => { Log(msg); System.Windows.Application.Current.Dispatcher.BeginInvoke(() => UpdateText = msg); });
            UpdateAvailable = false;
            _tools.ResetCapabilities();
            await DetectHardwareAsync();
            RebuildBackendOptions();
            Revalidate();
            _dialogs.Info($"FFmpeg {v} instalado correctamente.", "Actualizar FFmpeg");
        }
        catch (Exception ex)
        {
            UpdateText = "La actualización ha fallado: " + ex.Message;
            Log(UpdateText);
            _dialogs.Info("No se ha podido actualizar FFmpeg. Se mantiene la versión que tenías.\n\n" + ex.Message, "Actualizar FFmpeg");
        }
        finally
        {
            IsUpdating = false;
            UpdateProgress = 0;
        }
    }
}
