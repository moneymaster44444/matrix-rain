// Character-set presets, ported from MatrixRain/Languages.cs with the same
// code-point ranges. Each preset lists a font stack. Nothing is bundled: the
// stacks name fonts SteamOS ships (Noto, DejaVu), and the browser falls back
// per glyph to whatever else the system has (e.g. Malgun Gothic on Windows).
// main.js drops any glyph that still doesn't render, so a missing font thins
// the pool instead of drawing tofu boxes.
window.MATRIX_RAIN_CHARSETS = (function () {
    'use strict';

    function range(start, end, out) {
        out = out || [];
        for (let i = start; i <= end; i++) out.push(String.fromCharCode(i));
        return out;
    }

    function addAll(out, chars) {
        for (let i = 0; i < chars.length; i++) out.push(chars[i]);
        return out;
    }

    // Evenly-spaced sample across the source range (Languages.AddSample).
    function addSample(out, source, count) {
        if (source.length <= count) return addAll(out, source);
        const step = Math.floor(source.length / count);
        for (let i = 0; i < count; i++) out.push(source[i * step]);
        return out;
    }

    function hangul() { return range(0xAC00, 0xD7A3); }

    function katakana() {
        // The 32 half-width katakana from the film's code rain, plus digits
        // and the Latin letters/symbols that also appear in it.
        const out = addAll([], 'ﾊﾐﾋｰｳｼﾅﾓﾆｻﾜﾂｵﾘｱﾎﾃﾏｹﾒｴｶｷﾑﾕﾗｾﾈｽﾀﾇﾍ');
        range(0x30, 0x39, out);
        return addAll(out, 'Z:・.=*+-<>¦');
    }

    function cyrillic() {
        const out = range(0x0410, 0x044F);
        range(0x0400, 0x040F, out);
        range(0x0450, 0x045F, out);
        return range(0x0490, 0x04FF, out);
    }

    function arabic() {
        const out = range(0x0621, 0x064A);
        range(0x0660, 0x0669, out);
        return range(0x06A0, 0x06D3, out);
    }

    function hebrew() { return range(0x05D0, 0x05EA); }

    function greek() {
        const out = [];
        for (let i = 0x0391; i <= 0x03A9; i++) {
            if (i !== 0x03A2) out.push(String.fromCharCode(i));
        }
        range(0x03B1, 0x03C9, out);
        out.push('ς');
        return range(0x03D0, 0x03DF, out);
    }

    function math() {
        const out = range(0x2200, 0x22FF);
        range(0x2190, 0x21FF, out);
        range(0x2500, 0x257F, out);
        range(0x2580, 0x259F, out);
        range(0x25A0, 0x25FF, out);
        return range(0x2300, 0x23FF, out);
    }

    function symbols() {
        // Wingdings substitute: the Wingdings font doesn't exist on SteamOS, so
        // use the Unicode Dingbats and Miscellaneous Symbols blocks (scissors,
        // pencils, check marks, hands, stars, circled digits, arrows...), which
        // cover much of what Wingdings draws. Colour-emoji glyphs are filtered
        // out at startup because they can't take the trail colour.
        const out = range(0x2701, 0x27BF);
        return range(0x2600, 0x26FF, out);
    }

    function mixed() {
        const out = [];
        addSample(out, hangul(), 200);
        addAll(out, katakana());
        addAll(out, cyrillic());
        addAll(out, greek());
        addAll(out, arabic());
        addSample(out, math(), 200);
        range(0x16A0, 0x16F8, out);   // Runic
        range(0x05D0, 0x05EA, out);   // Hebrew
        return range(0x0905, 0x0939, out);  // Devanagari
    }

    const MONO = '"Noto Sans Mono", "DejaVu Sans Mono", monospace';

    return {
        arabic:    { name: 'Arabic', fonts: '"Noto Sans Arabic", "Noto Naskh Arabic", "DejaVu Sans", sans-serif', build: arabic },
        hanzi:     { name: 'Chinese (Hanzi)', fonts: '"Noto Sans CJK SC", "Noto Sans SC", sans-serif', build: function () { return range(0x4E00, 0x9FFF); } },
        cyrillic:  { name: 'Cyrillic', fonts: MONO, build: cyrillic },
        greek:     { name: 'Greek', fonts: MONO, build: greek },
        hebrew:    { name: 'Hebrew', fonts: '"Noto Sans Hebrew", "DejaVu Sans", sans-serif', build: hebrew },
        katakana:  { name: 'Japanese (Katakana)', fonts: '"Noto Sans CJK JP", "Noto Sans JP", sans-serif', build: katakana },
        hangul:    { name: 'Korean (Hangul)', fonts: '"Noto Sans CJK KR", "Noto Sans KR", sans-serif', build: hangul },
        math:      { name: 'Math & Geometry', fonts: '"Noto Sans Math", "Noto Sans Symbols", "Noto Sans Symbols 2", "DejaVu Sans", sans-serif', build: math },
        mixed:     { name: 'Mixed (Multi-script)', fonts: '"Noto Sans", "Noto Sans CJK KR", "Noto Sans CJK JP", "Noto Sans Arabic", "Noto Sans Hebrew", "Noto Sans Runic", "Noto Sans Devanagari", "Noto Sans Math", "Noto Sans Symbols", "DejaVu Sans", sans-serif', build: mixed },
        wingdings: { name: 'Symbols (Wingdings substitute)', fonts: '"Noto Sans Symbols 2", "Noto Sans Symbols", "DejaVu Sans", sans-serif', build: symbols },
    };
})();
