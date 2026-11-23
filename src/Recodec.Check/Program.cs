// Banco de pruebas del núcleo de Recodec: analiza archivos, genera comandos y ejecuta conversiones reales.
// Uso: Recodec.Check <carpeta-de-muestras> <carpeta-de-salida>
using System.Globalization;
using System.Text.RegularExpressions;
using Recodec.Core.Conversion;
using Recodec.Core.Devices;
using Recodec.Core.Ffmpeg;
using Recodec.Core.Media;
using Recodec.Core.Settings;

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
var samples = args.Length > 0 ? args[0] : throw new ArgumentException("Falta la carpeta de muestras");
var outDir = args.Length > 1 ? args[1] : Path.Combine(samples, "out");
var only = args.Length > 2 ? args[2] : null;
Directory.CreateDirectory(outDir);

if (args.Length > 0 && args[0] == "update")
{
    // Prueba del actualizador en una carpeta aparte: Recodec.Check update <carpeta>
    var sandbox = FfmpegTools.At(args[1]);
    var up = new FfmpegUpdater(sandbox);
    var chk = await up.CheckAsync();
    Console.WriteLine($"Instalada: {chk.Installed ?? "ninguna"} · Última: {chk.Latest} · Hay actualización: {chk.UpdateAvailable}");
    Directory.CreateDirectory(sandbox.FfmpegDir);
    File.WriteAllText(Path.Combine(sandbox.FfmpegDir, "ffplay.exe"), "sobra");
    var last = -1;
    var v = await up.InstallLatestAsync(new Progress<double>(p => { if ((int)p / 25 != last) { last = (int)p / 25; Console.WriteLine($"  descarga {p:0}%"); } }), m => Console.WriteLine("  " + m));
    Console.WriteLine("Instalada ahora: " + v);
    Console.WriteLine("Archivos: " + string.Join(", ", Directory.GetFileSystemEntries(sandbox.FfmpegDir).Select(Path.GetFileName)));
    Console.WriteLine("¿Más nueva 9.2 > 9.1? " + FfmpegUpdater.IsNewer("9.2", "9.1") + " · ¿8.0 > 9.1? " + FfmpegUpdater.IsNewer("8.0", "9.1") + " · ¿9.1.1 > 9.1? " + FfmpegUpdater.IsNewer("9.1.1", "9.1"));
    return 0;
}

if (args.Length > 0 && args[0] == "scan")
{
    // Busca reproductores DLNA y guarda su lista de formatos sin procesar: Recodec.Check scan <carpeta>
    var found = await DlnaScanner.ScanAsync(TimeSpan.FromSeconds(4), Console.WriteLine, default, (d, raw) =>
    {
        var file = Path.Combine(args[1], string.Concat(d.DisplayName.Split(Path.GetInvalidFileNameChars())) + ".txt");
        File.WriteAllText(file, raw);
        Console.WriteLine($"{d.DisplayName}: {raw.Split(',').Length} entradas → {file}");
    });
    return found.Count > 0 ? 0 : 1;
}

var tools = FfmpegTools.Locate();
Console.WriteLine($"AppRoot: {tools.AppRoot}  ffmpeg: {await tools.GetVersionAsync()}  NVENC: {await tools.HasNvencAsync()}");
var prober = new MediaProber(tools);
var preview = new PreviewService(tools);
var converter = new Converter(tools);
var failures = 0;

void Check(bool ok, string what)
{
    Console.WriteLine((ok ? "  OK   " : "  FALLO ") + what);
    if (!ok) failures++;
}

var hdr = await prober.ProbeAsync(Path.Combine(samples, "sample_hdr_51.mkv"));
Console.WriteLine($"\n== {hdr.FileName}: {hdr.Duration} · {hdr.Streams.Count} pistas · HDR meta: {hdr.HdrMetadata?.MasterDisplay} / {hdr.HdrMetadata?.MaxCll}");
foreach (var s in hdr.Streams) Console.WriteLine($"   #{s.Index} {s.Kind} {s.Describe()} {s.Width}x{s.Height} {s.ChannelLayout}");
Check(hdr.Video?.Hdr == HdrKind.Hdr10, "detecta HDR10");
Check(hdr.HdrMetadata?.MasterDisplay == "G(13250,34500)B(7500,3000)R(34000,16000)WP(15635,16450)L(10000000,1)", "lee master-display");
Check(hdr.HdrMetadata?.MaxCll == "1000,400", "lee max-cll");

var frame = await preview.GetFrameAsync(hdr, PreviewService.PreviewTime(hdr, 2));
Check(frame is { Length: > 1000 } && frame[1] == 'P', $"extrae fotograma PNG ({frame?.Length} bytes)");
var crop = await preview.DetectCropAsync(hdr);
Console.WriteLine($"   recorte detectado: {crop}");
Check(crop is { Width: 1920, Height: 800, X: 0, Y: 140 }, "detecta bandas negras 1920x800+0+140");

Check(new JobPlanner.Defaults().VolumeMode == VolumeMode.Off && AppSettings.Load(Path.Combine(outDir, "no_existe.json")).VolumeMode == VolumeMode.Off,
      "las pistas nuevas salen con el volumen \"Sin cambios\"");
var defaults = new JobPlanner.Defaults { PreferredLanguage = "spa", Container = ContainerKind.Mp4, VolumeMode = VolumeMode.Night };
var job = JobPlanner.CreateDefault(hdr, defaults);
Check(job.Video.Mode == VideoMode.Copy, "por defecto copia el vídeo HEVC en MP4");
Check(job.AudioTracks.Count == 1 && job.AudioTracks[0].StreamIndex == 1, "elige solo la pista en español");
Check(job.Subtitles.Keep.SequenceEqual(new[] { 3 }), "incluye los subtítulos en español");
Check(!job.Subtitles.Burns, "no quema subtítulos si el audio ya es español");

