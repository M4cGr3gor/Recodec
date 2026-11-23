using System.Globalization;
using Recodec.Core.Media;

namespace Recodec.Core.Conversion;

/// <summary>Traduce un <see cref="ConversionJob"/> a los argumentos de ffmpeg.</summary>
public static class CommandBuilder
{
    private static string Inv(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
    private static string Ts(TimeSpan t) => t.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture);

    private static readonly string[] X26xPresets = { "veryfast", "faster", "medium", "slow", "slower" };

    public static List<string> Build(ConversionJob job)
    {
        var info = job.Source;
        var v = info.Video;
        var a = new List<string> { "-hide_banner", "-nostdin", "-y", "-loglevel", "warning", "-progress", "pipe:1", "-nostats" };

        // ---------- Entradas ----------
        void Seek() { if (job.TrimStart is { } s) { a.Add("-ss"); a.Add(Ts(s)); } }

        Seek();
        a.Add("-i"); a.Add(info.Path);
        var nextInput = 1;

        var externalAudioInput = new Dictionary<AudioTrackJob, int>();
        foreach (var t in job.AudioTracks.Where(t => t.IsExternal))
        {
            Seek();
            if (t.DelayMs != 0) { a.Add("-itsoffset"); a.Add(Inv(t.DelayMs / 1000.0)); }
            a.Add("-i"); a.Add(t.ExternalPath!);
            externalAudioInput[t] = nextInput++;
        }

        var burn = job.Subtitles;
        StreamInfo? burnStream = burn.BurnKind == BurnSourceKind.Internal ? info.Streams.FirstOrDefault(s => s.Index == burn.BurnStreamIndex) : null;
        var burnExternalBitmap = burn.BurnKind == BurnSourceKind.External && IsBitmapFile(burn.BurnExternalPath);
        var burnBitmap = burnStream?.IsBitmapSubtitle == true || burnExternalBitmap;
        var burnInput = -1;
        if (burnExternalBitmap)
        {
            Seek();
            a.Add("-i"); a.Add(burn.BurnExternalPath!);
            burnInput = nextInput++;
        }

        // ---------- Vídeo ----------
        var encode = v is not null && job.Video.Mode == VideoMode.Encode;
        if (v is not null)
        {
            if (!encode)
            {
                a.AddRange(new[] { "-map", $"0:{v.Index}", "-c:v", "copy" });
                if (job.Container is ContainerKind.Mp4 or ContainerKind.Mov && v.Codec == "hevc") a.AddRange(new[] { "-tag:v", "hvc1" });
            }
            else
            {
                var chain = VideoFilters(job, v, burnBitmap);
                if (burnBitmap)
                {
                    var subRef = burnInput >= 0 ? $"{burnInput}:s:0" : $"0:{burnStream!.Index}";
                    // Los subtítulos de imagen se escalan al ancho del vídeo y se colocan antes de recortar/escalar.
                    var graph = $"[{subRef}][0:{v.Index}]scale2ref=w=rw:h=rw*ih/iw[sub][base];" +
                                "[base][sub]overlay=x=(W-w)/2:y='if(gt(h,H),H-h,(H-h)/2)':eof_action=pass:repeatlast=0:format=auto";
                    graph += chain.Count > 0 ? "," + string.Join(",", chain) + "[vout]" : "[vout]";
                    a.AddRange(new[] { "-filter_complex", graph, "-map", "[vout]" });
                }
                else
                {
                    a.AddRange(new[] { "-map", $"0:{v.Index}" });
                    if (chain.Count > 0) a.AddRange(new[] { "-vf", string.Join(",", chain) });
                }
                a.AddRange(VideoEncoderArgs(job, v));
            }
        }

        // ---------- Audio ----------
        for (var k = 0; k < job.AudioTracks.Count; k++)
        {
            var t = job.AudioTracks[k];
            a.Add("-map");
            a.Add(t.IsExternal ? $"{externalAudioInput[t]}:a:{t.ExternalStreamIndex}" : $"0:{t.StreamIndex}");

            if (t.Codec == AudioCodecChoice.Copy)
            {
                a.AddRange(new[] { $"-c:a:{k}", "copy" });
            }
            else
            {
                a.AddRange(new[] { $"-c:a:{k}", AudioEncoder(t.Codec) });
                if (ContainerRules.UsesBitrate(t.Codec)) a.AddRange(new[] { $"-b:a:{k}", $"{t.BitrateKbps}k" });
                var outCh = ContainerRules.OutputChannels(t.Codec, t.SourceChannels);
                var filter = AudioFilterBuilder.Build(t, job.Volume);
                if (filter is not null) a.AddRange(new[] { $"-filter:a:{k}", filter });
                a.AddRange(new[] { $"-ac:a:{k}", outCh.ToString(CultureInfo.InvariantCulture), $"-ar:a:{k}", "48000" });
            }
            a.AddRange(new[] { $"-metadata:s:a:{k}", $"language={Languages.Normalize(t.Language)}" });
            a.AddRange(new[] { $"-metadata:s:a:{k}", $"title={t.Title ?? AutoAudioTitle(t)}" });
            a.AddRange(new[] { $"-disposition:a:{k}", k == 0 ? "default" : "0" });
        }

        // ---------- Subtítulos como pista ----------
        var j = 0;
        var forcedDefaultDone = false;
        foreach (var idx in job.Subtitles.Keep)
        {
            var s = info.Streams.FirstOrDefault(x => x.Index == idx);
            if (s is null) continue;
            var codec = ContainerRules.SoftSubtitleCodec(job.Container, s);
            if (codec is null) continue;
            a.AddRange(new[] { "-map", $"0:{s.Index}", $"-c:s:{j}", codec });
            a.AddRange(new[] { $"-metadata:s:s:{j}", $"language={Languages.Normalize(s.Language)}" });
            a.AddRange(new[] { $"-disposition:s:{j}", !s.IsForced ? "0" : forcedDefaultDone || job.Subtitles.Burns ? "forced" : "default+forced" });
            if (s.IsForced) forcedDefaultDone = true;
            j++;
        }

        // ---------- General ----------
        if (job.Container == ContainerKind.Mkv && job.TrimStart is null) a.AddRange(new[] { "-map", "0:t?" });
        a.AddRange(new[] { "-map_metadata", "0", "-map_chapters", job.KeepChapters && job.TrimStart is null ? "0" : "-1" });
        if (job.TrimDuration is { } d) { a.Add("-t"); a.Add(Ts(d)); }
        if (job.Container is ContainerKind.Mp4 or ContainerKind.Mov && job.FastStart) a.AddRange(new[] { "-movflags", "+faststart" });
        a.AddRange(new[] { "-max_muxing_queue_size", "4096" });
        a.Add(job.OutputPath);
        return a;
    }

