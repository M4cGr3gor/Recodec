using System.Text.Json;
using System.Text.Json.Serialization;
using Recodec.Core.Conversion;
using Recodec.Core.Devices;
using Recodec.Core.Media;

namespace Recodec.Core.Settings;

/// <summary>Preferencias del usuario. Se guardan junto al programa (versión portable).</summary>
public sealed class AppSettings
{
    public string PreferredLanguage { get; set; } = Languages.FromOperatingSystem();
    public ContainerKind Container { get; set; } = ContainerKind.Mp4;
    public VideoCodec VideoCodec { get; set; } = VideoCodec.Hevc;
    public EncoderBackend Backend { get; set; } = EncoderBackend.Gpu;
    public int Speed { get; set; } = 2;
    public AudioCodecChoice AudioCodec { get; set; } = AudioCodecChoice.AacStereo;
    public VolumeMode VolumeMode { get; set; } = VolumeMode.Off;
    /// <summary>Versión del formato del archivo de configuración (para adaptar los antiguos).</summary>
    public int SettingsVersion { get; set; }
    public int DialogBoost { get; set; } = 7;
    public int Compression { get; set; } = 6;
    public double TargetLufs { get; set; } = -16;
    /// <summary>null = misma carpeta que el vídeo original.</summary>
    public string? OutputFolder { get; set; }
    public bool OpenFolderWhenDone { get; set; } = true;
    public bool CheckUpdatesOnStartup { get; set; } = true;
    public DateTime? LastUpdateCheck { get; set; }
    public string? SkippedFfmpegVersion { get; set; }

    /// <summary>Velocidades medidas en este equipo, para calcular el tiempo estimado (ver SpeedEstimator).</summary>
    public Dictionary<string, double> EncodeSpeeds { get; set; } = new();

    /// <summary>Perfiles creados por el usuario.</summary>
    public List<ConversionProfile> Profiles { get; set; } = new();
    /// <summary>Perfil que se aplica a cada vídeo que se abre (null = los últimos ajustes usados).</summary>
    public string? ProfileName { get; set; }

    [JsonIgnore]
    public IEnumerable<ConversionProfile> AllProfiles => ConversionProfile.BuiltIn.Concat(Profiles);

    [JsonIgnore]
    public ConversionProfile? Profile => ProfileName is null ? null : AllProfiles.FirstOrDefault(p => p.Name == ProfileName);

    /// <summary>Teles encontradas en la red (se guardan para no tener que buscarlas cada vez).</summary>
    public List<DeviceProfile> Devices { get; set; } = new();
    /// <summary>Aparato para el que se preparan los vídeos (null = ninguno; "generica" = tele cualquiera).</summary>
    public string? TargetDeviceId { get; set; } = DeviceProfile.GenericId;

    [JsonIgnore]
    public DeviceProfile? TargetDevice => TargetDeviceId is null ? null
        : TargetDeviceId == DeviceProfile.GenericId ? DeviceProfile.Generic
        : Devices.FirstOrDefault(d => d.Id == TargetDeviceId);

    [JsonIgnore] public string? FilePath { get; private set; }

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static AppSettings Load(string path)
    {
        AppSettings s;
        try
        {
            s = File.Exists(path) ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Options) ?? new() : new();
        }
        catch
        {
            s = new();
        }
        s.FilePath = path;
        // Versión 2: el volumen de las pistas nuevas pasa a "Sin cambios" (antes era "Modo noche" y no se podía cambiar).
        if (s.SettingsVersion < 2)
        {
            s.VolumeMode = VolumeMode.Off;
            s.SettingsVersion = 2;
            if (File.Exists(path)) s.Save();
        }
        return s;
    }

    public void Save()
    {
        if (FilePath is null) return;
        try { File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Options)); } catch { /* carpeta de solo lectura */ }
    }

    public JobPlanner.Defaults ToDefaults() => new()
    {
        PreferredLanguage = PreferredLanguage,
        Container = Container,
        Codec = VideoCodec,
        Backend = Backend,
        AudioCodec = AudioCodec,
        VolumeMode = VolumeMode,
        Volume = new VolumeSettings { DialogBoost = DialogBoost, Compression = Compression, TargetLufs = TargetLufs },
        OutputFolder = OutputFolder,
        Speed = Speed,
        Profile = Profile,
    };
}
