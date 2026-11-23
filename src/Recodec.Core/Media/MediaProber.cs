using System.Globalization;
using System.Text.Json;
using Recodec.Core.Ffmpeg;

namespace Recodec.Core.Media;

/// <summary>Analiza un archivo con ffprobe.</summary>
public sealed class MediaProber
{
    private readonly FfmpegTools _tools;
    public MediaProber(FfmpegTools tools) => _tools = tools;

    public async Task<MediaInfo> ProbeAsync(string path, CancellationToken ct = default)
    {
        var r = await ProcessRunner.RunAsync(_tools.Ffprobe, new[]
        {
            "-v", "error", "-print_format", "json", "-show_format", "-show_streams", "-show_chapters", path,
        }, ct: ct);
        if (r.ExitCode != 0)
            throw new InvalidOperationException("No se ha podido analizar el archivo. " + FirstLine(r.StdErr));

        var info = Parse(path, r.StdOut);
        SubtitleAnalysis.Classify(info);
        var v = info.Video;
        if (v is not null && (v.Hdr == HdrKind.Hdr10 || v.Hdr == HdrKind.DolbyVision))
        {
            try { info.HdrMetadata = await ProbeHdrAsync(path, ct); } catch (OperationCanceledException) { throw; } catch { /* opcional */ }
        }
        return info;
    }

    public static MediaInfo Parse(string path, string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var streams = new List<StreamInfo>();

        if (root.TryGetProperty("streams", out var ss))
        {
            foreach (var s in ss.EnumerateArray())
            {
                var type = Str(s, "codec_type");
                var kind = type switch
                {
                    "video" => StreamKind.Video,
                    "audio" => StreamKind.Audio,
                    "subtitle" => StreamKind.Subtitle,
                    "attachment" => StreamKind.Attachment,
                    "data" => StreamKind.Data,
                    _ => StreamKind.Unknown,
                };
                var disp = s.TryGetProperty("disposition", out var d) ? d : default;
                var tags = s.TryGetProperty("tags", out var t) ? t : default;

                int? dvProfile = null;
                var rotation = Int(tags, "rotate", isTag: true);
                if (s.TryGetProperty("side_data_list", out var sdl))
                {
                    foreach (var sd in sdl.EnumerateArray())
                    {
                        var sdType = Str(sd, "side_data_type") ?? "";
                        if (sdType.Contains("DOVI", StringComparison.OrdinalIgnoreCase))
                            dvProfile = Int(sd, "dv_profile");
                        else if (sdType.Contains("Display Matrix", StringComparison.OrdinalIgnoreCase))
                            rotation = Int(sd, "rotation");
                    }
                }

                streams.Add(new StreamInfo
                {
                    Index = Int(s, "index"),
                    Kind = kind,
                    Codec = Str(s, "codec_name") ?? "desconocido",
                    CodecLongName = Str(s, "codec_long_name") ?? "",
                    Profile = Str(s, "profile"),
                    Language = Tag(tags, "language"),
                    Title = Tag(tags, "title"),
                    IsDefault = Int(disp, "default") == 1,
                    EventCount = kind == StreamKind.Subtitle ? SubtitleEventsFromFile(s, tags, Str(s, "codec_name")) : null,
                    ForcedFlag = Int(disp, "forced") == 1 || (Tag(tags, "title")?.Contains("forz", StringComparison.OrdinalIgnoreCase) ?? false)
                               || (Tag(tags, "title")?.Contains("forced", StringComparison.OrdinalIgnoreCase) ?? false),
                    AttachedPic = Int(disp, "attached_pic") == 1,
                    BitRate = Long(s, "bit_rate") is > 0 and var br ? br : ParseBps(Tag(tags, "BPS")),
                    Width = Int(s, "width"),
                    Height = Int(s, "height"),
                    PixelFormat = Str(s, "pix_fmt"),
                    ColorTransfer = Str(s, "color_transfer"),
                    ColorPrimaries = Str(s, "color_primaries"),
                    ColorSpace = Str(s, "color_space"),
                    FrameRate = Rational(Str(s, "avg_frame_rate")) is > 0 and var fr ? fr : Rational(Str(s, "r_frame_rate")),
                    FieldOrder = Str(s, "field_order"),
                    DolbyVisionProfile = dvProfile,
                    Rotation = ((rotation % 360) + 360) % 360,
                    Channels = Int(s, "channels"),
                    ChannelLayout = Str(s, "channel_layout"),
                    SampleRate = Int(s, "sample_rate"),
                });
            }
        }

        var fmt = root.TryGetProperty("format", out var f) ? f : default;
        var ftags = fmt.ValueKind == JsonValueKind.Object && fmt.TryGetProperty("tags", out var ft) ? ft : default;
        var chapters = root.TryGetProperty("chapters", out var ch) ? ch.GetArrayLength() : 0;
        var seconds = Double(fmt, "duration");

        return new MediaInfo
        {
            Path = path,
            FormatName = Str(fmt, "format_name") ?? "",
            FormatLongName = Str(fmt, "format_long_name") ?? "",
            SizeBytes = Long(fmt, "size"),
            Duration = seconds > 0 ? TimeSpan.FromSeconds(seconds) : TimeSpan.Zero,
            BitRate = Long(fmt, "bit_rate"),
            Title = Tag(ftags, "title"),
            ChapterCount = chapters,
            Streams = streams,
        };
    }