    /// <summary>
    /// Clip corto para comprobar la sincronización de una pista de audio externa: vídeo a 720p (rápido de crear y
    /// reproducible en cualquier sitio) y solo esa pista, con el retraso indicado. No aplica la corrección de volumen.
    /// </summary>
    public static List<string> BuildSyncPreview(MediaInfo info, AudioTrackJob track, int delayMs, TimeSpan start, TimeSpan duration, string output, bool gpu)
    {
        if (!track.IsExternal) throw new ArgumentException("La pista no es externa.", nameof(track));
        var v = info.Video ?? throw new InvalidOperationException("El archivo no tiene vídeo.");
        var a = new List<string> { "-hide_banner", "-nostdin", "-y", "-loglevel", "warning", "-progress", "pipe:1", "-nostats" };
        a.AddRange(new[] { "-ss", Ts(start), "-i", info.Path });

        // Momento t del clip = vídeo en (start + t) = audio en (start + t - retraso).
        var audioStart = start.TotalSeconds - delayMs / 1000.0;
        string? pad = null;
        if (audioStart >= 0) a.AddRange(new[] { "-ss", Inv(audioStart) });
        else pad = $"adelay=delays={Math.Round(-audioStart * 1000)}:all=1";
        a.AddRange(new[] { "-i", track.ExternalPath! });

        var vf = new List<string>();
        if (v.IsInterlaced) vf.Add("bwdif=mode=send_frame:deint=interlaced");
        vf.Add("scale=w=-2:h='min(720,ih)':flags=bicubic");
        if (v.Hdr != HdrKind.None)
            vf.Add("zscale=t=linear:npl=100,format=gbrpf32le,zscale=p=bt709,tonemap=tonemap=hable:desat=0,zscale=t=bt709:m=bt709:r=tv");
        vf.Add("format=yuv420p");

        a.AddRange(new[] { "-map", $"0:{v.Index}", "-vf", string.Join(",", vf) });
        a.AddRange(gpu
            ? new[] { "-c:v", "h264_nvenc", "-preset", "p2", "-rc", "vbr", "-cq", "27", "-b:v", "0" }
            : new[] { "-c:v", "libx264", "-preset", "veryfast", "-crf", "24" });
        a.AddRange(new[] { "-map", $"1:a:{track.ExternalStreamIndex}", "-c:a", "aac", "-b:a", "192k", "-ac", "2", "-ar", "48000" });
        if (pad is not null) a.AddRange(new[] { "-af", pad });
        a.AddRange(new[] { "-sn", "-dn", "-map_metadata", "-1", "-map_chapters", "-1", "-t", Ts(duration), "-movflags", "+faststart", output });
        return a;
    }

