using Recodec.Core.Conversion;
using Recodec.Core.Media;

namespace Recodec.Core.Devices;

public enum CheckLevel { Ok, Warning, Problem }

public sealed record CheckItem(CheckLevel Level, string Text);

/// <summary>
/// Comprueba si el vídeo que se va a crear lo aceptará una tele concreta y, si no, ajusta la conversión.
/// El fallo típico: la tele sabe reproducir MKV pero lo anuncia con otro nombre (video/x-mkv) y
/// "Reproducir en" de Windows, que lo llama video/x-matroska, se niega a enviarlo.
/// </summary>
public static class TvCompatibility
{
    public static string? OutputVideoCodec(ConversionJob job)
    {
        var v = job.Source.Video;
        if (v is null) return null;
        if (job.Video.Mode == VideoMode.Copy) return v.Codec;
        return job.Video.Codec switch
        {
            VideoCodec.Hevc => "hevc",
            VideoCodec.H264 => "h264",
            VideoCodec.Av1 => "av1",
            VideoCodec.Vp9 => "vp9",
            _ => "mpeg4",
        };
    }

    public static string OutputAudioCodec(AudioTrackJob t) => t.Codec switch
    {
        AudioCodecChoice.Copy => t.SourceCodec,
        AudioCodecChoice.AacStereo or AudioCodecChoice.Aac51 => "aac",
        AudioCodecChoice.Ac3_51 => "ac3",
        AudioCodecChoice.Eac3_51 => "eac3",
        AudioCodecChoice.OpusStereo => "opus",
        AudioCodecChoice.Mp3Stereo => "mp3",
        AudioCodecChoice.Flac => "flac",
        _ => t.SourceCodec,
    };

    private static string Who(DeviceProfile p) => p.IsGeneric ? "La mayoría de teles" : "Tu tele";

    public static List<CheckItem> Evaluate(ConversionJob job, DeviceProfile p)
    {
        var r = new List<CheckItem>();
        var c = job.Container;
        var cn = ContainerRules.DisplayName(c);

        // Contenedor
        if (p.AcceptsFromWindows(c))
            r.Add(new(CheckLevel.Ok, $"Contenedor {cn}: la tele lo acepta y \"Reproducir en\" de Windows lo enviará."));
        else if (p.PlaysContainer(c))
            r.Add(new(CheckLevel.Problem, $"Contenedor {cn}: la tele lo reproduce, pero lo anuncia como \"{DeviceProfile.ContainerMimes(c).First(p.Mimes.Contains)}\" y Windows lo llama \"{DeviceProfile.WindowsMime(c)}\". Al no coincidir, \"Reproducir en\" dirá que no puede reproducirlo. Usa MP4."));
        else
            r.Add(new(CheckLevel.Problem, $"Contenedor {cn}: {(p.IsGeneric ? "muchas teles no lo aceptan por DLNA" : "la tele no lo anuncia")}. Usa MP4."));

        // Vídeo
        var vc = OutputVideoCodec(job);
        if (vc is not null)
        {
            var name = CodecNames.Display(vc);
            if (p.SupportsVideo(vc))
                r.Add(new(CheckLevel.Ok, $"Vídeo {name}: {(p.IsGeneric ? "lo reproducen las teles de los últimos años" : "la tele lo admite")}{(job.Video.Mode == VideoMode.Copy ? " (se copia sin recodificar)" : "")}."));
            else
                r.Add(new(CheckLevel.Problem, $"Vídeo {name}: {(p.IsGeneric ? "muchas teles no lo admiten" : "la tele no lo anuncia")}. Recodifica a {(p.SupportsVideo("hevc") ? "HEVC" : "H.264")}."));
            if (job.Source.Video!.Hdr != HdrKind.None && !CommandBuilder.NeedsToneMap(job, job.Source.Video))
                r.Add(new(CheckLevel.Warning, "El vídeo es HDR. Las teles 4K HDR lo muestran bien; una tele sin HDR lo verá con colores apagados (en ese caso, recodifica a H.264, que lo pasa a SDR)."));
        }

        // Audio
        foreach (var t in job.AudioTracks)
        {
            var ac = OutputAudioCodec(t);
            var label = $"Audio {Languages.DisplayName(t.Language)} {CodecNames.Display(ac)}";
            if (p.SupportsAudio(ac))
                r.Add(new(CheckLevel.Ok, $"{label}: {(p.IsGeneric ? "lo admite cualquier tele" : "la tele lo admite")}."));
            else if (ac is "eac3" or "opus" or "flac" && !p.IsGeneric)
                r.Add(new(CheckLevel.Warning, $"{label}: la tele no lo anuncia, aunque puede que funcione. Lo seguro es AAC o AC3."));
            else
                r.Add(new(CheckLevel.Problem, $"{label}: {(p.IsGeneric ? "muchas teles no lo admiten" : "la tele no lo anuncia")} (sonaría sin audio o no se reproduciría). Conviértelo a AAC o AC3."));
        }

        // Subtítulos como pista
        if (job.Subtitles.Keep.Count > 0 && c is ContainerKind.Mp4 or ContainerKind.Mov)
            r.Add(new(CheckLevel.Warning, "Subtítulos como pista: al enviar por DLNA muchas teles no los muestran. Si no aparecen, quémalos en la imagen (pestaña Subtítulos)."));

        return r;
    }