    /// <summary>Lee del primer fotograma los metadatos HDR10 (masterizado y brillo máximo).</summary>
    private async Task<HdrMetadata?> ProbeHdrAsync(string path, CancellationToken ct)
    {
        var r = await ProcessRunner.RunAsync(_tools.Ffprobe, new[]
        {
            "-v", "error", "-select_streams", "v:0", "-read_intervals", "%+#1", "-show_frames",
            "-show_entries", "frame=side_data_list", "-print_format", "json", path,
        }, ct: ct);
        if (r.ExitCode != 0) return null;

        using var doc = JsonDocument.Parse(r.StdOut);
        string? master = null, cll = null;
        if (!doc.RootElement.TryGetProperty("frames", out var frames)) return null;
        foreach (var fr in frames.EnumerateArray())
        {
            if (!fr.TryGetProperty("side_data_list", out var list)) continue;
            foreach (var sd in list.EnumerateArray())
            {
                var type = Str(sd, "side_data_type") ?? "";
                if (type.StartsWith("Mastering display", StringComparison.OrdinalIgnoreCase))
                {
                    // x265 espera cromaticidad en unidades de 0,00002 y luminancia en 0,0001 cd/m².
                    long C(string k) => (long)Math.Round(Rational(Str(sd, k)) * 50000);
                    long L(string k) => (long)Math.Round(Rational(Str(sd, k)) * 10000);
                    master = $"G({C("green_x")},{C("green_y")})B({C("blue_x")},{C("blue_y")})R({C("red_x")},{C("red_y")})" +
                             $"WP({C("white_point_x")},{C("white_point_y")})L({L("max_luminance")},{L("min_luminance")})";
                }
                else if (type.StartsWith("Content light level", StringComparison.OrdinalIgnoreCase))
                {
                    cll = $"{Int(sd, "max_content")},{Int(sd, "max_average")}";
                }
            }
        }
        return master is null && cll is null ? null : new HdrMetadata { MasterDisplay = master, MaxCll = cll };
    }

    // --- utilidades JSON ---
    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v)
            ? v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString()
            : null;

    private static string? Tag(JsonElement tags, string name)
    {
        if (tags.ValueKind != JsonValueKind.Object) return null;
        foreach (var p in tags.EnumerateObject())
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) return p.Value.GetString();
        return null;
    }

    private static int Int(JsonElement e, string name)
    {
        var s = Str(e, name);
        return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;
    }

    private static int Int(JsonElement tags, string name, bool isTag) => int.TryParse(Tag(tags, name), out var v) ? v : 0;

    private static long Long(JsonElement e, string name)
    {
        var s = Str(e, name);
        return long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;
    }

    private static double Double(JsonElement e, string name)
    {
        var s = Str(e, name);
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;
    }

    /// <summary>
    /// Líneas de un subtítulo si el archivo ya lo dice: MKV hechos con mkvmerge (etiqueta NUMBER_OF_FRAMES)
    /// o MP4 (nb_frames). Si no, null y se cuentan después.
    /// </summary>
    private static int? SubtitleEventsFromFile(JsonElement s, JsonElement tags, string? codec)
    {
        // En MP4 el texto incluye muestras vacías entre subtítulo y subtítulo: ese número no sirve, se cuentan después.
        if (codec == "mov_text") return null;
        long n = Long(s, "nb_frames");
        if (n <= 0 && tags.ValueKind == JsonValueKind.Object)
            foreach (var p in tags.EnumerateObject())
                if (p.Name.StartsWith("NUMBER_OF_FRAMES", StringComparison.OrdinalIgnoreCase) && long.TryParse(p.Value.GetString(), out var v)) { n = v; break; }
        if (n <= 0) return null;
        return codec == "hdmv_pgs_subtitle" ? (int)((n + 1) / 2) : (int)n;
    }

    private static long ParseBps(string? s) =>
        long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;

    public static double Rational(string? s)
    {
        if (string.IsNullOrEmpty(s)) return 0;
        var parts = s.Split('/');
        if (parts.Length == 2 &&
            double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var a) &&
            double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var b) && b != 0)
            return a / b;
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ? x : 0;
    }

    private static string FirstLine(string s) => s.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "";
}