    /// <summary>Filtros de vídeo, en el orden en que se aplican.</summary>
    private static List<string> VideoFilters(ConversionJob job, StreamInfo v, bool bitmapBurn)
    {
        var f = new List<string>();
        var vs = job.Video;

        if (vs.Deinterlace == DeinterlaceMode.On) f.Add("bwdif=mode=send_frame:deint=all");
        else if (vs.Deinterlace == DeinterlaceMode.Auto && v.IsInterlaced) f.Add("bwdif=mode=send_frame:deint=interlaced");

        // FFmpeg ya ha girado la imagen (vídeos de móvil): todo se calcula con el tamaño tal como se ve.
        if (JobPlanner.EffectiveCrop(job) is { } c)
            f.Add($"crop={c.Width}:{c.Height}:{c.X}:{c.Y}");

        var (cw, ch) = JobPlanner.CroppedSize(job);
        var (ow, oh) = JobPlanner.OutputSize(job);
        if (ow != cw || oh != ch)
            f.Add($"scale={ow}:{oh}:flags=lanczos");

        if (DenoiseFilter(vs.Denoise, v) is { } dn) f.Add(dn);

        if (NeedsToneMap(job, v))
        {
            f.Add("zscale=t=linear:npl=100,format=gbrpf32le,zscale=p=bt709,tonemap=tonemap=hable:desat=0,zscale=t=bt709:m=bt709:r=tv,format=yuv420p");
        }

        var burn = job.Subtitles;
        if (!bitmapBurn && burn.Burns)
        {
            var shift = job.TrimStart is { } s ? Ts(s) : null;
            // Al empezar a mitad de la película, el decodificador entrega fotogramas anteriores (con tiempo negativo)
            // que FFmpeg descartaría después; como aquí se desplaza el tiempo, hay que quitarlos antes.
            if (shift is not null) f.Insert(0, "trim=start=0");
            if (shift is not null) f.Add($"setpts=PTS+{shift}/TB");
            if (burn.BurnKind == BurnSourceKind.Internal)
                f.Add($"subtitles=filename='{EscapeFilterPath(job.Source.Path)}':si={job.Source.SubtitleRelativeIndex(burn.BurnStreamIndex)}");
            else
                f.Add($"subtitles=filename='{EscapeFilterPath(burn.BurnExternalPath!)}'");
            if (shift is not null) f.Add("setpts=PTS-STARTPTS");
        }
        return f;
    }

    /// <summary>
    /// Filtro de reducción de ruido. hqdn3d (con valores habituales para cada nivel) es rápido y conserva los 10 bits;
    /// NLMeans elimina mejor el grano pero es muy lento y solo trabaja en 8 bits, así que en vídeos de 10 bits
    /// (HDR) el nivel máximo usa un hqdn3d más fuerte.
    /// </summary>
    public static string? DenoiseFilter(DenoiseMode mode, StreamInfo v) => mode switch
    {
        DenoiseMode.Light => "hqdn3d=2:1:2:3",
        DenoiseMode.Medium => "hqdn3d=3:2:2:3",
        DenoiseMode.Strong => "hqdn3d=7:7:5:5",
        DenoiseMode.Max when v.BitDepth > 8 => "hqdn3d=10:10:8:8",
        DenoiseMode.Max => "nlmeans=s=3:p=7:r=9",
        _ => null,
    };

    public static bool NeedsToneMap(ConversionJob job, StreamInfo v) =>
        job.Video.Mode == VideoMode.Encode && v.Hdr != HdrKind.None && ContainerRules.IsSdrOnly(job.Video.Codec);