    /// <summary>Ajusta el trabajo para que la tele lo acepte. Devuelve lo que se ha cambiado.</summary>
    public static List<string> Adapt(ConversionJob job, DeviceProfile p)
    {
        var changes = new List<string>();

        // Contenedor: primero los que Windows enviará sin problemas.
        if (!p.AcceptsFromWindows(job.Container))
        {
            var target = new[] { ContainerKind.Mp4, ContainerKind.Mkv, ContainerKind.Mov }.FirstOrDefault(p.AcceptsFromWindows, ContainerKind.Mp4);
            changes.Add($"Contenedor {ContainerRules.DisplayName(job.Container)} → {ContainerRules.DisplayName(target)}");
            job.Container = target;
            if (!string.IsNullOrWhiteSpace(job.OutputPath)) job.OutputPath = Path.ChangeExtension(job.OutputPath, ContainerRules.Extension(target));
        }

        // Vídeo
        var v = job.Source.Video;
        if (v is not null)
        {
            var best = p.SupportsVideo("hevc") && ContainerRules.CanEncodeVideo(job.Container, VideoCodec.Hevc) ? VideoCodec.Hevc : VideoCodec.H264;
            if (job.Video.Mode == VideoMode.Copy && (!p.SupportsVideo(v.Codec) || !ContainerRules.CanHoldVideo(job.Container, v.Codec)))
            {
                job.Video.Mode = VideoMode.Encode;
                job.Video.Codec = best;
                job.Video.Quality = JobPlanner.RecommendedQuality(best, job.Video.Backend);
                changes.Add($"Vídeo {CodecNames.Display(v.Codec)} → recodificar a {JobPlanner.CodecLabel(best)}");
            }
            else if (job.Video.Mode == VideoMode.Encode && OutputVideoCodec(job) is { } oc && !p.SupportsVideo(oc))
            {
                changes.Add($"Códec {JobPlanner.CodecLabel(job.Video.Codec)} → {JobPlanner.CodecLabel(best)}");
                job.Video.Codec = best;
                job.Video.Quality = JobPlanner.RecommendedQuality(best, job.Video.Backend);
            }
        }

        // Audio
        var allowed = ContainerRules.AllowedAudioCodecs(job.Container);
        foreach (var t in job.AudioTracks)
        {
            var ac = OutputAudioCodec(t);
            if (p.SupportsAudio(ac) && (t.Codec != AudioCodecChoice.Copy || ContainerRules.CanHoldAudio(job.Container, t.SourceCodec)) && allowed.Contains(t.Codec)) continue;
            // Una pista copiada con sonido envolvente (para una barra de sonido) se pasa a AC3 5.1; el resto, a AAC estéreo.
            var surround = t.Codec == AudioCodecChoice.Copy && t.SourceChannels >= 6 && p.SupportsAudio("ac3") && allowed.Contains(AudioCodecChoice.Ac3_51);
            var nc = surround ? AudioCodecChoice.Ac3_51
                : p.SupportsAudio("aac") && allowed.Contains(AudioCodecChoice.AacStereo) ? AudioCodecChoice.AacStereo
                : p.SupportsAudio("ac3") && allowed.Contains(AudioCodecChoice.Ac3_51) ? AudioCodecChoice.Ac3_51
                : AudioCodecChoice.Mp3Stereo;
            changes.Add($"Audio {Languages.DisplayName(t.Language)} {CodecNames.Display(ac)} → {ContainerRules.AudioCodecName(nc)}");
            t.Codec = nc;
            t.BitrateKbps = ContainerRules.DefaultBitrate(nc);
        }

        return changes;
    }
}
