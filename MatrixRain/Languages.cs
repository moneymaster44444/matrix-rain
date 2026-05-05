namespace MatrixRain;

public enum LanguagePreset
{
    KoreanHangul,
    JapaneseKatakana,
    ChineseHanzi,
    Cyrillic,
    Arabic,
    Hebrew,
    Wingdings,
    Greek,
    MathSymbols,
    Mixed,
}

internal sealed class LanguageDefinition
{
    public LanguagePreset Preset { get; }
    public string DisplayName { get; }
    public char[] Chars { get; }
    public string[] FontPreferences { get; }

    public LanguageDefinition(
        LanguagePreset preset,
        string displayName,
        char[] chars,
        string[] fontPreferences)
    {
        Preset = preset;
        DisplayName = displayName;
        Chars = chars;
        FontPreferences = fontPreferences;
    }
}

internal static class Languages
{
    private static readonly Dictionary<LanguagePreset, LanguageDefinition> _cache = new();

    public static LanguageDefinition Get(LanguagePreset preset)
    {
        if (_cache.TryGetValue(preset, out var existing)) return existing;
        var built = Build(preset);
        _cache[preset] = built;
        return built;
    }

    public static IEnumerable<(LanguagePreset Preset, string DisplayName)> All()
    {
        // Alphabetised by display name.
        yield return (LanguagePreset.Arabic, "Arabic");
        yield return (LanguagePreset.ChineseHanzi, "Chinese (Hanzi)");
        yield return (LanguagePreset.Cyrillic, "Cyrillic");
        yield return (LanguagePreset.Greek, "Greek");
        yield return (LanguagePreset.Hebrew, "Hebrew");
        yield return (LanguagePreset.JapaneseKatakana, "Japanese (Katakana)");
        yield return (LanguagePreset.KoreanHangul, "Korean (Hangul)");
        yield return (LanguagePreset.MathSymbols, "Math & Geometry");
        yield return (LanguagePreset.Mixed, "Mixed (Multi-script)");
        yield return (LanguagePreset.Wingdings, "Wingdings (Symbols)");
    }

    private static LanguageDefinition Build(LanguagePreset preset) => preset switch
    {
        LanguagePreset.KoreanHangul => new LanguageDefinition(
            preset, "Korean (Hangul)",
            BuildRange(0xAC00, 0xD7A3),
            new[] { "Malgun Gothic", "Gulim", "Batang", "Dotum", "MS Gothic", "Consolas" }),

        LanguagePreset.JapaneseKatakana => new LanguageDefinition(
            preset, "Japanese (Katakana)",
            BuildJapaneseMatrix(),
            new[] { "MS Gothic", "Yu Gothic", "Meiryo", "Consolas" }),

        LanguagePreset.ChineseHanzi => new LanguageDefinition(
            preset, "Chinese (Hanzi)",
            // CJK Unified Ideographs (~20k common characters).
            BuildRange(0x4E00, 0x9FFF),
            new[] { "Microsoft YaHei", "SimSun", "NSimSun", "MingLiU", "MS Gothic", "Consolas" }),

        LanguagePreset.Cyrillic => new LanguageDefinition(
            preset, "Cyrillic",
            BuildCyrillic(),
            new[] { "Consolas", "Cascadia Mono", "Lucida Console", "Courier New" }),

        LanguagePreset.Arabic => new LanguageDefinition(
            preset, "Arabic",
            BuildArabic(),
            new[] { "Tahoma", "Arial", "Segoe UI", "Microsoft Sans Serif" }),

        LanguagePreset.Hebrew => new LanguageDefinition(
            preset, "Hebrew",
            BuildHebrew(),
            // David is a traditional Hebrew typeface; Tahoma/Arial have
            // excellent Hebrew coverage and are present on every Windows.
            new[] { "David", "Tahoma", "Arial", "Segoe UI" }),

        LanguagePreset.Wingdings => new LanguageDefinition(
            preset, "Wingdings (Symbols)",
            BuildWingdings(),
            new[] { "Wingdings" }),

        LanguagePreset.Greek => new LanguageDefinition(
            preset, "Greek",
            BuildGreek(),
            new[] { "Consolas", "Cascadia Mono", "Lucida Console", "Courier New" }),

        LanguagePreset.MathSymbols => new LanguageDefinition(
            preset, "Math & Geometry",
            BuildMathSymbols(),
            new[] { "Cambria Math", "Segoe UI Symbol", "Consolas", "Lucida Console" }),

        LanguagePreset.Mixed => new LanguageDefinition(
            preset, "Mixed (Multi-script)",
            BuildMixed(),
            // Arial Unicode MS has the broadest BMP coverage on Windows; Segoe
            // UI Historic falls back gracefully for unusual scripts.
            new[] { "Arial Unicode MS", "Segoe UI Historic", "Segoe UI", "Arial", "Consolas" }),

        _ => Build(LanguagePreset.KoreanHangul),
    };

    private static char[] BuildRange(int startInclusive, int endInclusive)
    {
        var arr = new char[endInclusive - startInclusive + 1];
        for (int i = 0; i < arr.Length; i++) arr[i] = (char)(startInclusive + i);
        return arr;
    }