var eng = JobPlanner.CreateDefault(hdr, defaults with { PreferredLanguage = "eng" });
Check(eng.AudioTracks[0].StreamIndex == 2, "con idioma inglés elige la pista inglesa");

async Task<MediaInfo?> Run(string name, ConversionJob j)
{
    if (only is not null && !name.Contains(only)) return null;
    var rep = JobPlanner.Normalize(j);
    foreach (var x in rep.Adjustments.Concat(rep.Warnings)) Console.WriteLine("   · " + x);
    Console.WriteLine($"\n== {name}\n   {ProcessRunner.FormatCommandLine("ffmpeg", CommandBuilder.Build(j))}");
    double last = -1;
    var sw = System.Diagnostics.Stopwatch.StartNew();
    var errLines = new List<string>();
    var res = await converter.RunAsync(j, new Progress<ConversionProgress>(p => last = p.Percent), l => { lock (errLines) errLines.Add(l); }, CancellationToken.None);
    if (!res.Success) foreach (var l in errLines.Skip(1).TakeLast(8)) Console.WriteLine("   ffmpeg: " + l);
    Console.WriteLine($"   resultado: {(res.Success ? "bien" : "ERROR " + res.Error)} en {sw.Elapsed.TotalSeconds:0.0}s · progreso final {last:0}%");
    Check(res.Success && File.Exists(j.OutputPath), $"{name} termina");
    return res.Success ? await prober.ProbeAsync(j.OutputPath) : null;
}

// 1) Por defecto: copia vídeo, audio modo noche, MP4
var j1 = JobPlanner.CreateDefault(hdr, defaults);
j1.OutputPath = Path.Combine(outDir, "1_copia_noche.mp4");
var o1 = await Run("1_copia_noche", j1);
if (o1 is not null)
{
    Check(o1.Video?.Codec == "hevc" && o1.Video.Hdr == HdrKind.Hdr10, "1: vídeo HEVC HDR10 copiado");
    Check(o1.AudioStreams.Single().Channels == 2 && o1.AudioStreams.Single().Codec == "aac", "1: audio AAC estéreo");
    Check(o1.SubtitleStreams.Single().Codec == "mov_text", "1: subtítulo como mov_text");
}

// 2) HEVC por GPU con recorte, subtítulo de texto quemado y pista original añadida, MKV
var j2 = JobPlanner.CreateDefault(hdr, defaults with { Container = ContainerKind.Mkv });
j2.OutputPath = Path.Combine(outDir, "2_hevc_gpu_recorte_quemado.mkv");
j2.Video.Crop = crop;
j2.Subtitles.BurnKind = BurnSourceKind.Internal;
j2.Subtitles.BurnStreamIndex = 3;
j2.AudioTracks.Add(JobPlanner.TrackFromStream(hdr.Streams[1], AudioCodecChoice.Copy, VolumeMode.Off));
j2.TrimStart = TimeSpan.FromSeconds(1);
j2.TrimDuration = TimeSpan.FromSeconds(15);
var o2 = await Run("2_hevc_gpu_recorte_quemado", j2);
if (o2 is not null)
{
    Check(o2.Video is { Width: 1920, Height: 800 }, $"2: recortado a 1920x800 ({o2.Video?.Width}x{o2.Video?.Height})");
    Check(o2.Video?.Hdr == HdrKind.Hdr10 && o2.Video.BitDepth == 10, "2: mantiene HDR10 10 bits");
    Check(o2.HdrMetadata?.MaxCll is not null, $"2: NVENC conserva metadatos HDR ({o2.HdrMetadata?.MaxCll ?? "no"})");
    Check(o2.AudioStreams.Count() == 2 && o2.AudioStreams.Last().Codec == "eac3", "2: dos pistas, la segunda original");
    // La pista copiada puede arrastrar hasta un GOP previo de audio: se admite hasta ~1,5 s más.
    Check(o2.Duration.TotalSeconds is > 14.5 and < 16.6, $"2: dura unos 15 s ({o2.Duration.TotalSeconds:0.0})");
}

// 3) H.264 por CPU con conversión HDR->SDR, audio externo con retraso, MP4
var ext = Path.Combine(outDir, "audio_externo.m4a");
await ProcessRunner.RunAsync(tools.Ffmpeg, new[] { "-y", "-loglevel", "error", "-f", "lavfi", "-i", "sine=f=600:d=60", "-c:a", "aac", ext });
var j3 = JobPlanner.CreateDefault(hdr, defaults);
j3.OutputPath = Path.Combine(outDir, "3_h264_cpu_sdr.mp4");
j3.Video.Mode = VideoMode.Encode;
j3.Video.Codec = VideoCodec.H264;
j3.Video.Backend = EncoderBackend.Cpu;
j3.Video.Speed = 0;
j3.Video.MaxHeight = 720;
j3.AudioTracks.Add(new AudioTrackJob { ExternalPath = ext, DelayMs = 500, SourceChannels = 1, SourceCodec = "aac", Language = "cat", Codec = AudioCodecChoice.AacStereo, BitrateKbps = 128, Volume = VolumeMode.Soft });
j3.TrimDuration = TimeSpan.FromSeconds(10);
var o3 = await Run("3_h264_cpu_sdr", j3);
if (o3 is not null)
{
    Check(o3.Video?.Codec == "h264" && o3.Video.Hdr == HdrKind.None && o3.Video.ColorTransfer == "bt709", "3: H.264 SDR bt709");
    Check(o3.Video is { Height: 720 }, $"3: escalado a 720 ({o3.Video?.Width}x{o3.Video?.Height})");
    Check(o3.AudioStreams.Last().Language == "cat", "3: pista externa con idioma catalán");
}

