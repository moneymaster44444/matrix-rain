using System.Text.Json;
using System.Text.Json.Serialization;

namespace MatrixRain;

public class Config
{
    // 1 = sparse rain columns, 10 = a stream in every column
    public int Density { get; set; } = 6;

    // Multiplier on each stream's per-row advance rate.
    // 0.1 = crawls (≈2–6 rows/sec), 1.0 = default, 5.0 = blistering.
    public double Speed { get; set; } = 1.0;

    // Which character set the rain draws from.
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public LanguagePreset Language { get; set; } = LanguagePreset.KoreanHangul;

    // Bright trail color (the "matrix green" by default — #00FF41). The
    // dim background and the near-white head are both derived from this.
    public byte ColorR { get; set; } = 0;
    public byte ColorG { get; set; } = 255;
    public byte ColorB { get; set; } = 65;

    private static string ConfigPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MatrixRain",
        "config.json");

    public static Config Load()
    {
        var c = new Config();
        try
        {
            if (!File.Exists(ConfigPath)) return c;
            var json = File.ReadAllText(ConfigPath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Per-property reads so e.g. an unknown enum string only
            // resets the Language field instead of wiping all settings.
            if (root.TryGetProperty(nameof(Density), out var d) && d.TryGetInt32(out var di))
                c.Density = di;
            if (root.TryGetProperty(nameof(Speed), out var s) && s.TryGetDouble(out var sv))
                c.Speed = sv;
            if (root.TryGetProperty(nameof(Language), out var l)
                && l.ValueKind == JsonValueKind.String
                && Enum.TryParse<LanguagePreset>(l.GetString(), out var lp))
            {
                c.Language = lp;
            }
            if (root.TryGetProperty(nameof(ColorR), out var cr) && cr.TryGetByte(out var crv)) c.ColorR = crv;
            if (root.TryGetProperty(nameof(ColorG), out var cg) && cg.TryGetByte(out var cgv)) c.ColorG = cgv;
            if (root.TryGetProperty(nameof(ColorB), out var cb) && cb.TryGetByte(out var cbv)) c.ColorB = cbv;
        }
        catch
        {
            // Malformed JSON — defaults already in place
        }
        return c;
    }

    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(ConfigPath)!;
            Directory.CreateDirectory(dir);
            File.WriteAllText(
                ConfigPath,
                JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Best-effort — silently swallow so a failed save can't crash the saver
        }
    }
}