    private static char[] BuildJapaneseMatrix()
    {
        // The exact 32 half-width katakana scanned (and partly mirrored) by
        // production designer Simon Whiteley for The Matrix's code rain,
        // plus Latin numerals and the handful of Latin letters/symbols
        // ("Z : ・ . = * + - < > ¦") that also appear in the rain.
        // Source: documented breakdowns of the "Matrix Code NFI" font.
        var list = new List<char>();
        foreach (var c in "ﾊﾐﾋｰｳｼﾅﾓﾆｻﾜﾂｵﾘｱﾎﾃﾏｹﾒｴｶｷﾑﾕﾗｾﾈｽﾀﾇﾍ") list.Add(c);
        for (char c = '0'; c <= '9'; c++) list.Add(c);
        foreach (var c in "Z:・.=*+-<>¦") list.Add(c);
        return list.ToArray();
    }

    private static char[] BuildCyrillic()
    {
        // Comprehensive Cyrillic block — covers Russian (incl. Ё, Ъ, Ы, Э),
        // Ukrainian (Ґ, Є, І, Ї), Belarusian (Ў), Serbian (Ј, Љ, Њ, Ћ, Џ),
        // and other Slavic / non-Slavic extensions in U+0490..U+04FF.
        var list = new List<char>();
        // Basic Cyrillic uppercase + lowercase
        for (int i = 0x0410; i <= 0x044F; i++) list.Add((char)i);
        // Cyrillic supplement: Ё, Ѓ, Љ, etc.
        for (int i = 0x0400; i <= 0x040F; i++) list.Add((char)i);
        for (int i = 0x0450; i <= 0x045F; i++) list.Add((char)i);
        // Extended Cyrillic: Ґ, Ў, and many more
        for (int i = 0x0490; i <= 0x04FF; i++) list.Add((char)i);
        return list.ToArray();
    }

    private static char[] BuildArabic()
    {
        var list = new List<char>();
        // Arabic letters in their isolated forms — Arabic shaping doesn't
        // happen with single chars, but isolated forms still look distinctly
        // Arabic and work in the matrix-rain context.
        for (int i = 0x0621; i <= 0x064A; i++) list.Add((char)i);
        // Arabic-Indic digits
        for (int i = 0x0660; i <= 0x0669; i++) list.Add((char)i);
        // Additional letters used in Persian, Urdu, etc.
        for (int i = 0x06A0; i <= 0x06D3; i++) list.Add((char)i);
        return list.ToArray();
    }

    private static char[] BuildHebrew()
    {
        // U+05D0..U+05EA — the 22 base consonants plus the 5 distinct
        // word-final letterforms (ך ם ן ף ץ). Skipping niqqud (vowel marks)
        // since they're combining characters that wouldn't render as
        // standalone glyphs.
        var list = new List<char>();
        for (int i = 0x05D0; i <= 0x05EA; i++) list.Add((char)i);
        return list.ToArray();
    }

    private static char[] BuildWingdings()
    {
        // The Wingdings font remaps printable ASCII to symbols. Skip space
        // and DEL. The chars look like normal letters in any other font but
        // render as symbols when our form picks the Wingdings font family.
        var list = new List<char>();
        for (int i = 0x21; i <= 0x7E; i++) list.Add((char)i);
        for (int i = 0xA1; i <= 0xFF; i++) list.Add((char)i);
        return list.ToArray();
    }

    private static char[] BuildGreek()
    {
        var list = new List<char>();
        // Uppercase (skip the reserved slot at U+03A2).
        for (int i = 0x0391; i <= 0x03A9; i++)
        {
            if (i == 0x03A2) continue;
            list.Add((char)i);
        }
        // Lowercase
        for (int i = 0x03B1; i <= 0x03C9; i++) list.Add((char)i);
        // Final sigma + a few extras
        list.Add('ς');
        for (int i = 0x03D0; i <= 0x03DF; i++) list.Add((char)i);
        return list.ToArray();
    }

    private static char[] BuildMathSymbols()
    {
        var list = new List<char>();
        // Mathematical Operators
        for (int i = 0x2200; i <= 0x22FF; i++) list.Add((char)i);
        // Arrows
        for (int i = 0x2190; i <= 0x21FF; i++) list.Add((char)i);
        // Box Drawing
        for (int i = 0x2500; i <= 0x257F; i++) list.Add((char)i);
        // Block Elements
        for (int i = 0x2580; i <= 0x259F; i++) list.Add((char)i);
        // Geometric Shapes
        for (int i = 0x25A0; i <= 0x25FF; i++) list.Add((char)i);
        // Miscellaneous Technical (gear, command key, etc.)
        for (int i = 0x2300; i <= 0x23FF; i++) list.Add((char)i);
        return list.ToArray();
    }

    private static char[] BuildMixed()
    {
        // A grab-bag chosen for visual variety. Caps the contribution from
        // any one script so no single script dominates the look.
        var list = new List<char>();
        AddSample(list, BuildRange(0xAC00, 0xD7A3), 200);   // Hangul (sampled)
        list.AddRange(BuildJapaneseMatrix());
        list.AddRange(BuildCyrillic());
        list.AddRange(BuildGreek());
        list.AddRange(BuildArabic());
        AddSample(list, BuildMathSymbols(), 200);
        // Runic — looks great in a matrix context.
        for (int i = 0x16A0; i <= 0x16F8; i++) list.Add((char)i);
        // Hebrew letters
        for (int i = 0x05D0; i <= 0x05EA; i++) list.Add((char)i);
        // Devanagari consonants & vowels (Hindi/Sanskrit) — visually striking.
        for (int i = 0x0905; i <= 0x0939; i++) list.Add((char)i);
        return list.ToArray();
    }

    private static void AddSample(List<char> list, char[] source, int count)
    {
        if (source.Length <= count)
        {
            list.AddRange(source);
            return;
        }
        // Evenly-spaced sample across the source range.
        int step = source.Length / count;
        for (int i = 0; i < count; i++)
            list.Add(source[i * step]);
    }
}