    private static IEnumerable<string> VideoEncoderArgs(ConversionJob job, StreamInfo v)
    {
        var vs = job.Video;
        var q = vs.Quality.ToString(CultureInfo.InvariantCulture);
        var speed = Math.Clamp(vs.Speed, 0, 4);
        var keepHdr = v.Hdr != HdrKind.None && !NeedsToneMap(job, v);
        var args = new List<string>();

        switch (vs.Codec, vs.Backend)
        {
            case (_, not EncoderBackend.Cpu) when ContainerRules.HardwareCodecs(vs.Backend).Contains(vs.Codec):
                args.AddRange(HardwareEncoderArgs(vs.Codec, vs.Backend, vs.Quality, speed));
                break;
            case (VideoCodec.Hevc, _):
                args.AddRange(new[] { "-c:v", "libx265", "-preset", X26xPresets[speed], "-crf", q, "-pix_fmt", "yuv420p10le" });
                var x265 = new List<string> { "log-level=error" };
                if (keepHdr)
                {
                    x265.Add("hdr10-opt=1");
                    x265.Add("repeat-headers=1");
                    x265.Add($"colorprim=bt2020:transfer={(v.Hdr == HdrKind.Hlg ? "arib-std-b67" : "smpte2084")}:colormatrix=bt2020nc");
                    if (v.Hdr != HdrKind.Hlg && job.Source.HdrMetadata is { } md)
                    {
                        x265.Add("hdr10=1");
                        if (md.MasterDisplay is not null) x265.Add($"master-display={md.MasterDisplay}");
                        if (md.MaxCll is not null) x265.Add($"max-cll={md.MaxCll}");
                    }
                }
                args.AddRange(new[] { "-x265-params", string.Join(":", x265) });
                break;
            case (VideoCodec.H264, _):
                args.AddRange(new[] { "-c:v", "libx264", "-preset", X26xPresets[speed], "-crf", q, "-profile:v", "high", "-pix_fmt", "yuv420p" });
                break;
            case (VideoCodec.Av1, _):
                args.AddRange(new[] { "-c:v", "libaom-av1", "-crf", q, "-b:v", "0", "-cpu-used", (8 - speed).ToString(CultureInfo.InvariantCulture),
                    "-row-mt", "1", "-pix_fmt", "yuv420p10le" });
                break;
            case (VideoCodec.Vp9, _):
                // VP9 perfil 2 (10 bits) para conservar el HDR; perfil 0 (8 bits) para SDR.
                args.AddRange(new[] { "-c:v", "libvpx-vp9", "-crf", q, "-b:v", "0", "-deadline", "good",
                    "-cpu-used", (5 - speed).ToString(CultureInfo.InvariantCulture), "-row-mt", "1", "-tile-columns", "2",
                    "-auto-alt-ref", "1", "-lag-in-frames", "25" });
                args.AddRange(keepHdr ? new[] { "-profile:v", "2", "-pix_fmt", "yuv420p10le" } : new[] { "-pix_fmt", "yuv420p" });
                break;
            case (VideoCodec.Xvid, _):
                // MPEG-4 Part 2 con la etiqueta XVID: lo reconocen los reproductores DivX/XviD.
                args.AddRange(new[] { "-c:v", "libxvid", "-q:v", q, "-me_quality", (2 + speed).ToString(CultureInfo.InvariantCulture),
                    "-pix_fmt", "yuv420p", "-vtag", "XVID" });
                break;
        }

        if (keepHdr)
        {
            args.AddRange(new[] { "-color_primaries", "bt2020", "-color_trc", v.Hdr == HdrKind.Hlg ? "arib-std-b67" : "smpte2084", "-colorspace", "bt2020nc" });
        }
        else if (NeedsToneMap(job, v))
        {
            args.AddRange(new[] { "-color_primaries", "bt709", "-color_trc", "bt709", "-colorspace", "bt709" });
        }

        if (job.Container is ContainerKind.Mp4 or ContainerKind.Mov && vs.Codec == VideoCodec.Hevc) args.AddRange(new[] { "-tag:v", "hvc1" });
        return args;
    }

