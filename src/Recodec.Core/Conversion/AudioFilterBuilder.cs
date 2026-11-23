using System.Globalization;

namespace Recodec.Core.Conversion;

/// <summary>
/// Construye la cadena de filtros de audio que corrige el volumen:
///   1) mezcla a estéreo dando prioridad al canal central (diálogos),
///   2) compresión del rango dinámico (baja las explosiones),
///   3) normalización de sonoridad EBU R128 (loudnorm).
/// </summary>
public static class AudioFilterBuilder
{
    private static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    public static string? Build(AudioTrackJob t, VolumeSettings v)
    {
        if (t.Codec == AudioCodecChoice.Copy) return null;

        var src = Math.Max(t.SourceChannels, 1);
        var outCh = ContainerRules.OutputChannels(t.Codec, src);
        var layout = t.SourceChannelLayout ?? "";
        var parts = new List<string>();

        // 1) Mezcla de canales
        if (t.Volume == VolumeMode.Night && outCh == 2 && src >= 6 && (src == 6 && layout.StartsWith("5.1") || src == 8 && layout.StartsWith("7.1")))
        {
            parts.Add(DialogDownmix(src, v.DialogBoost));
        }
        else if (t.Volume == VolumeMode.Night && outCh == 6 && src == 6 && layout.StartsWith("5.1"))
        {
            // Mantener 5.1 pero bajar todo menos el central.
            var w = F(1.0 - 0.05 * Clamp(v.DialogBoost));
            parts.Add($"pan=5.1|c0={w}*c0|c1={w}*c1|c2=c2|c3={w}*c3|c4={w}*c4|c5={w}*c5");
        }
        else if (outCh == 2 && src != 2)
        {
            parts.Add("aformat=channel_layouts=stereo");
        }
        else if (outCh == 6 && src > 6)
        {
            parts.Add("aformat=channel_layouts=5.1");
        }

        // 2) Compresión y 3) normalización
        switch (t.Volume)
        {
            case VolumeMode.Soft:
                parts.Add("acompressor=threshold=-20dB:ratio=2:attack=20:release=300:knee=4");
                parts.Add($"loudnorm=I={F(v.TargetLufs)}:LRA=11:TP=-1.5");
                parts.Add("aresample=48000");
                break;
            case VolumeMode.Night:
                var c = Clamp(v.Compression);
                if (c > 0)
                {
                    var threshold = -10 - 2 * c;          // c=6 -> -22 dB, c=10 -> -30 dB
                    var ratio = 1.5 + 0.45 * c;           // c=6 -> 4.2:1, c=10 -> 6:1
                    parts.Add($"acompressor=threshold={F(threshold)}dB:ratio={F(ratio)}:attack=8:release=300:knee=4");
                }
                var lra = Math.Max(4, 11 - 0.7 * c);      // c=6 -> 6.8 LU
                parts.Add($"loudnorm=I={F(v.TargetLufs)}:LRA={F(lra)}:TP=-1.5");
                parts.Add("aresample=48000");
                break;
        }

        return parts.Count == 0 ? null : string.Join(",", parts);
    }

    /// <summary>
    /// Mezcla 5.1/7.1 a estéreo con el canal central a ganancia completa y el resto atenuado.
    /// Una mezcla estándar usa 0,707 para todos; aquí, cuanto mayor es el realce, menos peso tienen música y efectos.
    /// El canal LFE (subwoofer) se descarta, como en el "modo noche" de los reproductores.
    /// </summary>
    public static string DialogDownmix(int channels, int boost)
    {
        var b = Clamp(boost);
        var front = 0.707 - 0.04 * b;          // b=0 -> 0,707 (estándar), b=10 -> 0,307
        var surround = front * 0.85;
        var fw = F(front);
        var sw = F(surround);
        // Orden de canales FFmpeg: 5.1 = FL FR FC LFE BL/SL BR/SR ; 7.1 = FL FR FC LFE BL BR SL SR
        return channels == 8
            ? $"pan=stereo|c0=c2+{fw}*c0+{sw}*c4+{sw}*c6|c1=c2+{fw}*c1+{sw}*c5+{sw}*c7"
            : $"pan=stereo|c0=c2+{fw}*c0+{sw}*c4|c1=c2+{fw}*c1+{sw}*c5";
    }

    private static int Clamp(int x) => Math.Clamp(x, 0, 10);
}
