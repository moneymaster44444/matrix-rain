// Matrix Rain settings. Edit the values below and save; Steam reloads the
// page each time the screensaver starts. A missing or invalid value falls
// back to its default without affecting the other settings, and numbers
// outside their range are clamped. Reinstalling never overwrites this file.
window.MATRIX_RAIN_CONFIG = {
  // Which script the rain is drawn from:
  //   arabic | hanzi | cyrillic | greek | hebrew | katakana | hangul | math |
  //   mixed | wingdings
  // "wingdings" uses Unicode dingbats and symbols, since the Wingdings font
  // isn't available on SteamOS. Default: "hangul".
  characters: "hangul",

  // 1-10. How many streams fall at once: 1 is sparse, 10 puts a stream in
  // every column. Default: 6.
  density: 6,

  // 0.1-5.0. Multiplier on each stream's fall rate. Lower is slower; above
  // about 1.5 every stream already falls one row per tick. Default: 0.4.
  speed: 0.4,

  // 0-100. Keeps at least this many drops (stream heads) on screen: when the
  // count dips, a waiting stream drops in at the top right away. 0 turns it
  // off (the Windows screensaver's behavior). Default: 6.
  minDrops: 6,

  // Bright trail color as "#RRGGBB" or "#RGB". The dim background
  // (color x 0.20) and the near-white head are derived from it.
  // Default: "#00FF41".
  color: "#00FF41",

  // true gives each new drop a random, fully saturated color, clearly
  // different from its column's current one. The text it leaves behind takes
  // that color (dimmed), so every drop wipes a new color down its column.
  // "color" is ignored while this is on. Default: false.
  rgbMode: false,

  // Glyph size. "auto" uses the Windows screensaver's 16 pt and scales it up
  // on screens taller than 1080 px (so a 4K TV gets 2x the size). Or set a
  // number of physical pixels, 8-200 (e.g. 42 for a 4K TV). Default: "auto".
  fontSize: "auto",

  // true shows an overlay with fps, timing, grid size and glyph coverage.
  // Useful for checking performance and fonts on the Deck. Default: false.
  debug: false
};
