using Recodec.Core.Ffmpeg;

namespace Recodec.Core.Conversion;

/// <summary>
/// Codificadores de tarjeta gráfica que funcionan en este equipo (NVIDIA NVENC, Intel Quick Sync, AMD AMF).
/// Se averigua al arrancar codificando unos pocos fotogramas con las mismas opciones que se usarán de verdad:
/// solo se ofrece lo que ha funcionado.
/// </summary>
public sealed class HardwareEncoders
{
    /// <summary>Orden de preferencia cuando hay varias gráficas.</summary>
    public static readonly EncoderBackend[] Order = { EncoderBackend.Gpu, EncoderBackend.Qsv, EncoderBackend.Amf };

    private readonly HashSet<(VideoCodec, EncoderBackend)> _ok;

    public HardwareEncoders(IEnumerable<(VideoCodec Codec, EncoderBackend Backend)> working) => _ok = working.ToHashSet();

    public static HardwareEncoders None { get; } = new(Array.Empty<(VideoCodec, EncoderBackend)>());

    /// <summary>Una tarjeta NVIDIA moderna (valor por defecto en pruebas y antes de detectar nada).</summary>
    public static HardwareEncoders Nvidia { get; } = new(new[]
    {
        (VideoCodec.Hevc, EncoderBackend.Gpu), (VideoCodec.H264, EncoderBackend.Gpu), (VideoCodec.Av1, EncoderBackend.Gpu),
    });

    public bool Any => _ok.Count > 0;
    public bool Supports(VideoCodec codec, EncoderBackend backend) => backend == EncoderBackend.Cpu || _ok.Contains((codec, backend));

    /// <summary>Gráficas que funcionan con algún códec, en orden de preferencia.</summary>
    public IEnumerable<EncoderBackend> Vendors => Order.Where(b => _ok.Any(x => x.Item2 == b));

    /// <summary>Codificadores disponibles para un códec: primero las gráficas, al final el procesador.</summary>
    public IEnumerable<EncoderBackend> BackendsFor(VideoCodec codec) =>
        Order.Where(b => _ok.Contains((codec, b))).Append(EncoderBackend.Cpu);

    /// <summary>
    /// El codificador que se usará: el preferido si funciona con ese códec; si se prefería una gráfica que no está,
    /// otra gráfica que sí funcione; si no, el procesador.
    /// </summary>
    public EncoderBackend Resolve(VideoCodec codec, EncoderBackend preferred)
    {
        if (preferred == EncoderBackend.Cpu || Supports(codec, preferred)) return preferred;
        return Order.FirstOrDefault(b => _ok.Contains((codec, b)), EncoderBackend.Cpu);
    }

    /// <summary>Prueba cada combinación posible (5 fotogramas cada una, en paralelo).</summary>
    public static async Task<HardwareEncoders> DetectAsync(FfmpegTools tools, CancellationToken ct = default)
    {
        if (!tools.IsAvailable) return None;
        var candidates = Order.SelectMany(b => ContainerRules.HardwareCodecs(b).Select(c => (Codec: c, Backend: b))).ToList();
        var results = await Task.WhenAll(candidates.Select(async c =>
        {
            var args = new List<string> { "-hide_banner", "-nostdin", "-loglevel", "error", "-f", "lavfi", "-i", "color=black:s=1280x720:r=24", "-frames:v", "5" };
            args.AddRange(CommandBuilder.HardwareEncoderArgs(c.Codec, c.Backend, JobPlanner.RecommendedQuality(c.Codec, c.Backend), 2));
            args.AddRange(new[] { "-f", "null", "-" });
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(20));
                var r = await ProcessRunner.RunAsync(tools.Ffmpeg, args, ct: timeout.Token);
                return r.ExitCode == 0;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return false; }
        }));
        return new HardwareEncoders(candidates.Where((_, i) => results[i]));
    }

    public static string VendorName(EncoderBackend b) => b switch
    {
        EncoderBackend.Gpu => "NVIDIA",
        EncoderBackend.Qsv => "Intel",
        EncoderBackend.Amf => "AMD",
        _ => "procesador",
    };
}
