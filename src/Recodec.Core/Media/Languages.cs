using System.Globalization;

namespace Recodec.Core.Media;

public sealed record LanguageOption(string Code, string Name)
{
    public override string ToString() => Name;
}

/// <summary>Idiomas en códigos ISO 639-2 (los que usan MKV y MP4).</summary>
public static class Languages
{
    // Código bibliográfico (el habitual en MKV) -> nombre en español.
    private static readonly (string Code, string Name)[] Known =
    {
        ("spa", "Español"), ("eng", "Inglés"), ("fre", "Francés"), ("ger", "Alemán"),
        ("ita", "Italiano"), ("por", "Portugués"), ("cat", "Catalán"), ("baq", "Euskera"),
        ("glg", "Gallego"), ("jpn", "Japonés"), ("kor", "Coreano"), ("chi", "Chino"),
        ("rus", "Ruso"), ("dut", "Neerlandés"), ("pol", "Polaco"), ("swe", "Sueco"),
        ("nor", "Noruego"), ("dan", "Danés"), ("fin", "Finés"), ("tur", "Turco"),
        ("ara", "Árabe"), ("hin", "Hindi"), ("gre", "Griego"), ("cze", "Checo"),
        ("hun", "Húngaro"), ("rum", "Rumano"), ("ukr", "Ucraniano"), ("heb", "Hebreo"),
        ("tha", "Tailandés"), ("vie", "Vietnamita"),
    };

    // Equivalencias entre el código terminológico (T) y el bibliográfico (B).
    private static readonly Dictionary<string, string> TerminologicToBibliographic = new()
    {
        ["fra"] = "fre", ["deu"] = "ger", ["eus"] = "baq", ["zho"] = "chi", ["nld"] = "dut",
        ["ces"] = "cze", ["ell"] = "gre", ["ron"] = "rum", ["esl"] = "spa", ["sqi"] = "alb",
    };

    public static IReadOnlyList<LanguageOption> All { get; } =
        Known.Select(k => new LanguageOption(k.Code, k.Name)).OrderBy(l => l.Name, StringComparer.Create(new CultureInfo("es-ES"), true)).ToList();

    public static string Normalize(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return "und";
        code = code.Trim().ToLowerInvariant();
        if (code.Length == 2)
        {
            try
            {
                var ci = new CultureInfo(code);
                code = ci.ThreeLetterISOLanguageName;
            }
            catch (CultureNotFoundException) { return code; }
        }
        return TerminologicToBibliographic.TryGetValue(code, out var b) ? b : code;
    }

    public static bool Same(string? a, string? b) => Normalize(a) == Normalize(b);

    public static string DisplayName(string? code)
    {
        var n = Normalize(code);
        if (n == "und") return "Sin idioma";
        foreach (var k in Known) if (k.Code == n) return k.Name;
        return n;
    }

    /// <summary>Idioma del sistema operativo como valor inicial del idioma favorito.</summary>
    public static string FromOperatingSystem()
    {
        var n = Normalize(CultureInfo.InstalledUICulture.ThreeLetterISOLanguageName);
        return Known.Any(k => k.Code == n) ? n : "eng";
    }
}