    /// <summary>
    /// Opciones de los codificadores de tarjeta gráfica. Se usan también en la prueba de arranque, así que si alguna
    /// opción no la admite la gráfica del usuario, ese codificador simplemente no se ofrece.
    /// HEVC siempre en 10 bits (mejor compresión y necesario para el HDR); H.264 en 8 bits (lo que admiten las teles).
    /// </summary>
    public static IEnumerable<string> HardwareEncoderArgs(VideoCodec codec, EncoderBackend backend, int quality, int speed)
    {
        var q = quality.ToString(CultureInfo.InvariantCulture);
        speed = Math.Clamp(speed, 0, 4);
        var tenBit = codec != VideoCodec.H264;
        var pixFmt = tenBit ? "p010le" : (backend == EncoderBackend.Gpu ? "yuv420p" : "nv12");
        string Profile() => codec == VideoCodec.Hevc ? "main10" : "high";
        var a = new List<string>();
        switch (backend)
        {
            case EncoderBackend.Gpu:
                // NVIDIA: calidad constante (VBR con -cq), presets p3 (rápido) .. p7 (lento).
                a.AddRange(new[] { "-c:v", codec switch { VideoCodec.Hevc => "hevc_nvenc", VideoCodec.H264 => "h264_nvenc", _ => "av1_nvenc" },
                    "-preset", "p" + (3 + speed), "-tune", "hq", "-rc", "vbr", "-cq", q, "-b:v", "0" });
                if (codec != VideoCodec.Av1) a.AddRange(new[] { "-spatial-aq", "1", "-temporal-aq", "1" });
                a.AddRange(new[] { "-rc-lookahead", "32" });
                if (codec != VideoCodec.Av1) a.AddRange(new[] { "-profile:v", Profile() });
                break;
            case EncoderBackend.Qsv:
                // Intel Quick Sync: calidad constante inteligente (ICQ) con -global_quality; presets veryfast .. veryslow.
                a.AddRange(new[] { "-c:v", codec == VideoCodec.Hevc ? "hevc_qsv" : "h264_qsv",
                    "-preset", new[] { "veryfast", "faster", "medium", "slow", "veryslow" }[speed], "-global_quality", q, "-profile:v", Profile() });
                break;
            case EncoderBackend.Amf:
                // AMD: cuantización constante (el modo más predecible en todas las generaciones de tarjetas).
                a.AddRange(new[] { "-c:v", codec == VideoCodec.Hevc ? "hevc_amf" : "h264_amf",
                    "-quality", speed <= 1 ? "speed" : speed == 2 ? "balanced" : "quality",
                    "-rc", "cqp", "-qp_i", q, "-qp_p", q });
                if (codec == VideoCodec.H264) a.AddRange(new[] { "-qp_b", q });
                a.AddRange(new[] { "-profile:v", Profile() });
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(backend));
        }
        a.AddRange(new[] { "-pix_fmt", pixFmt });
        return a;
    }

    private static string AudioEncoder(AudioCodecChoice c) => c switch
    {
        AudioCodecChoice.AacStereo or AudioCodecChoice.Aac51 => "aac",
        AudioCodecChoice.Ac3_51 => "ac3",
        AudioCodecChoice.Eac3_51 => "eac3",
        AudioCodecChoice.OpusStereo => "libopus",
        AudioCodecChoice.Mp3Stereo => "libmp3lame",
        AudioCodecChoice.Flac => "flac",
        _ => "copy",
    };

    public static string AutoAudioTitle(AudioTrackJob t)
    {
        var lang = Languages.DisplayName(t.Language);
        if (t.Codec == AudioCodecChoice.Copy) return $"{lang} · original";
        var vol = t.Volume switch { VolumeMode.Night => " · modo noche", VolumeMode.Soft => " · normalizado", _ => "" };
        return $"{lang} · {ContainerRules.AudioCodecName(t.Codec)}{vol}";
    }

    private static bool IsBitmapFile(string? path) =>
        path is not null && Path.GetExtension(path).Equals(".sup", StringComparison.OrdinalIgnoreCase);

    /// <summary>Escapa una ruta de Windows para usarla dentro de un filtro de FFmpeg (entre comillas simples).</summary>
    public static string EscapeFilterPath(string path) =>
        path.Replace("\\", "/").Replace(":", "\\:").Replace("'", "'\\\\\\''").Replace("[", "\\[").Replace("]", "\\]").Replace(",", "\\,");
}