// 4) AV1 por GPU en WebM
var j4 = JobPlanner.CreateDefault(hdr, defaults with { Container = ContainerKind.WebM });
j4.OutputPath = Path.Combine(outDir, "4_av1.webm");
j4.TrimDuration = TimeSpan.FromSeconds(8);
var o4 = await Run("4_av1_webm", j4);
if (o4 is not null)
{
    Check(o4.Video?.Codec == "av1" && o4.AudioStreams.Single().Codec == "opus", "4: AV1 + Opus");
    Check(o4.SubtitleStreams.Single().Codec == "webvtt", "4: subtítulo WebVTT");
}

// 5) AVI antiguo -> MP4 (debe recodificar automáticamente)
var avi = await prober.ProbeAsync(Path.Combine(samples, "sample_old.avi"));
var j5 = JobPlanner.CreateDefault(avi, defaults);
Console.WriteLine("   5: modo de vídeo elegido para XviD en MP4: " + j5.Video.Mode);
j5.OutputPath = Path.Combine(outDir, "5_avi.mp4");
await Run("5_avi", j5);

// 6) Subtítulo externo .srt quemado en ruta con caracteres problemáticos
var odd = Path.Combine(outDir, "carpeta [rara], de l'usuario");
Directory.CreateDirectory(odd);
File.Copy(Path.Combine(samples, "spa.srt"), Path.Combine(odd, "subs, raros [1].srt"), true);
var j6 = JobPlanner.CreateDefault(hdr, defaults);
j6.OutputPath = Path.Combine(odd, "6_quemado_externo.mp4");
j6.Subtitles.BurnKind = BurnSourceKind.External;
j6.Subtitles.BurnExternalPath = Path.Combine(odd, "subs, raros [1].srt");
j6.TrimStart = TimeSpan.FromSeconds(10);
j6.TrimDuration = TimeSpan.FromSeconds(8);
await Run("6_externo_ruta_rara", j6);

// 8) XviD/DivX en AVI con MP3 (desde un HDR: debe pasar a SDR, sin subtítulos como pista)
var j8 = JobPlanner.CreateDefault(hdr, defaults with { Container = ContainerKind.Avi });
j8.OutputPath = Path.Combine(outDir, "8_xvid.avi");
j8.Video.MaxHeight = 720;
j8.TrimDuration = TimeSpan.FromSeconds(8);
Check(j8.Video.Mode == VideoMode.Encode && j8.Video.Codec == VideoCodec.Xvid && j8.Video.Backend == EncoderBackend.Cpu, "8: AVI fuerza XviD por CPU");
Check(j8.AudioTracks.Single().Codec == AudioCodecChoice.Mp3Stereo && j8.Subtitles.Keep.Count == 0, "8: AVI usa MP3 y quita los subtítulos como pista");
var o8 = await Run("8_xvid_avi", j8);
if (o8 is not null)
{
    Check(o8.Video?.Codec == "mpeg4" && o8.Video.Height == 720 && o8.Video.Hdr == HdrKind.None, $"8: MPEG-4 ASP 720p SDR ({o8.Video?.Codec} {o8.Video?.Height})");
    Check(o8.AudioStreams.Single().Codec == "mp3" && !o8.SubtitleStreams.Any(), "8: audio MP3 y sin subtítulos");
    var tag = await ProcessRunner.RunAsync(tools.Ffprobe, new[] { "-v", "error", "-select_streams", "v:0", "-show_entries", "stream=codec_tag_string", "-of", "csv=p=0", j8.OutputPath });
    Check(tag.StdOut.Trim() == "XVID", $"8: etiqueta XVID ({tag.StdOut.Trim()})");
}

// 9) VP9 en WebM conservando el HDR (10 bits)
var j9 = JobPlanner.CreateDefault(hdr, defaults with { Container = ContainerKind.WebM, Codec = VideoCodec.Vp9 });
j9.OutputPath = Path.Combine(outDir, "9_vp9.webm");
j9.Video.Speed = 0;
j9.TrimDuration = TimeSpan.FromSeconds(3);
var o9 = await Run("9_vp9_webm", j9);
if (o9 is not null)
    Check(o9.Video?.Codec == "vp9" && o9.Video.BitDepth == 10 && o9.Video.Hdr == HdrKind.Hdr10, $"9: VP9 10 bits HDR10 ({o9.Video?.BitDepth} bits, {o9.Video?.Hdr})");

// 10) FLAC 5.1 (modo noche) + MP3 estéreo en MKV
var j10 = JobPlanner.CreateDefault(hdr, defaults with { Container = ContainerKind.Mkv, AudioCodec = AudioCodecChoice.Flac });
j10.AudioTracks.Add(JobPlanner.TrackFromStream(hdr.Streams[2], AudioCodecChoice.Mp3Stereo, VolumeMode.Soft));
j10.OutputPath = Path.Combine(outDir, "10_flac_mp3.mkv");
j10.TrimDuration = TimeSpan.FromSeconds(6);
var o10 = await Run("10_flac_mp3", j10);
if (o10 is not null)
{
    var au = o10.AudioStreams.ToList();
    Check(au.Count == 2 && au[0].Codec == "flac" && au[0].Channels == 6 && au[1].Codec == "mp3" && au[1].Channels == 2, $"10: FLAC 5.1 + MP3 estéreo ({string.Join(", ", au.Select(a => a.Codec + " " + a.Channels))})");
}

// 11) FLAC y MP3 también caben en MP4
var j11 = JobPlanner.CreateDefault(hdr, defaults with { AudioCodec = AudioCodecChoice.Flac });
j11.OutputPath = Path.Combine(outDir, "11_flac.mp4");
j11.TrimDuration = TimeSpan.FromSeconds(4);
var o11 = await Run("11_flac_mp4", j11);
if (o11 is not null) Check(o11.AudioStreams.Single().Codec == "flac", "11: FLAC en MP4");

