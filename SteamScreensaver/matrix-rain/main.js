// Matrix Rain for Steam Big Picture / Steam Deck.
// A port of MatrixRain/MatrixRainForm.cs. The simulation runs on a fixed tick
// (the .scr's timer), decoupled from requestAnimationFrame, and rendering is
// capped at ~30 fps.
(function () {
    'use strict';

    // ---- Timing --------------------------------------------------------------
    // The .scr asks WinForms for a 16 ms timer, but Windows' 15.6 ms timer
    // granularity makes it fire every ~25 ms on average (measured). 25 ms
    // matches what the .scr actually looks like on screen.
    const TICK_MS = 25;
    const MAX_STEP_MS = 100;           // hitch clamp, same as Valve's example
    const FRAME_MS = 1000 / 30;        // render cap
    const FRAME_SLACK_MS = 4;          // so a 60 Hz rAF lands on every 2nd frame

    // ---- Simulation constants (frame = one tick) ------------------------------
    const TRAIL_LENGTH = 20;
    const HEAD_FLICKER_INTERVAL = 2;
    const BURST_CHANGE_INTERVAL = 4;
    const BURST_MIN_FRAMES = 125;
    const BURST_MAX_FRAMES = 188;
    const BURST_CELLS_PER_BURST = 200;
    const BURST_SEED_ATTEMPTS = 64;
    const SINGLE_FLIP_CELLS_PER_FLIP = 2500;
    const VOID_STREAM_CHANCE = 0.15;
    const RESPAWN_CHANCE = 0.04;
    const RESET_DELAY_MAX = 90;        // exclusive
    const FREE_COLUMN_ATTEMPTS = 16;
    // Not in the .scr: keep at least cfg.minDrops heads on screen. A stream
    // spends a lot of its cycle above the screen or clearing its tail off the
    // bottom, so keep this many streams per required drop. The first fill is
    // staggered by this many ticks so drops don't enter in a line.
    const MIN_DROPS_STREAMS_PER_DROP = 3;
    const MIN_DROPS_INTERVAL = 4;

    // ---- Layout --------------------------------------------------------------
    const BASE_FONT_PX = 16 * 96 / 72; // the .scr's 16 pt at 96 DPI
    const REFERENCE_HEIGHT = 1080;     // "auto" font size scales above this
    const CELL_WIDTH_SAMPLES = 80;
    const FONT_LOAD_TIMEOUT_MS = 3000;
    const RESIZE_DEBOUNCE_MS = 150;

    const DEFAULTS = {
        characters: 'hangul',
        density: 6,
        speed: 0.4,
        minDrops: 6,
        color: '#00FF41',
        rgbMode: false,
        fontSize: 'auto',
        debug: false,
    };

    const CHARSETS = window.MATRIX_RAIN_CHARSETS;

    // ---- Config --------------------------------------------------------------
    // Per-field resilient, like Config.Load(): a bad field falls back to its
    // own default. URL params override config.js for desktop-browser testing
    // (e.g. index.html?characters=greek&fps).
    function toNumber(v) {
        if (typeof v === 'number') return v;
        if (typeof v === 'string' && v.trim() !== '') return Number(v);
        return NaN;
    }

    function clamp(v, lo, hi) { return v < lo ? lo : v > hi ? hi : v; }

    function parseColor(v) {
        if (typeof v !== 'string') return null;
        const m = /^#?([0-9a-f]{3}|[0-9a-f]{6})$/i.exec(v.trim());
        if (!m) return null;
        let hex = m[1];
        if (hex.length === 3) hex = hex[0] + hex[0] + hex[1] + hex[1] + hex[2] + hex[2];
        const n = parseInt(hex, 16);
        return { r: (n >> 16) & 255, g: (n >> 8) & 255, b: n & 255 };
    }

    // Load config.js with a cache-busting query so edits show up the next time
    // the screensaver starts. If the host rejects the query, retry plainly; if
    // that fails too, readConfig() falls back to defaults.
    function loadScript(src) {
        return new Promise(resolve => {
            const s = document.createElement('script');
            s.src = src;
            s.onload = () => resolve(true);
            s.onerror = () => { s.remove(); resolve(false); };
            document.head.appendChild(s);
        });
    }

    async function loadConfigScript() {
        if (!await loadScript('config.js?t=' + Date.now())) await loadScript('config.js');
    }

    function readConfig() {
        let raw = {};
        try {
            const c = window.MATRIX_RAIN_CONFIG;
            if (c && typeof c === 'object') raw = Object.assign({}, c);
        } catch (e) { /* defaults */ }

        const params = new URLSearchParams(window.location.search);
        for (const key of Object.keys(DEFAULTS)) {
            if (params.has(key)) raw[key] = params.get(key);
        }
        if (params.has('fps')) raw.debug = true;

        const cfg = {};

        const ch = typeof raw.characters === 'string' ? raw.characters.trim().toLowerCase() : '';
        cfg.characters = Object.prototype.hasOwnProperty.call(CHARSETS, ch) ? ch : DEFAULTS.characters;

        const d = toNumber(raw.density);
        cfg.density = Number.isFinite(d) ? clamp(Math.round(d), 1, 10) : DEFAULTS.density;

        const s = toNumber(raw.speed);
        cfg.speed = Number.isFinite(s) ? clamp(s, 0.1, 5.0) : DEFAULTS.speed;

        const md = toNumber(raw.minDrops);
        cfg.minDrops = Number.isFinite(md) ? clamp(Math.round(md), 0, 100) : DEFAULTS.minDrops;

        cfg.color = parseColor(raw.color) || parseColor(DEFAULTS.color);
        cfg.rgbMode = toBool(raw.rgbMode);

        const fsRaw = raw.fontSize;
        const fs = toNumber(fsRaw);
        if (typeof fsRaw === 'string' && fsRaw.trim().toLowerCase() === 'auto') cfg.fontSize = 'auto';
        else cfg.fontSize = Number.isFinite(fs) ? clamp(Math.round(fs), 8, 200) : DEFAULTS.fontSize;

        cfg.debug = toBool(raw.debug);

        return cfg;
    }

    // true, or "true" / "1" / "" from a URL param (?debug).
    function toBool(v) {
        return v === true || v === 'true' || v === '1' || v === '';
    }

    // ---- Palette ---------------------------------------------------------------
    // C#'s Math.Round rounds half to even; keep that so colours and per-stream
    // rates match the .scr exactly.
    function roundHalfEven(x) {
        const f = Math.floor(x);
        const d = x - f;
        if (d > 0.5) return f + 1;
        if (d < 0.5) return f;
        return f % 2 === 0 ? f : f + 1;
    }

    function rgb(r, g, b) { return 'rgb(' + r + ',' + g + ',' + b + ')'; }

    function buildPalette(c) {
        const dim = {
            r: roundHalfEven(c.r * 0.20),
            g: roundHalfEven(c.g * 0.20),
            b: roundHalfEven(c.b * 0.20),
        };
        const head = rgb(
            roundHalfEven(c.r * 0.15 + 255 * 0.85),
            roundHalfEven(c.g * 0.15 + 255 * 0.85),
            roundHalfEven(c.b * 0.15 + 255 * 0.85));

        // Index 0 = head, 1..N-1 = bright fading to dim (or to black for void
        // streams). The .scr computes these in single precision.
        const f = Math.fround;
        const lerp = (a, b, t) => roundHalfEven(f(f(f(1 - t) * a) + f(t * b)));
        const trail = [head];
        const voidTrail = [head];
        for (let i = 1; i < TRAIL_LENGTH; i++) {
            const t = f((i - 1) / (TRAIL_LENGTH - 1));
            trail.push(rgb(lerp(c.r, dim.r, t), lerp(c.g, dim.g, t), lerp(c.b, dim.b, t)));
            voidTrail.push(rgb(lerp(c.r, 0, t), lerp(c.g, 0, t), lerp(c.b, 0, t)));
        }
        return { dim: rgb(dim.r, dim.g, dim.b), trail, voidTrail };
    }

    // RGB mode: every new drop picks a random, fully saturated colour (and the
    // palette derived from it), clearly different from the colour its column
    // already has. The cells it passes take that colour, so each drop wipes
    // the column's old colour down the screen. Palettes are precomputed for a
    // fixed set of hues so spawning allocates nothing.
    const RGB_HUE_STEPS = 72;          // 5° apart
    const RGB_MIN_HUE_STEPS = 8;       // 40° minimum change per column
    const RGB_HUE_ATTEMPTS = 16;

    function hueToRgb(h) {
        const x = (h / 60) % 6;
        const i = Math.floor(x);
        const up = Math.round(255 * (x - i));
        const down = 255 - up;
        switch (i) {
            case 0: return { r: 255, g: up, b: 0 };
            case 1: return { r: down, g: 255, b: 0 };
            case 2: return { r: 0, g: 255, b: up };
            case 3: return { r: 0, g: down, b: 255 };
            case 4: return { r: up, g: 0, b: 255 };
            default: return { r: 255, g: 0, b: down };
        }
    }

    // palettes[0] is the picked colour; in RGB mode palettes[k] is hue k * 5°.
    function buildPalettes() {
        if (!cfg.rgbMode) return [buildPalette(cfg.color)];
        const out = [];
        for (let k = 0; k < RGB_HUE_STEPS; k++) {
            out.push(buildPalette(hueToRgb((k * 360) / RGB_HUE_STEPS)));
        }
        return out;
    }

    // Palette for a new drop in this column.
    function pickPalette(col) {
        if (!cfg.rgbMode) return 0;
        const prev = columnPalette[col];
        for (let attempt = 0; attempt < RGB_HUE_ATTEMPTS; attempt++) {
            const k = rand(RGB_HUE_STEPS);
            if (prev === NO_PALETTE) return k;
            const d = Math.abs(k - prev);
            if (Math.min(d, RGB_HUE_STEPS - d) >= RGB_MIN_HUE_STEPS) return k;
        }
        return (prev + RGB_HUE_STEPS / 2) % RGB_HUE_STEPS;
    }

    // ---- Glyph coverage --------------------------------------------------------
    // Without bundled fonts, some glyphs may have no font on this system. Render
    // every candidate once, small, and drop glyphs that come out blank, match the
    // missing-glyph box of an unassigned code point, or are colour emoji (which
    // can't take the trail colour).
    const PROBE_PX = 12;
    const PROBE_CELL = 24;
    const PROBE_COLS = 64;
    const PROBE_ROWS = 32;
    const PROBE_BATCH = PROBE_COLS * PROBE_ROWS;
    const UNASSIGNED = ['͸', '׾', '௾'];

    // Yield between probe batches. setTimeout rather than rAF, which may not
    // tick until the page is actually on screen.
    function yieldToBrowser() {
        return new Promise(resolve => setTimeout(resolve, 0));
    }

    function probeSignature(data, width, k) {
        const x0 = (k % PROBE_COLS) * PROBE_CELL;
        const y0 = ((k / PROBE_COLS) | 0) * PROBE_CELL;
        let hash = 2166136261;
        let ink = 0;
        for (let y = 0; y < PROBE_CELL; y++) {
            let p = ((y0 + y) * width + x0) * 4;
            for (let x = 0; x < PROBE_CELL; x++, p += 4) {
                const a = data[p + 3];
                if (a !== 0) {
                    ink++;
                    // Probes are drawn pure red, so any green or blue means the
                    // glyph has its own colours (emoji, even grey ones).
                    if (a > 64 && (data[p + 1] > 48 || data[p + 2] > 48)) return -1;
                }
                hash = Math.imul(hash ^ a, 16777619);
            }
        }
        return ink === 0 ? -1 : hash >>> 0;
    }

    function drawProbeBatch(g, chars, start, end) {
        g.clearRect(0, 0, g.canvas.width, g.canvas.height);
        for (let i = start; i < end; i++) {
            const k = i - start;
            g.fillText(chars[i],
                (k % PROBE_COLS) * PROBE_CELL + PROBE_CELL / 2,
                ((k / PROBE_COLS) | 0) * PROBE_CELL + PROBE_CELL / 2);
        }
        return g.getImageData(0, 0, g.canvas.width, g.canvas.height).data;
    }

    async function filterRenderable(chars, fonts) {
        const c = document.createElement('canvas');
        c.width = PROBE_COLS * PROBE_CELL;
        c.height = PROBE_ROWS * PROBE_CELL;
        const g = c.getContext('2d', { willReadFrequently: true });
        g.font = PROBE_PX + 'px ' + fonts;
        g.textAlign = 'center';
        g.textBaseline = 'middle';
        g.fillStyle = '#f00';

        const missing = new Set();
        const refData = drawProbeBatch(g, UNASSIGNED, 0, UNASSIGNED.length);
        for (let k = 0; k < UNASSIGNED.length; k++) {
            const sig = probeSignature(refData, c.width, k);
            if (sig !== -1) missing.add(sig);
        }

        const keep = [];
        for (let start = 0; start < chars.length; start += PROBE_BATCH) {
            const end = Math.min(chars.length, start + PROBE_BATCH);
            const data = drawProbeBatch(g, chars, start, end);
            for (let i = start; i < end; i++) {
                const sig = probeSignature(data, c.width, i - start);
                if (sig !== -1 && !missing.has(sig)) keep.push(chars[i]);
            }
            await yieldToBrowser();
        }
        return keep;
    }

    async function waitForFonts(font, sample) {
        if (!document.fonts || !document.fonts.load) return;
        const timeout = new Promise(resolve => setTimeout(resolve, FONT_LOAD_TIMEOUT_MS));
        try {
            await Promise.race([document.fonts.load(font, sample), timeout]);
        } catch (e) { /* draw with whatever is available */ }
    }

    // ---- State -----------------------------------------------------------------
    const canvas = document.getElementById('rain');
    const ctx = canvas.getContext('2d', { alpha: false });
    // Persistent dim layer; only cells that change get repainted.
    const dimCanvas = document.createElement('canvas');
    const dimCtx = dimCanvas.getContext('2d', { alpha: false });

    let cfg, palettes, charset, pool, poolTotal, fontStack;
    const NO_PALETTE = 255;
    let cellPalette = new Uint8Array(0);    // palette of the drop that last froze each cell
    let columnPalette = new Uint8Array(0);  // palette of each column's latest drop
    let fontPx = 0, cellW = 1, cellH = 1, baseline = 0, cols = 0, rows = 0, dpr = 1;

    let grid = new Uint16Array(0);      // index into pool, per cell (row-major)
    let revealed = new Uint8Array(0);   // a head has passed and left dim text
    let dirtyFlag = new Uint8Array(0);
    let dirtyList = new Int32Array(0);
    let dirtyCount = 0;
    let columnActive = new Uint8Array(0);

    // Streams (struct of arrays).
    let streamCount = 0;
    let sCol, sHead, sStep, sCounter, sReset, sActive, sHeadChar, sFlicker, sVoid, sPalette;

    // Background churn bursts.
    let burstCount = 0, burstCap = 0;
    let bCell, bUntil, bRemaining;

    function rand(n) { return (Math.random() * n) | 0; }
    function randRange(lo, hiExclusive) { return lo + rand(hiExclusive - lo); }
    function randomChar() { return rand(pool.length); }

    function markDirty(idx) {
        if (dirtyFlag[idx]) return;
        dirtyFlag[idx] = 1;
        dirtyList[dirtyCount++] = idx;
    }

    // ---- Layout / resize ---------------------------------------------------------
    function computeFontPx(heightPx) {
        if (cfg.fontSize !== 'auto') return cfg.fontSize;
        return BASE_FONT_PX * Math.max(dpr, heightPx / REFERENCE_HEIGHT);
    }

    function setupTextContext(g) {
        g.font = fontPx.toFixed(2) + 'px ' + fontStack;
        g.textAlign = 'left';
        g.textBaseline = 'alphabetic';
        g.direction = 'ltr';
    }

    function measureCells() {
        // Most scripts aren't monospaced, so sample many glyphs and use the
        // widest as the cell width (same as the .scr).
        let maxW = 0, maxAsc = 0, maxDesc = 0, fontAsc = 0, fontDesc = 0;
        const samples = Math.min(CELL_WIDTH_SAMPLES, pool.length);
        for (let i = 0; i < samples; i++) {
            const m = ctx.measureText(pool[randomChar()]);
            if (m.width > maxW) maxW = m.width;
            if (m.actualBoundingBoxAscent > maxAsc) maxAsc = m.actualBoundingBoxAscent;
            if (m.actualBoundingBoxDescent > maxDesc) maxDesc = m.actualBoundingBoxDescent;
            if (m.fontBoundingBoxAscent > fontAsc) fontAsc = m.fontBoundingBoxAscent;
            if (m.fontBoundingBoxDescent > fontDesc) fontDesc = m.fontBoundingBoxDescent;
        }
        cellW = Math.max(1, Math.ceil(maxW));
        const asc = Math.max(fontAsc, maxAsc);
        const desc = Math.max(fontDesc, maxDesc);
        cellH = Math.max(1, Math.round(asc + desc));
        baseline = Math.round((cellH - (asc + desc)) / 2 + asc);
    }

    function layout() {
        dpr = window.devicePixelRatio || 1;
        const w = Math.max(1, Math.round(window.innerWidth * dpr));
        const h = Math.max(1, Math.round(window.innerHeight * dpr));

        canvas.width = w;
        canvas.height = h;
        dimCanvas.width = w;
        dimCanvas.height = h;
        fontPx = computeFontPx(h);
        setupTextContext(ctx);
        setupTextContext(dimCtx);
        measureCells();

        const newCols = Math.max(1, Math.floor(w / cellW));
        const newRows = Math.max(1, Math.floor(h / cellH));
        const newGrid = new Uint16Array(newCols * newRows);
        const newRevealed = new Uint8Array(newCols * newRows);
        const newCellPalette = new Uint8Array(newCols * newRows);
        const newColumnPalette = new Uint8Array(newCols).fill(NO_PALETTE);
        for (let i = 0; i < newGrid.length; i++) newGrid[i] = randomChar();
        // Keep whatever overlaps the old grid so a resize (e.g. docking the
        // Deck) doesn't wipe the screen back to black.
        for (let r = 0; r < Math.min(rows, newRows); r++) {
            for (let c = 0; c < Math.min(cols, newCols); c++) {
                newGrid[r * newCols + c] = grid[r * cols + c];
                newRevealed[r * newCols + c] = revealed[r * cols + c];
                newCellPalette[r * newCols + c] = cellPalette[r * cols + c];
            }
        }
        for (let c = 0; c < Math.min(cols, newCols); c++) newColumnPalette[c] = columnPalette[c];
        cols = newCols;
        rows = newRows;
        grid = newGrid;
        revealed = newRevealed;
        cellPalette = newCellPalette;
        columnPalette = newColumnPalette;
        dirtyFlag = new Uint8Array(cols * rows);
        dirtyList = new Int32Array(cols * rows);
        dirtyCount = 0;

        // The canvases were reset to black by the resize; repaint revealed cells.
        for (let i = 0; i < revealed.length; i++) {
            if (revealed[i]) markDirty(i);
        }

        burstCap = Math.max(1, Math.floor((cols * rows) / BURST_CELLS_PER_BURST));
        bCell = new Int32Array(burstCap);
        bUntil = new Int32Array(burstCap);
        bRemaining = new Int32Array(burstCap);
        burstCount = 0;

        initStreams();
    }

    // ---- Streams -----------------------------------------------------------------
    function framesPerStep() {
        // Base advance rate is 1–3 frames per row, scaled by the speed
        // multiplier (single precision in the .scr).
        const speedMul = Math.fround(cfg.speed);
        const baseFps = 1 + rand(3);
        return Math.max(1, roundHalfEven(Math.fround(baseFps / speedMul)));
    }

    // A random column not occupied by an active stream, falling back to a
    // linear scan and then to any column.
    function pickFreeColumn() {
        for (let attempt = 0; attempt < FREE_COLUMN_ATTEMPTS; attempt++) {
            const c = rand(cols);
            if (!columnActive[c]) return c;
        }
        for (let c = 0; c < cols; c++) {
            if (!columnActive[c]) return c;
        }
        return rand(cols);
    }

    function initStreams() {
        // Density sets how many streams fall at once; each roams to a random
        // free column whenever it (re)spawns.
        const spacing = Math.max(1, 11 - cfg.density);
        const reserve = cfg.minDrops * MIN_DROPS_STREAMS_PER_DROP;
        streamCount = clamp(Math.max(Math.floor(cols / spacing), reserve), 1, cols);
        minDropsFilled = false;
        minDropsCooldown = 0;
        columnActive = new Uint8Array(cols);
        sCol = new Int32Array(streamCount);
        sHead = new Int32Array(streamCount);
        sStep = new Int32Array(streamCount);
        sCounter = new Int32Array(streamCount);
        sReset = new Int32Array(streamCount);
        sActive = new Uint8Array(streamCount);
        sHeadChar = new Uint16Array(streamCount);
        sFlicker = new Int32Array(streamCount);
        sVoid = new Uint8Array(streamCount);
        sPalette = new Uint8Array(streamCount);

        for (let i = 0; i < streamCount; i++) {
            const col = pickFreeColumn();
            columnActive[col] = 1;
            const step = framesPerStep();
            sCol[i] = col;
            assignPalette(i, col);
            // Negative head row = still above the screen; the random offset
            // desynchronises streams.
            sHead[i] = -rand(rows + TRAIL_LENGTH);
            sStep[i] = step;
            sCounter[i] = rand(step);
            sReset[i] = 0;
            sActive[i] = 1;
            sHeadChar[i] = randomChar();
            sFlicker[i] = 0;
            sVoid[i] = Math.random() < VOID_STREAM_CHANCE ? 1 : 0;
        }
    }

    function assignPalette(i, col) {
        sPalette[i] = pickPalette(col);
        columnPalette[col] = sPalette[i];
    }

    // Respawn an idle stream in a new random free column.
    function respawnStream(i, headRow) {
        const col = pickFreeColumn();
        columnActive[col] = 1;
        sCol[i] = col;
        assignPalette(i, col);
        sHead[i] = headRow;
        sActive[i] = 1;
        sStep[i] = framesPerStep();
        sCounter[i] = 0;
        sHeadChar[i] = randomChar();
        sFlicker[i] = 0;
        sVoid[i] = Math.random() < VOID_STREAM_CHANCE ? 1 : 0;
    }

    // Keep at least cfg.minDrops heads on screen: bring in the stream that is
    // closest to entering from above, or else an idle one, at the top row.
    let minDropsCooldown = 0;
    let minDropsFilled = false;

    function enforceMinDrops() {
        const target = Math.min(cfg.minDrops, streamCount);
        let visible = countVisibleDrops();
        if (visible >= target) {
            minDropsFilled = true;
            return;
        }
        if (!minDropsFilled) {
            if (minDropsCooldown > 0) {
                minDropsCooldown--;
                return;
            }
            minDropsCooldown = MIN_DROPS_INTERVAL - 1;
            bringInDrop();
            return;
        }
        while (visible < target && bringInDrop()) visible++;
    }

    function bringInDrop() {
        let pick = -1;
        for (let i = 0; i < streamCount; i++) {
            if (sActive[i] && sHead[i] < 0 && (pick === -1 || sHead[i] > sHead[pick])) pick = i;
        }
        if (pick !== -1) {
            sHead[pick] = 0;
        } else {
            for (let i = 0; i < streamCount; i++) {
                if (!sActive[i]) { pick = i; break; }
            }
            if (pick === -1) return false;
            respawnStream(pick, 0);
        }
        return true;
    }

    // ---- Simulation tick (MatrixRainForm.Step) ---------------------------------
    function tick() {
        // 1. Advance streams.
        for (let i = 0; i < streamCount; i++) {
            if (!sActive[i]) {
                if (sReset[i] > 0) {
                    sReset[i]--;
                    continue;
                }
                if (Math.random() < RESPAWN_CHANCE) {
                    respawnStream(i, -rand(((rows / 2) | 0) + TRAIL_LENGTH));
                }
                continue;
            }

            // The head re-rolls its glyph on its own interval, independent of
            // the fall rate.
            if (++sFlicker[i] >= HEAD_FLICKER_INTERVAL) {
                sFlicker[i] = 0;
                sHeadChar[i] = randomChar();
            }

            if (++sCounter[i] >= sStep[i]) {
                sCounter[i] = 0;
                const head = sHead[i];
                // Freeze the head's last glyph into the cell it leaves. Normal
                // streams reveal it; void streams return it to black.
                if (head >= 0 && head < rows) {
                    const idx = head * cols + sCol[i];
                    grid[idx] = sHeadChar[i];
                    cellPalette[idx] = sPalette[i];
                    revealed[idx] = sVoid[i] ? 0 : 1;
                    markDirty(idx);
                }
                sHead[i] = head + 1;
                sHeadChar[i] = randomChar();

                if (sHead[i] - TRAIL_LENGTH >= rows) {
                    sActive[i] = 0;
                    sReset[i] = rand(RESET_DELAY_MAX);
                    columnActive[sCol[i]] = 0;
                }
            }
        }
        if (cfg.minDrops > 0) enforceMinDrops();

        // 2. Localized churn bursts, seeded only on revealed cells.
        const cells = cols * rows;
        for (let attempt = 0; burstCount < burstCap && attempt < BURST_SEED_ATTEMPTS; attempt++) {
            const idx = rand(rows) * cols + rand(cols);
            if (!revealed[idx]) continue;
            bCell[burstCount] = idx;
            bUntil[burstCount] = 0;
            bRemaining[burstCount] = randRange(BURST_MIN_FRAMES, BURST_MAX_FRAMES + 1);
            burstCount++;
        }

        for (let i = burstCount - 1; i >= 0; i--) {
            if (--bRemaining[i] <= 0) {
                // Swap-remove; the moved entry was already processed this tick.
                burstCount--;
                bCell[i] = bCell[burstCount];
                bUntil[i] = bUntil[burstCount];
                bRemaining[i] = bRemaining[burstCount];
                continue;
            }
            if (bUntil[i] <= 0) {
                grid[bCell[i]] = randomChar();
                markDirty(bCell[i]);
                // Jitter the cadence so bursts don't beat in lockstep.
                bUntil[i] = BURST_CHANGE_INTERVAL + randRange(-1, 2);
            } else {
                bUntil[i]--;
            }
        }

        // 3. A sprinkle of single flips on top of the bursts.
        const singleFlips = Math.max(1, Math.floor(cells / SINGLE_FLIP_CELLS_PER_FLIP));
        for (let i = 0; i < singleFlips; i++) {
            const idx = rand(rows) * cols + rand(cols);
            if (!revealed[idx]) continue;
            grid[idx] = randomChar();
            markDirty(idx);
        }
    }

    // ---- Render ------------------------------------------------------------------
    function flushDimLayer() {
        for (let k = 0; k < dirtyCount; k++) {
            const idx = dirtyList[k];
            dirtyFlag[idx] = 0;
            const x = (idx % cols) * cellW;
            const y = ((idx / cols) | 0) * cellH;
            dimCtx.fillStyle = '#000';
            dimCtx.fillRect(x, y, cellW, cellH);
            if (revealed[idx]) {
                // Clip to the cell so an overhanging glyph can't leave residue
                // in a neighbour that won't be repainted.
                dimCtx.save();
                dimCtx.beginPath();
                dimCtx.rect(x, y, cellW, cellH);
                dimCtx.clip();
                dimCtx.fillStyle = palettes[cellPalette[idx]].dim;
                dimCtx.fillText(pool[grid[idx]], x, y + baseline);
                dimCtx.restore();
            }
        }
        dirtyCount = 0;
    }

    function render() {
        if (dirtyCount > 0) flushDimLayer();

        ctx.drawImage(dimCanvas, 0, 0);

        for (let i = 0; i < streamCount; i++) {
            if (!sActive[i]) continue;
            const head = sHead[i];
            const top = Math.max(0, head - (TRAIL_LENGTH - 1));
            const bottom = Math.min(rows - 1, head);
            if (bottom < top) continue;

            const col = sCol[i];
            const x = col * cellW;
            const pal = palettes[sPalette[i]];
            const trail = sVoid[i] ? pal.voidTrail : pal.trail;

            // Canvas text is anti-aliased, so clear the stream's cells to black
            // first: the bright glyph then never blends with the identical dim
            // glyph underneath (the .scr's aliased text overwrote it exactly).
            ctx.fillStyle = '#000';
            ctx.fillRect(x, top * cellH, cellW, (bottom - top + 1) * cellH);

            for (let row = top; row <= bottom; row++) {
                const n = head - row;
                ctx.fillStyle = trail[n];
                const glyph = n === 0 ? pool[sHeadChar[i]] : pool[grid[row * cols + col]];
                ctx.fillText(glyph, x, row * cellH + baseline);
            }
        }
    }

    // ---- Debug overlay -----------------------------------------------------------
    let overlay = null;
    const stats = { frames: 0, ticks: 0, workMs: 0, maxWorkMs: 0, since: 0, dropsSum: 0, dropsMin: Infinity };
    // Lowest on-screen drop count per overlay update, for the last ~10 s.
    const dropsLowHistory = [];
    const DROPS_HISTORY = 20;

    function countVisibleDrops() {
        let n = 0;
        for (let i = 0; i < streamCount; i++) {
            if (sActive[i] && sHead[i] >= 0 && sHead[i] < rows) n++;
        }
        return n;
    }

    function createOverlay() {
        overlay = document.createElement('div');
        overlay.id = 'debug';
        document.body.appendChild(overlay);
    }

    function updateOverlay(now) {
        const elapsed = now - stats.since;
        if (elapsed < 500) return;
        const fps = (stats.frames * 1000) / elapsed;
        const tps = (stats.ticks * 1000) / elapsed;
        const avg = stats.frames ? stats.workMs / stats.frames : 0;
        dropsLowHistory.push(stats.dropsMin);
        if (dropsLowHistory.length > DROPS_HISTORY) dropsLowHistory.shift();
        const dropsLow = Math.min.apply(null, dropsLowHistory);
        const dropsAvg = stats.frames ? stats.dropsSum / stats.frames : 0;
        overlay.textContent =
            'fps ' + fps.toFixed(1) + '  ticks/s ' + tps.toFixed(1) + '\n' +
            'work ' + avg.toFixed(2) + ' ms avg, ' + stats.maxWorkMs.toFixed(2) + ' ms max\n' +
            'grid ' + cols + '×' + rows + '  cell ' + cellW + '×' + cellH + '  font ' + fontPx.toFixed(1) + 'px\n' +
            'canvas ' + canvas.width + '×' + canvas.height + '  dpr ' + dpr + '\n' +
            'streams ' + streamCount + '  bursts ' + burstCount + '/' + burstCap + '\n' +
            'drops on screen ' + dropsAvg.toFixed(1) + ' avg, ' + dropsLow + ' lowest (10 s)\n' +
            'config ' + cfg.characters + '  density ' + cfg.density + '  speed ' + cfg.speed + '  minDrops ' + cfg.minDrops +
            '  color ' + (cfg.rgbMode ? 'rgb' : palettes[0].trail[1]) + '  fontSize ' + cfg.fontSize + '\n' +
            charset.name + ': ' + pool.length + '/' + poolTotal + ' glyphs render' +
            (pool.length < poolTotal ? ' (' + (poolTotal - pool.length) + ' dropped)' : '');
        stats.frames = 0;
        stats.ticks = 0;
        stats.workMs = 0;
        stats.maxWorkMs = 0;
        stats.dropsSum = 0;
        stats.dropsMin = Infinity;
        stats.since = now;
    }

    // ---- Main loop ---------------------------------------------------------------
    let lastTime, lastRender = 0, accumulator = 0;

    function frame(now) {
        requestAnimationFrame(frame);
        if (lastTime === undefined) {
            lastTime = now;
            lastRender = now - FRAME_MS;
            stats.since = now;
        }
        let dt = now - lastTime;
        lastTime = now;
        if (dt > MAX_STEP_MS) dt = MAX_STEP_MS;
        accumulator += dt;

        if (now - lastRender < FRAME_MS - FRAME_SLACK_MS) return;
        lastRender = now;

        const t0 = performance.now();
        let ticks = 0;
        while (accumulator >= TICK_MS) {
            tick();
            accumulator -= TICK_MS;
            ticks++;
        }
        if (ticks > 0) render();

        if (overlay) {
            const work = performance.now() - t0;
            stats.frames++;
            stats.ticks += ticks;
            stats.workMs += work;
            if (work > stats.maxWorkMs) stats.maxWorkMs = work;
            const drops = countVisibleDrops();
            stats.dropsSum += drops;
            if (drops < stats.dropsMin) stats.dropsMin = drops;
            updateOverlay(now);
        }
    }

    async function start() {
        await loadConfigScript();
        cfg = readConfig();
        palettes = buildPalettes();
        charset = CHARSETS[cfg.characters];
        fontStack = charset.fonts;
        if (cfg.debug) {
            createOverlay();
            overlay.textContent = 'checking glyphs...';
        }

        const all = charset.build();
        poolTotal = all.length;
        await waitForFonts(BASE_FONT_PX + 'px ' + fontStack, all.slice(0, 64).join(''));
        pool = await filterRenderable(all, fontStack);
        if (pool.length < 2) {
            console.warn('matrix-rain: no font on this system renders "' + cfg.characters + '"; using digits');
            pool = '0123456789Z:.=*+-<>'.split('');
        }

        layout();
        let resizeTimer = 0;
        window.addEventListener('resize', () => {
            clearTimeout(resizeTimer);
            resizeTimer = setTimeout(layout, RESIZE_DEBOUNCE_MS);
        });
        requestAnimationFrame(frame);
    }

    start();
})();