// 7) Medición de volumen: diálogo (s 4-9) frente a acción (s 0-3) en una mezcla estándar y en modo noche.
async Task<double> Mean(string file, double from, double dur)
{
    var r = await ProcessRunner.RunAsync(tools.Ffmpeg, new[] { "-hide_banner", "-ss", from.ToString(CultureInfo.InvariantCulture), "-t", dur.ToString(CultureInfo.InvariantCulture), "-i", file, "-map", "0:a:0", "-ac", "2", "-af", "volumedetect", "-f", "null", "-" });
    var m = Regex.Match(r.StdErr, @"mean_volume: (-?[\d.]+) dB");
    return m.Success ? double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : double.NaN;
}
// 12) Perfil DLNA real de la Samsung QE55Q60T: MKV anunciado como video/x-mkv, sin DTS
var sink = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "samsung-qe55q60t-sink.txt"));
var tv = DeviceProfile.FromProtocolInfo(sink, "uuid:tv", "[TV] Samsung", "Samsung", "QE55Q60T", "192.168.1.10");
Console.WriteLine($"\n== Perfil de la tele: {tv.Mimes.Count} tipos, {tv.DlnaProfiles.Count} perfiles DLNA");
Check(tv.AcceptsFromWindows(ContainerKind.Mp4) && !tv.AcceptsFromWindows(ContainerKind.Mkv) && tv.PlaysContainer(ContainerKind.Mkv), "12: MP4 sí; MKV lo reproduce pero Windows no lo envía");
Check(tv.AcceptsFromWindows(ContainerKind.Avi), "12: AVI (video/avi) sí");
Check(tv.SupportsVideo("hevc") && tv.SupportsVideo("h264") && tv.SupportsVideo("mpeg4") && !tv.SupportsVideo("av1"), "12: vídeo HEVC, H.264 y XviD sí; AV1 no");
Check(tv.SupportsAudio("aac") && tv.SupportsAudio("ac3") && tv.SupportsAudio("mp3") && tv.SupportsAudio("flac") && !tv.SupportsAudio("dts") && !tv.SupportsAudio("truehd") && !tv.SupportsAudio("eac3"),
      "12: audio AAC, AC3, MP3 y FLAC sí; DTS (aunque anuncia AAC_ADTS), TrueHD y E-AC3 no");

var j12 = JobPlanner.CreateDefault(hdr, defaults with { Container = ContainerKind.Mkv, Device = tv });
Check(j12.Container == ContainerKind.Mp4 && j12.OutputPath.EndsWith(".mp4") && j12.Video.Mode == VideoMode.Copy, "12: con la tele elegida, un MKV pasa a MP4 copiando el HEVC");
var dts = JobPlanner.TrackFromStream(hdr.Streams[2], AudioCodecChoice.Copy, VolumeMode.Off) with { SourceCodec = "dts", SourceChannels = 6 };
j12.AudioTracks.Add(dts);
var problems = TvCompatibility.Evaluate(j12, tv).Where(c => c.Level == CheckLevel.Problem).ToList();
Check(problems.Count == 1 && problems[0].Text.Contains("DTS"), $"12: detecta el DTS copiado ({problems.Count} problemas)");
var changes = TvCompatibility.Adapt(j12, tv);
JobPlanner.Normalize(j12);
Check(dts.Codec == AudioCodecChoice.Ac3_51 && changes.Count == 1, $"12: el DTS 5.1 se convierte a AC3 5.1 ({string.Join("; ", changes)})");
Check(TvCompatibility.Evaluate(j12, tv).All(c => c.Level != CheckLevel.Problem), "12: tras ajustar, la tele lo acepta");
var jAv1 = JobPlanner.CreateDefault(hdr, defaults with { Device = tv });
jAv1.Video.Mode = VideoMode.Encode; jAv1.Video.Codec = VideoCodec.Av1;
TvCompatibility.Adapt(jAv1, tv);
Check(jAv1.Video.Codec == VideoCodec.Hevc, "12: AV1 (no anunciado) pasa a HEVC");
var generic = JobPlanner.CreateDefault(hdr, defaults with { Container = ContainerKind.Mkv, Device = DeviceProfile.Generic });
Check(generic.Container == ContainerKind.Mp4, "12: la tele genérica también evita MKV");

// 13) Clip de sincronización con una pista externa (retraso positivo y negativo)
if (only is null || only.Contains("sync"))
{
    var extAudio = Path.Combine(outDir, "13_externo.ac3");
    await ProcessRunner.RunAsync(tools.Ffmpeg, new[] { "-hide_banner", "-y", "-loglevel", "error", "-i", hdr.Path, "-map", "0:a:0", "-c:a", "ac3", extAudio });
    var extTrack = new AudioTrackJob { ExternalPath = extAudio, ExternalStreamIndex = 0, SourceCodec = "ac3", SourceChannels = 6 };
    var gpu = await tools.HasNvencAsync();
    foreach (var (delay, start) in new[] { (300, 5.0), (-250, 5.0), (3000, 1.0) })
    {
        var clip = Path.Combine(outDir, $"13_sync_{delay}.mp4");
        var sargs = CommandBuilder.BuildSyncPreview(hdr, extTrack, delay, TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(6), clip, gpu);
        var sr = await ProcessRunner.RunAsync(tools.Ffmpeg, sargs);
        var ok = sr.ExitCode == 0 && File.Exists(clip);
        if (!ok) Console.WriteLine(sr.StdErr);
        var oc = ok ? await prober.ProbeAsync(clip) : null;
        Check(oc is not null && oc.Video?.Codec == "h264" && oc.Video.Height == 720 && oc.Video.Hdr == HdrKind.None
              && oc.AudioStreams.Single().Codec == "aac" && Math.Abs(oc.Duration.TotalSeconds - 6) < 0.3,
              $"13: clip de sincronización con retraso {delay} ms desde {start} s ({oc?.Video?.Codec} {oc?.Video?.Height}p, {oc?.Duration.TotalSeconds:0.00} s)");
    }
    // Con retraso de 3 s empezando en el segundo 1, el primer 2 s del clip debe ser silencio.
    var silent = await Mean(Path.Combine(outDir, "13_sync_3000.mp4"), 0.2, 1.5);
    var loud = await Mean(Path.Combine(outDir, "13_sync_3000.mp4"), 3.5, 1.5);
    Check(silent < -60 && loud > -50, $"13: el retraso deja silencio al principio ({silent:0} dB frente a {loud:0} dB)");
}

// 14) Reducción de ruido: hqdn3d conserva los 10 bits del HDR; NLMeans en un vídeo de 8 bits
var j14 = JobPlanner.CreateDefault(hdr, defaults);
j14.Video.Mode = VideoMode.Encode;
j14.Video.Denoise = DenoiseMode.Strong;
j14.OutputPath = Path.Combine(outDir, "14_ruido_hdr.mp4");
j14.TrimDuration = TimeSpan.FromSeconds(3);
Check(CommandBuilder.Build(j14).Any(x => x.Contains("hqdn3d=7:7:5:5")), "14: el filtro hqdn3d fuerte está en el comando");
var o14 = await Run("14_ruido_hdr", j14);
if (o14 is not null) Check(o14.Video?.BitDepth == 10 && o14.Video.Hdr == HdrKind.Hdr10, "14: con ruido reducido sigue siendo HDR10 de 10 bits");
j14.Video.Denoise = DenoiseMode.Max;
Check(CommandBuilder.Build(j14).Any(x => x.Contains("hqdn3d=10:10:8:8")) && !CommandBuilder.Build(j14).Any(x => x.Contains("nlmeans")), "14: el nivel máximo en 10 bits no usa NLMeans");
var old = await prober.ProbeAsync(Path.Combine(samples, "sample_old.avi"));
var j14b = JobPlanner.CreateDefault(old, defaults);
j14b.Video.Mode = VideoMode.Encode;
j14b.Video.Denoise = DenoiseMode.Max;
j14b.OutputPath = Path.Combine(outDir, "14_ruido_nlmeans.mp4");
j14b.TrimDuration = TimeSpan.FromSeconds(2);
Check(CommandBuilder.Build(j14b).Any(x => x.Contains("nlmeans")), "14: en 8 bits el nivel máximo usa NLMeans");
await Run("14_ruido_nlmeans", j14b);

// 15) Perfiles
var serie = ConversionProfile.BuiltIn.First(p => p.Name.StartsWith("Serie"));
var j15 = JobPlanner.CreateDefault(hdr, defaults with { Profile = serie });
Check(j15.Video.Mode == VideoMode.Encode && j15.Video.MaxHeight == 720 && j15.Video.Quality == 28 && j15.Container == ContainerKind.Mp4, "15: el perfil Serie a 720p se aplica al abrir");
var mp4only = ConversionProfile.BuiltIn.First(p => p.Name.StartsWith("Solo pasar"));
var j15b = JobPlanner.CreateDefault(hdr, defaults with { Container = ContainerKind.Mkv, Profile = mp4only });
Check(j15b.Container == ContainerKind.Mp4 && j15b.Video.Mode == VideoMode.Copy && j15b.AudioTracks.All(t => t.Codec == AudioCodecChoice.Copy) && j15b.OutputPath.EndsWith(".mp4"),
      "15: \"Solo pasar a MP4\" copia vídeo y audio");
var xvidProfile = ConversionProfile.BuiltIn.First(p => p.Container == ContainerKind.Avi);
var j15c = JobPlanner.CreateDefault(hdr, defaults with { Profile = xvidProfile });
Check(j15c.Container == ContainerKind.Avi && j15c.Video.Codec == VideoCodec.Xvid && j15c.AudioTracks.All(t => t.Codec == AudioCodecChoice.Mp3Stereo), "15: el perfil DivX/XviD da AVI + XviD + MP3");
var noGpu = JobPlanner.CreateDefault(hdr, defaults with { Hardware = HardwareEncoders.None, Profile = serie });
Check(noGpu.Video.Backend == EncoderBackend.Cpu, "15: sin tarjeta NVIDIA el perfil usa el procesador");
var noGpu2 = JobPlanner.CreateDefault(hdr, defaults with { Hardware = HardwareEncoders.None });
Check(noGpu2.Video.Backend == EncoderBackend.Cpu && noGpu2.Video.Quality == JobPlanner.RecommendedQuality(VideoCodec.Hevc, EncoderBackend.Cpu), "15: sin GPU la calidad recomendada es la del procesador");

// Un perfil propio se guarda, se vuelve a leer y reproduce los mismos ajustes
var mine = JobPlanner.CreateDefault(hdr, defaults);
mine.Video.Mode = VideoMode.Encode; mine.Video.Codec = VideoCodec.H264; mine.Video.Quality = 21; mine.Video.Denoise = DenoiseMode.Light; mine.Video.MaxHeight = 1080;
mine.AudioTracks[0].Codec = AudioCodecChoice.Ac3_51; mine.AudioTracks[0].BitrateKbps = 640; mine.AudioTracks[0].Volume = VolumeMode.Soft;
mine.Volume.Compression = 3;
var settingsFile = Path.Combine(outDir, "15_ajustes.json");
var st = AppSettings.Load(settingsFile);
st.Profiles.Add(ConversionProfile.FromJob("Mi perfil", mine));
st.ProfileName = "Mi perfil";
st.Save();
var st2 = AppSettings.Load(settingsFile);
var j15d = JobPlanner.CreateDefault(hdr, st2.ToDefaults() with { PreferredLanguage = "spa" });
Check(st2.Profile is { IsBuiltIn: false, Name: "Mi perfil" }, "15: el perfil propio se guarda y se vuelve a leer");
Check(j15d.Video.Codec == VideoCodec.H264 && j15d.Video.Quality == 21 && j15d.Video.Denoise == DenoiseMode.Light && j15d.Video.MaxHeight == 1080
      && j15d.AudioTracks[0].Codec == AudioCodecChoice.Ac3_51 && j15d.AudioTracks[0].BitrateKbps == 640 && j15d.AudioTracks[0].Volume == VolumeMode.Soft && j15d.Volume.Compression == 3,
      "15: al abrir otro vídeo con el perfil propio salen los mismos ajustes");

// 16) Resoluciones "raras": cine 2,40:1, 4:3, vertical, DVD anamórfico y sin ampliar nunca
foreach (var (w, h, max, ew, eh) in new[]
{
    (3840, 1600, 1080, 1920, 800), (3840, 2160, 1080, 1920, 1080), (1440, 1080, 720, 960, 720), (1080, 1920, 720, 720, 1280),
    (1920, 1080, 2160, 1920, 1080), (720, 576, 480, 600, 480), (1920, 804, 720, 1280, 536), (1998, 1080, 720, 1280, 692),
    (4096, 1716, 1080, 1920, 804), (1280, 1024, 720, 900, 720), (2560, 1080, 1080, 1920, 810), (1081, 607, 480, 854, 480),
})
{
    var r = JobPlanner.FitResolution(w, h, max);
    Check(r == (ew, eh) && r.Width % 2 == 0 && r.Height % 2 == 0, $"16: {w}×{h} a {max}p → {r.Width}×{r.Height} (esperado {ew}×{eh})");
}

// Vídeo de móvil: 1920×1080 guardado con giro de 90° (se ve vertical) y bandas negras laterales
if (only is null || only.Contains("rot"))
{
    var flat = Path.Combine(outDir, "16_plano.mp4");
    var rotated = Path.Combine(outDir, "16_movil_girado.mp4");
    await ProcessRunner.RunAsync(tools.Ffmpeg, new[] { "-hide_banner", "-y", "-loglevel", "error", "-f", "lavfi", "-i", "testsrc2=s=1920x800:r=25:d=12",
        "-vf", "pad=1920:1080:0:140:black", "-c:v", "libx264", "-preset", "ultrafast", "-pix_fmt", "yuv420p", flat });
    await ProcessRunner.RunAsync(tools.Ffmpeg, new[] { "-hide_banner", "-y", "-loglevel", "error", "-display_rotation:v:0", "90", "-i", flat, "-c", "copy", rotated });
    var rot = await prober.ProbeAsync(rotated);
    Check(rot.Video!.Rotation is 90 or 270 && rot.Video.DisplayWidth == 1080 && rot.Video.DisplayHeight == 1920, $"16: detecta el giro ({rot.Video.Rotation}°, se ve {rot.Video.DisplayWidth}×{rot.Video.DisplayHeight})");
    var rcrop = await preview.DetectCropAsync(rot);
    Check(rcrop is { Width: 800, Height: 1920 }, $"16: las bandas negras se detectan en la imagen girada ({rcrop})");
    var j16 = JobPlanner.CreateDefault(rot, defaults);
    j16.Video.Mode = VideoMode.Encode;
    j16.Video.Crop = rcrop;
    j16.Video.MaxHeight = 720;
    j16.OutputPath = Path.Combine(outDir, "16_movil_720.mp4");
    Check(JobPlanner.OutputSize(j16) == (534, 1280), $"16: vertical recortado a 720p → {JobPlanner.OutputSize(j16)}");
    var o16 = await Run("16_movil_720", j16);
    if (o16 is not null) Check(o16.Video!.DisplayWidth == 534 && o16.Video.DisplayHeight == 1280, $"16: el vídeo vertical sale en {o16.Video.DisplayWidth}×{o16.Video.DisplayHeight}");
}

// 17) Gráficas: NVIDIA, Intel (Quick Sync) y AMD (AMF)
var hwReal = await HardwareEncoders.DetectAsync(tools);
Console.WriteLine("   gráficas detectadas: " + string.Join(", ", hwReal.Vendors.Select(HardwareEncoders.VendorName)));
Check(hwReal.Supports(VideoCodec.Hevc, EncoderBackend.Gpu) && hwReal.Supports(VideoCodec.H264, EncoderBackend.Gpu) && hwReal.Supports(VideoCodec.Av1, EncoderBackend.Gpu),
      "17: en este equipo detecta la NVIDIA para HEVC, H.264 y AV1");
Check(!hwReal.Supports(VideoCodec.Hevc, EncoderBackend.Qsv) && !hwReal.Supports(VideoCodec.Hevc, EncoderBackend.Amf), "17: no ofrece Intel ni AMD si no funcionan (aquí no hay)");

var intel = new HardwareEncoders(new[] { (VideoCodec.Hevc, EncoderBackend.Qsv), (VideoCodec.H264, EncoderBackend.Qsv) });
var amd = new HardwareEncoders(new[] { (VideoCodec.Hevc, EncoderBackend.Amf), (VideoCodec.H264, EncoderBackend.Amf) });
Check(intel.Resolve(VideoCodec.Hevc, EncoderBackend.Gpu) == EncoderBackend.Qsv && amd.Resolve(VideoCodec.H264, EncoderBackend.Gpu) == EncoderBackend.Amf,
      "17: si se pedía NVIDIA y solo hay Intel o AMD, se usa esa gráfica");
Check(intel.Resolve(VideoCodec.Av1, EncoderBackend.Gpu) == EncoderBackend.Cpu && intel.Resolve(VideoCodec.Hevc, EncoderBackend.Cpu) == EncoderBackend.Cpu,
      "17: AV1 con Intel va al procesador y el procesador elegido se respeta");
Check(string.Join(",", intel.BackendsFor(VideoCodec.Hevc)) == "Qsv,Cpu", "17: con Intel se ofrecen Intel y procesador");
var jIntel = JobPlanner.CreateDefault(hdr, defaults with { Hardware = intel, Profile = serie });
Check(jIntel.Video.Backend == EncoderBackend.Qsv && jIntel.Video.Quality == 28, "17: el perfil Serie a 720p usa Intel si es lo que hay");
var jAmd = JobPlanner.CreateDefault(hdr, defaults with { Hardware = amd });
jAmd.Video.Mode = VideoMode.Encode;
var amdArgs = CommandBuilder.Build(jAmd);
Check(amdArgs.Contains("hevc_amf") && amdArgs.Contains("cqp") && amdArgs.Contains("main10") && amdArgs.Contains("p010le") && amdArgs.Contains("bt2020"),
      "17: comando AMD HEVC 10 bits con HDR");
jIntel.Video.Codec = VideoCodec.H264;
JobPlanner.Normalize(jIntel);
var qsvArgs = CommandBuilder.Build(jIntel);
Check(qsvArgs.Contains("h264_qsv") && qsvArgs.Contains("-global_quality") && qsvArgs.Contains("nv12") && qsvArgs.Any(a => a.Contains("tonemap")),
      "17: comando Intel H.264 (8 bits, HDR pasado a SDR)");

// Sin la gráfica no se pueden ejecutar, pero sí comprobar que FFmpeg acepta todas las opciones:
// el error tiene que ser por no encontrar la gráfica, nunca por una opción mal escrita.
foreach (var (codec, backend) in new[] { (VideoCodec.Hevc, EncoderBackend.Qsv), (VideoCodec.H264, EncoderBackend.Qsv), (VideoCodec.Hevc, EncoderBackend.Amf), (VideoCodec.H264, EncoderBackend.Amf) })
{
    foreach (var speed in new[] { 0, 2, 4 })
    {
        var targs = new List<string> { "-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i", "color=black:s=1280x720", "-frames:v", "2" };
        targs.AddRange(CommandBuilder.HardwareEncoderArgs(codec, backend, JobPlanner.RecommendedQuality(codec, backend), speed));
        targs.AddRange(new[] { "-f", "null", "-" });
        var tr = await ProcessRunner.RunAsync(tools.Ffmpeg, targs);
        var badOption = Regex.IsMatch(tr.StdErr, "Unrecognized option|Unable to parse|Invalid value|not found|Error setting option|has not been used", RegexOptions.IgnoreCase)
                        && !tr.StdErr.Contains("amfrt64.dll", StringComparison.OrdinalIgnoreCase);
        var noDevice = tr.StdErr.Contains("MFX session") || tr.StdErr.Contains("amfrt64.dll") || tr.ExitCode == 0;
        Check(!badOption && noDevice, $"17: opciones de {codec} {HardwareEncoders.VendorName(backend)} (velocidad {speed}) aceptadas{(noDevice ? "" : ": " + tr.StdErr.Split('\n')[0])}");
    }
}

// 18) Subtítulos completos frente a forzados sin marcar (por el número de líneas)
if (only is null || only.Contains("subs"))
{
    static string Srt(int count, int everySeconds, string text)
    {
        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < count; i++)
        {
            var t = TimeSpan.FromSeconds(5 + i * everySeconds);
            sb.Append($"{i + 1}\n{t:hh\\:mm\\:ss},000 --> {t + TimeSpan.FromSeconds(2):hh\\:mm\\:ss},000\n{text} {i + 1}\n\n");
        }
        return sb.ToString();
    }
    File.WriteAllText(Path.Combine(outDir, "18_forzados.srt"), Srt(12, 140, "Habla en ruso"));
    File.WriteAllText(Path.Combine(outDir, "18_completos_es.srt"), Srt(300, 6, "Diálogo"));
    File.WriteAllText(Path.Combine(outDir, "18_completos_en.srt"), Srt(300, 6, "Dialogue"));
    async Task MakeMovie(string file, string audioLang, string subCodec)
    {
        // 30 minutos a 1 fotograma por segundo: rápido de crear pero con la duración de un capítulo.
        await ProcessRunner.RunAsync(tools.Ffmpeg, new[] { "-hide_banner", "-y", "-loglevel", "error",
            "-f", "lavfi", "-i", "color=gray:s=320x240:r=1:d=1800", "-f", "lavfi", "-i", "anullsrc=r=48000:cl=stereo",
            "-i", Path.Combine(outDir, "18_forzados.srt"), "-i", Path.Combine(outDir, "18_completos_es.srt"), "-i", Path.Combine(outDir, "18_completos_en.srt"),
            "-map", "0:v", "-map", "1:a", "-map", "2", "-map", "3", "-map", "4", "-t", "1800", "-c:v", "libx264", "-preset", "ultrafast", "-c:a", "aac", "-c:s", subCodec,
            "-metadata:s:a:0", $"language={audioLang}", "-metadata:s:s:0", "language=spa", "-metadata:s:s:1", "language=spa", "-metadata:s:s:2", "language=eng", file });
    }
    var mkvSubs = Path.Combine(outDir, "18_pelicula_ingles.mkv");
    var mp4Subs = Path.Combine(outDir, "18_pelicula_espanol.mp4");
    await MakeMovie(mkvSubs, "eng", "srt");
    await MakeMovie(mp4Subs, "spa", "mov_text");

    var m1 = await prober.ProbeAsync(mkvSubs);
    var s1 = m1.SubtitleStreams.ToList();
    Check(s1.Count == 3 && s1.All(x => x.EventCount is null && !x.ForcedFlag), "18: el MKV no dice cuántas líneas tiene ni marca los forzados");
    var jBefore = JobPlanner.CreateDefault(m1, defaults);
    await SubtitleAnalysis.CountAsync(tools, m1);
    Check(s1[0].EventCount == 12 && s1[1].EventCount == 300 && s1[2].EventCount == 300, $"18: cuenta las líneas ({string.Join(", ", s1.Select(x => x.EventCount))})");
    Check(s1[0].IsForced && !s1[1].IsForced && !s1[2].IsForced, "18: los de 12 líneas son forzados; los de 300, completos");
    Check(s1[0].Describe().Contains("forzados (por el número de líneas)") && s1[0].Describe().Contains("12 líneas"), "18: se ve en la descripción: " + s1[0].Describe());
    var j18 = JobPlanner.CreateDefault(m1, defaults);
    Check(j18.Subtitles.BurnKind == BurnSourceKind.Internal && j18.Subtitles.BurnStreamIndex == s1[1].Index,
          $"18: audio en inglés → se queman los completos en español (#{j18.Subtitles.BurnStreamIndex}), no los forzados");
    Check(jBefore.Subtitles.BurnStreamIndex == s1[0].Index, "18: (sin contar, se habría quemado el primero, que son los forzados)");

    var m2 = await prober.ProbeAsync(mp4Subs);
    var s2 = m2.SubtitleStreams.ToList();
    await SubtitleAnalysis.CountAsync(tools, m2);
    Check(s2[0].EventCount == 12 && s2[1].EventCount == 300 && s2[0].IsForced && !s2[1].IsForced, $"18: en MP4 no cuenta los huecos vacíos entre subtítulos ({string.Join(", ", s2.Select(x => x.EventCount))})");
    var j18b = JobPlanner.CreateDefault(m2, defaults);
    j18b.OutputPath = Path.Combine(outDir, "18_salida.mp4");
    j18b.TrimDuration = TimeSpan.FromSeconds(30);
    Check(!j18b.Subtitles.Burns && j18b.Subtitles.Keep.Count == 2, "18: audio en español → no se queman, se guardan los dos en español");
    var o18 = await Run("18_forzados_por_defecto", j18b);
    if (o18 is not null)
    {
        var disp = await ProcessRunner.RunAsync(tools.Ffprobe, new[] { "-v", "error", "-select_streams", "s", "-show_entries", "stream_disposition=default,forced", "-of", "csv=p=0", j18b.OutputPath });
        var lines = disp.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Check(lines.Length == 2 && lines[0] == "1,1" && lines[1] == "0,0", $"18: los forzados salen como \"por defecto\" y \"forzados\" ({string.Join(" | ", lines)})");
    }
}

// 19) Estimación del tiempo: valores de referencia y aprendizaje con lo que tarda de verdad
{
    // Como "La Odisea": 2 h 52 min de 4K HEVC 10 bits (3840×1600 sin bandas) a HEVC por NVIDIA; tardó unos 30 min.
    var movie = new MediaInfo
    {
        Path = "pelicula.mkv", Duration = TimeSpan.FromMinutes(172), SizeBytes = 33_000_000_000,
        Streams = new List<StreamInfo> { new() { Index = 0, Kind = StreamKind.Video, Codec = "hevc", Width = 3840, Height = 2160, FrameRate = 23.976, PixelFormat = "yuv420p10le" } },
    };
    var est = JobPlanner.CreateDefault(movie, defaults);
    est.Video.Mode = VideoMode.Encode;
    est.Video.Crop = new CropRect(3840, 1600, 0, 280);
    var (t1, l1) = SpeedEstimator.Estimate(est, null);
    Check(t1.TotalMinutes is > 20 and < 45 && !l1, $"19: 4K HEVC por NVIDIA se estima en {t1.TotalMinutes:0} min (real: unos 30)");
    var learned = new Dictionary<string, double>();
    SpeedEstimator.Learn(est, TimeSpan.FromMinutes(30), learned);
    var (t2, l2) = SpeedEstimator.Estimate(est, learned);
    Check(Math.Abs(t2.TotalMinutes - 30) < 0.5 && l2, $"19: tras una conversión real, la misma película se estima en {t2.TotalMinutes:0.0} min");
    est.Video.MaxHeight = 1080;
    var (t3, _) = SpeedEstimator.Estimate(est, learned);
    Check(t3 < t2, $"19: a 1080p se estima menos ({t3.TotalMinutes:0} min)");
    est.Video.Mode = VideoMode.Copy;
    var (t4, _) = SpeedEstimator.Estimate(est, learned);
    Check(t4.TotalMinutes is > 1 and < 10, $"19: copiar 33 GB se estima en {t4.TotalMinutes:0} min");
}

if (only is null || only.Contains("vol"))
{
    var orig = hdr.Path;
    var dO = await Mean(orig, 4.5, 4); var aO = await Mean(orig, 0.5, 2);
    Console.WriteLine($"\n== Volumen original (mezcla estándar): diálogo {dO:0.0} dB · acción {aO:0.0} dB · diferencia {aO - dO:0.0} dB");
    var night = Path.Combine(outDir, "1_copia_noche.mp4");
    var dN = await Mean(night, 14.5, 4); var aN = await Mean(night, 10.5, 2);
    Console.WriteLine($"== Volumen modo noche: diálogo {dN:0.0} dB · acción {aN:0.0} dB · diferencia {aN - dN:0.0} dB");
    Check((aN - dN) < (aO - dO) - 6, "el modo noche reduce la diferencia en más de 6 dB");
}

Console.WriteLine(failures == 0 ? "\nTODO CORRECTO" : $"\n{failures} FALLOS");
return failures == 0 ? 0 : 1;
