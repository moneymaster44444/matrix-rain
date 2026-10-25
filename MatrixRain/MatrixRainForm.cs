using System.ComponentModel;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace MatrixRain;

public class MatrixRainForm : Form
{
    private readonly bool _isPreview;
    private readonly Config _config;

    // Two-bitmap pipeline:
    // - _backgroundBitmap holds every cell in dim green, mutated lazily.
    // - _frontBuffer is rebuilt each frame: blit background, then overlay
    //   bright stream trails on top.
    private Bitmap? _backgroundBitmap;
    private Bitmap? _frontBuffer;

    // Character grid covering the whole screen.
    private char[,] _grid = new char[0, 0];
    // Whether a rain head has ever passed through a cell. Until it has, the
    // cell stays black — the screen starts as a clean void and the first wipe
    // is what establishes the persistent dim text. After a cell is revealed it
    // behaves like the classic persistent background (subtle re-roll churn).
    private bool[,] _revealed = new bool[0, 0];
    private int _cols;
    private int _rows;
    private int _cellWidth;
    private int _cellHeight;
    private readonly List<(int col, int row)> _dirtyCells = new();

    private Stream[] _streams = Array.Empty<Stream>();
    // Which columns currently hold an active stream. Streams roam to random
    // free columns when they (re)spawn, so over time every column gets rained
    // on instead of a fixed evenly-spaced subset.
    private bool[] _columnActive = Array.Empty<bool>();

    // Cells currently in a background "churn burst" — see BurstChangeInterval.
    private readonly List<CellBurst> _bursts = new();

    private Font _font = null!;
    private SolidBrush _blackBrush = null!;
    // _palettes[0] is the picked colour. In RGB mode there's one palette per
    // hue step instead, and every new stream picks one at random.
    private Palette[] _palettes = Array.Empty<Palette>();
    // Palette of the stream that last froze each cell (its dim text colour).
    private byte[,] _cellPalette = new byte[0, 0];
    // Palette of each column's latest stream, so the next one differs.
    private byte[] _columnPalette = Array.Empty<byte>();
    // Every palette brush, for disposal.
    private readonly List<SolidBrush> _paletteBrushes = new();

    // RGB mode: each new drop gets a random, fully saturated colour clearly
    // different from its column's current one, and wipes that colour down
    // the column as it falls.
    private const int RgbHueSteps = 72;        // 5° apart
    private const int RgbMinHueSteps = 8;      // 40° minimum change per column
    private const int RgbHueAttempts = 16;
    private const byte NoPalette = 255;

    // Dim: the persistent background text.
    // Trail: index 0 = head (white), 1..N-1 = ramp from bright down to dim.
    // VoidTrail: same head/bright start, but the ramp fades to black instead
    // of dim — used by "void" streams that leave a dark column behind them.
    private sealed record Palette(SolidBrush Dim, SolidBrush[] Trail, SolidBrush[] VoidTrail);
    private StringFormat _stringFormat = null!;

    private LanguageDefinition _language = null!;
    private System.Windows.Forms.Timer? _timer;
    private readonly Random _rng = new();
    private Point? _firstMousePos;

    private const int TrailLength = 20;

    // The head re-rolls its glyph every this-many timer frames (timer runs at
    // ~16 ms). This is deliberately decoupled from the per-stream fall rate so
    // the head visibly flickers through many characters even when the stream is
    // crawling — only the glyph it lands on when it advances gets frozen.
    private const int HeadFlickerInterval = 2;

    // Background churn happens in short localized bursts instead of scattered
    // one-off flips: a revealed cell "wakes up" and re-rolls its glyph roughly
    // every BurstChangeInterval frames for BurstMin..BurstMax frames, then goes
    // quiet again. At ~16 ms/frame that's ~15 flips/sec across a 2–3 s burst.
    private const int BurstChangeInterval = 4;    // ~15 flips/sec
    private const int BurstMinFrames = 125;       // ~2 s
    private const int BurstMaxFrames = 188;       // ~3 s
    // One concurrent burst per this many cells, scaled to the grid size.
    private const int BurstCellsPerBurst = 200;
    // Alongside the bursts, a few revealed cells re-roll exactly once each
    // frame (the old scattered-flip churn) — one flip per this many cells.
    private const int SingleFlipCellsPerFlip = 2500;

    // Fraction of streams that dissolve their tail to black instead of dim,
    // leaving a dark void in the column behind them. A later non-void stream
    // roaming into that column fills it back to normal dim text.
    private const double VoidStreamChance = 0.15;

    private sealed class CellBurst
    {
        public int Col;
        public int Row;
        public int FramesUntilChange;
        public int FramesRemaining;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public IntPtr ParentHwndForCheck { get; set; } = IntPtr.Zero;

    public event EventHandler? ExitRequested;

    private sealed class Stream
    {
        public int Col;
        public int HeadRow;        // Can be negative (above the screen) before stream enters.
        public int FramesPerStep;  // Lower = faster.
        public int FrameCounter;
        public int ResetDelay;     // Frames to wait after going off-bottom before respawning.
        public bool Active;
        // The head glyph re-rolls rapidly while the head sits on a row (see
        // HeadFlickerInterval), independent of the fall rate. The value it
        // holds when the head moves on is the one that gets frozen into the
        // grid for that cell.
        public char HeadChar;
        public int HeadFlickerCounter;
        // When true, the tail fades to black and the cells it leaves are
        // un-revealed (returned to the dark void) instead of settling as dim
        // text. Decided fresh on every spawn/respawn.
        public bool Void;
        // Index into _palettes; also decided fresh on every spawn/respawn.
        public byte PaletteIndex;
    }

    public MatrixRainForm(Rectangle bounds, bool isPreview, Config config)
    {
        _isPreview = isPreview;
        _config = config;

        SuspendLayout();
        FormBorderStyle = FormBorderStyle.None;
        BackColor = Color.Black;
        ShowInTaskbar = false;
        KeyPreview = true;
        StartPosition = FormStartPosition.Manual;

        if (!isPreview)
        {
            TopMost = true;
            Bounds = bounds;
        }
        else
        {
            Location = Point.Empty;
            ClientSize = bounds.Size;
        }

        SetStyle(
            ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.UserPaint,
            true);
        UpdateStyles();
        ResumeLayout();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (!_isPreview) Cursor.Hide();
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        InitGraphics();
        _timer = new System.Windows.Forms.Timer { Interval = 16 };
        _timer.Tick += OnTick;
        _timer.Start();
    }

    private char RandomChar() => _language.Chars[_rng.Next(_language.Chars.Length)];

    private void InitGraphics()
    {
        _language = Languages.Get(_config.Language);

        // Each language carries its own preferred font chain (e.g. Wingdings
        // requires the Wingdings font, Korean prefers Malgun Gothic, etc.).
        float pointSize = _isPreview ? 8f : 16f;
        _font = LoadFont(_language.FontPreferences, pointSize);

        _stringFormat = new StringFormat(StringFormat.GenericTypographic)
        {
            FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.NoClip,
        };

        // Most non-Latin scripts aren't strictly monospaced, so sample many
        // glyphs and use the maximum width as the cell width.
        using (var g = CreateGraphics())
        {
            g.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;
            float maxWidth = 0;
            int samples = Math.Min(80, _language.Chars.Length);
            for (int i = 0; i < samples; i++)
            {
                var ch = RandomChar().ToString();
                var sz = g.MeasureString(ch, _font, int.MaxValue, _stringFormat);
                if (sz.Width > maxWidth) maxWidth = sz.Width;
            }
            _cellWidth = Math.Max(1, (int)Math.Ceiling(maxWidth));
            _cellHeight = Math.Max(1, (int)Math.Round(_font.GetHeight(g)));
        }

        _blackBrush = new SolidBrush(Color.Black);

        _cols = Math.Max(1, ClientSize.Width / _cellWidth);
        _rows = Math.Max(1, ClientSize.Height / _cellHeight);
        BuildPalettes();
        _grid = new char[_cols, _rows];
        for (int c = 0; c < _cols; c++)
            for (int r = 0; r < _rows; r++)
                _grid[c, r] = RandomChar();
        // Nothing revealed yet — the whole screen starts as a black void.
        _revealed = new bool[_cols, _rows];
        _cellPalette = new byte[_cols, _rows];
        _columnPalette = new byte[_cols];
        Array.Fill(_columnPalette, NoPalette);

        int w = Math.Max(1, ClientSize.Width);
        int h = Math.Max(1, ClientSize.Height);
        _backgroundBitmap = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
        _frontBuffer = new Bitmap(w, h, PixelFormat.Format32bppPArgb);

        // Start on a clean black background. No cells are drawn up front — the
        // first rain wipe is what lays down the persistent dim text, after
        // which only cells that mutate are repainted.
        using (var g = Graphics.FromImage(_backgroundBitmap))
        {
            g.Clear(Color.Black);
        }

        InitStreams();
    }

    private void BuildPalettes()
    {
        if (!_config.RgbMode)
        {
            _palettes = new[] { BuildPalette(Color.FromArgb(255, _config.ColorR, _config.ColorG, _config.ColorB)) };
            return;
        }
        _palettes = new Palette[RgbHueSteps];
        for (int k = 0; k < RgbHueSteps; k++)
            _palettes[k] = BuildPalette(HueToColor(k * 360.0 / RgbHueSteps));
    }

    // Palette for a new stream in this column.
    private byte PickPalette(int col)
    {
        if (!_config.RgbMode) return 0;
        int prev = _columnPalette[col];
        for (int attempt = 0; attempt < RgbHueAttempts; attempt++)
        {
            int k = _rng.Next(RgbHueSteps);
            if (prev == NoPalette) return (byte)k;
            int d = Math.Abs(k - prev);
            if (Math.Min(d, RgbHueSteps - d) >= RgbMinHueSteps) return (byte)k;
        }
        return (byte)((prev + RgbHueSteps / 2) % RgbHueSteps);
    }

    private void AssignPalette(Stream s)
    {
        s.PaletteIndex = PickPalette(s.Col);
        _columnPalette[s.Col] = s.PaletteIndex;
    }

    // Color palette derived from one bright trail color:
    //   bright = picker
    //   dim    = picker * 0.20  (background, & trail tail end)
    //   head   = picker * 0.15 + white * 0.85  (near-white tinted by picker)
    // For the canonical Matrix green (#00FF41) this reproduces the
    // original (0,51,13) dim and (~217,255,~227) head colours.
    private Palette BuildPalette(Color picker)
    {
        var dim = Color.FromArgb(
            255,
            (int)Math.Round(picker.R * 0.20),
            (int)Math.Round(picker.G * 0.20),
            (int)Math.Round(picker.B * 0.20));
        var head = Color.FromArgb(
            255,
            (int)Math.Round(picker.R * 0.15 + 255 * 0.85),
            (int)Math.Round(picker.G * 0.15 + 255 * 0.85),
            (int)Math.Round(picker.B * 0.15 + 255 * 0.85));

        // Trail ramp: head at index 0, then a smooth lerp from bright (picker)
        // at index 1 down to dim at index N-1, so the tail dissolves into the
        // background layer.
        var trail = new SolidBrush[TrailLength];
        trail[0] = NewPaletteBrush(head);
        for (int i = 1; i < TrailLength; i++)
        {
            float t = (i - 1) / (float)(TrailLength - 1);
            int r = (int)Math.Round((1 - t) * picker.R + t * dim.R);
            int g = (int)Math.Round((1 - t) * picker.G + t * dim.G);
            int b = (int)Math.Round((1 - t) * picker.B + t * dim.B);
            trail[i] = NewPaletteBrush(Color.FromArgb(255, r, g, b));
        }

        // Void ramp: identical head, but lerps from bright (picker) down to
        // pure black so the tail dissolves into darkness, leaving no dim text.
        var voidTrail = new SolidBrush[TrailLength];
        voidTrail[0] = trail[0];
        for (int i = 1; i < TrailLength; i++)
        {
            float t = (i - 1) / (float)(TrailLength - 1);
            int r = (int)Math.Round((1 - t) * picker.R);
            int g = (int)Math.Round((1 - t) * picker.G);
            int b = (int)Math.Round((1 - t) * picker.B);
            voidTrail[i] = NewPaletteBrush(Color.FromArgb(255, r, g, b));
        }

        return new Palette(NewPaletteBrush(dim), trail, voidTrail);
    }

    private SolidBrush NewPaletteBrush(Color color)
    {
        var brush = new SolidBrush(color);
        _paletteBrushes.Add(brush);
        return brush;
    }

    // Fully saturated, full-brightness colour for a hue in degrees.
    private static Color HueToColor(double hue)
    {
        double x = hue / 60 % 6;
        int sector = (int)x;
        int up = (int)Math.Round(255 * (x - sector));
        int down = 255 - up;
        return sector switch
        {
            0 => Color.FromArgb(255, 255, up, 0),
            1 => Color.FromArgb(255, down, 255, 0),
            2 => Color.FromArgb(255, 0, 255, up),
            3 => Color.FromArgb(255, 0, down, 255),
            4 => Color.FromArgb(255, up, 0, 255),
            _ => Color.FromArgb(255, 255, 0, down),
        };
    }

    private static Font LoadFont(string[] families, float pointSize)
    {
        foreach (var family in families)
        {
            try
            {
                var f = new Font(family, pointSize, FontStyle.Regular, GraphicsUnit.Point);
                // GDI+ silently substitutes when the family doesn't exist.
                // Compare names (case-insensitive) so we don't accept a fallback.
                if (string.Equals(f.Name, family, StringComparison.OrdinalIgnoreCase))
                    return f;
                f.Dispose();
            }
            catch
            {
                // try next
            }
        }
        return new Font(FontFamily.GenericMonospace, pointSize);
    }

    private void InitStreams()
    {
        // Density 1..10 controls how many streams fall *at once* (1/spacing of
        // the columns). Streams don't own a fixed column: each picks a random
        // free column when it spawns and roams to a new one when it respawns,
        // so over time the rain hits every column rather than a fixed subset.
        int density = Math.Clamp(_config.Density, 1, 10);
        int spacing = Math.Max(1, 11 - density);
        int count = Math.Clamp(_cols / spacing, 1, _cols);

        _columnActive = new bool[_cols];
        var list = new List<Stream>(count);
        for (int i = 0; i < count; i++)
        {
            int col = PickFreeColumn();
            _columnActive[col] = true;
            list.Add(NewStream(col));
        }
        _streams = list.ToArray();
    }

    // A random column not currently occupied by an active stream. Falls back to
    // a linear scan, then to any column, so it always returns something.
    private int PickFreeColumn()
    {
        for (int attempt = 0; attempt < 16; attempt++)
        {
            int c = _rng.Next(_cols);
            if (!_columnActive[c]) return c;
        }
        for (int c = 0; c < _cols; c++)
            if (!_columnActive[c]) return c;
        return _rng.Next(_cols);
    }

    private Stream NewStream(int col)
    {
        float speedMul = (float)Math.Clamp(_config.Speed, 0.1, 5.0);
        // Base advance rate is 1–3 frames per row. Speed multiplier scales it.
        int baseFps = 1 + _rng.Next(3);
        int fps = Math.Max(1, (int)Math.Round(baseFps / speedMul));
        var stream = new Stream
        {
            Col = col,
            // Negative head row = stream is still above the visible area.
            // Random offset desynchronises streams so they don't all land in
            // the same row at the same time.
            HeadRow = -_rng.Next(0, _rows + TrailLength),
            FramesPerStep = fps,
            FrameCounter = _rng.Next(fps),
            ResetDelay = 0,
            Active = true,
            HeadChar = RandomChar(),
            HeadFlickerCounter = 0,
            Void = _rng.NextDouble() < VoidStreamChance,
        };
        AssignPalette(stream);
        return stream;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (_isPreview && ParentHwndForCheck != IntPtr.Zero && !Native.IsWindow(ParentHwndForCheck))
        {
            Close();
            return;
        }

        Step();
        Invalidate();
    }

    private void Step()
    {
        if (_backgroundBitmap == null || _frontBuffer == null) return;

        float speedMul = (float)Math.Clamp(_config.Speed, 0.1, 5.0);

        // 1. Advance every stream. When a head crosses into a new on-screen
        //    row, replace that cell's character — that's the "rain mutates
        //    the column as it falls" behaviour.
        foreach (var s in _streams)
        {
            if (!s.Active)
            {
                if (s.ResetDelay > 0)
                {
                    s.ResetDelay--;
                    continue;
                }
                if (_rng.NextDouble() < 0.04)
                {
                    // Roam to a new random free column on respawn.
                    s.Col = PickFreeColumn();
                    _columnActive[s.Col] = true;
                    s.HeadRow = -_rng.Next(0, _rows / 2 + TrailLength);
                    s.Active = true;
                    int baseFps = 1 + _rng.Next(3);
                    s.FramesPerStep = Math.Max(1, (int)Math.Round(baseFps / speedMul));
                    s.FrameCounter = 0;
                    s.HeadChar = RandomChar();
                    s.HeadFlickerCounter = 0;
                    s.Void = _rng.NextDouble() < VoidStreamChance;
                    AssignPalette(s);
                }
                continue;
            }

            // Rapidly re-roll the head glyph while it sits on its current row.
            // This is independent of FramesPerStep so the head flickers fast
            // even when the stream falls slowly.
            s.HeadFlickerCounter++;
            if (s.HeadFlickerCounter >= HeadFlickerInterval)
            {
                s.HeadFlickerCounter = 0;
                s.HeadChar = RandomChar();
            }

            s.FrameCounter++;
            if (s.FrameCounter >= s.FramesPerStep)
            {
                s.FrameCounter = 0;

                // Freeze the glyph the head landed on into the cell it's
                // leaving — that's the character the viewer sees settle once
                // the head moves down. A normal stream reveals it into the dim
                // background; a void stream un-reveals it, returning the cell
                // to black so the column goes dark behind the drop. (The glyph
                // is still stored so the fading tail can draw it on the way
                // out.)
                if (s.HeadRow >= 0 && s.HeadRow < _rows)
                {
                    _grid[s.Col, s.HeadRow] = s.HeadChar;
                    _revealed[s.Col, s.HeadRow] = !s.Void;
                    _cellPalette[s.Col, s.HeadRow] = s.PaletteIndex;
                    _dirtyCells.Add((s.Col, s.HeadRow));
                }

                s.HeadRow++;
                s.HeadChar = RandomChar();

                // Stream finished crossing the screen. Release its column so
                // another stream can roam into it.
                if (s.HeadRow - TrailLength >= _rows)
                {
                    s.Active = false;
                    s.ResetDelay = _rng.Next(0, 90);
                    _columnActive[s.Col] = false;
                }
            }
        }

        // 2. Background churn in localized bursts. Top up the active bursts
        //    toward a screen-size-scaled target, seeding only on already-
        //    revealed cells so the untouched void stays black. A cell in a
        //    burst re-rolls every ~BurstChangeInterval frames for the burst's
        //    lifetime, then goes quiet — gentle motion that doesn't pop.
        int burstTarget = Math.Max(1, (_cols * _rows) / BurstCellsPerBurst);
        for (int attempt = 0; _bursts.Count < burstTarget && attempt < 64; attempt++)
        {
            int c = _rng.Next(_cols);
            int r = _rng.Next(_rows);
            if (!_revealed[c, r]) continue;
            _bursts.Add(new CellBurst
            {
                Col = c,
                Row = r,
                FramesUntilChange = 0,  // first flip on the seeding frame
                FramesRemaining = _rng.Next(BurstMinFrames, BurstMaxFrames + 1),
            });
        }

        for (int i = _bursts.Count - 1; i >= 0; i--)
        {
            var b = _bursts[i];
            if (--b.FramesRemaining <= 0)
            {
                _bursts.RemoveAt(i);
                continue;
            }
            if (b.FramesUntilChange <= 0)
            {
                _grid[b.Col, b.Row] = RandomChar();
                _dirtyCells.Add((b.Col, b.Row));
                // Jitter the cadence a little so bursts don't beat in lockstep.
                b.FramesUntilChange = BurstChangeInterval + _rng.Next(-1, 2);
            }
            else
            {
                b.FramesUntilChange--;
            }
        }

        // 2b. A sprinkle of one-off flips on top of the bursts — a handful of
        //     revealed cells re-roll a single time each frame, like the dim
        //     layer churned before bursts existed. Void cells stay black.
        int singleFlips = Math.Max(1, (_cols * _rows) / SingleFlipCellsPerFlip);
        for (int i = 0; i < singleFlips; i++)
        {
            int c = _rng.Next(_cols);
            int r = _rng.Next(_rows);
            if (!_revealed[c, r]) continue;
            _grid[c, r] = RandomChar();
            _dirtyCells.Add((c, r));
        }

        // 3. Repaint only the dirty cells on the background bitmap.
        if (_dirtyCells.Count > 0)
        {
            using var bg = Graphics.FromImage(_backgroundBitmap);
            bg.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;
            foreach (var (c, r) in _dirtyCells)
            {
                bg.FillRectangle(_blackBrush, c * _cellWidth, r * _cellHeight, _cellWidth, _cellHeight);
                // Un-revealed cells (the initial void, or a void stream's wake)
                // stay pure black — only revealed cells carry dim text.
                if (_revealed[c, r])
                    bg.DrawString(_grid[c, r].ToString(), _font, _palettes[_cellPalette[c, r]].Dim,
                        c * _cellWidth, r * _cellHeight, _stringFormat);
            }
            _dirtyCells.Clear();
        }

        // 4. Build the front buffer: blit the dim background, then paint
        //    bright trail cells over the top. Trail cells (i >= 1) reuse the
        //    settled grid character at the same coordinates as the dim
        //    background, so with grid-fit (no anti-aliasing) the bright pixels
        //    overwrite the dim ones exactly — no halos, no fill needed.
        using (var fg = Graphics.FromImage(_frontBuffer))
        {
            fg.DrawImageUnscaled(_backgroundBitmap, 0, 0);
            fg.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;

            foreach (var s in _streams)
            {
                if (!s.Active) continue;

                // Void streams fade their tail to black (leaving the column
                // dark behind them); normal streams fade to the dim background.
                var palette = _palettes[s.PaletteIndex];
                var trail = s.Void ? palette.VoidTrail : palette.Trail;

                // Trail below the head: settled glyphs, brightest just under
                // the head fading down toward the layer it dissolves into.
                for (int i = 1; i < TrailLength; i++)
                {
                    int row = s.HeadRow - i;
                    if (row < 0 || row >= _rows) continue;
                    fg.DrawString(_grid[s.Col, row].ToString(), _font, trail[i],
                        s.Col * _cellWidth, row * _cellHeight, _stringFormat);
                }

                // The head itself flickers a fresh glyph each frame and isn't
                // settled in the grid yet, so it won't match whatever the
                // background holds underneath — clear the cell to black first
                // so the previous flicker glyph leaves no residue.
                if (s.HeadRow >= 0 && s.HeadRow < _rows)
                {
                    fg.FillRectangle(_blackBrush,
                        s.Col * _cellWidth, s.HeadRow * _cellHeight, _cellWidth, _cellHeight);
                    fg.DrawString(s.HeadChar.ToString(), _font, trail[0],
                        s.Col * _cellWidth, s.HeadRow * _cellHeight, _stringFormat);
                }
            }
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (_frontBuffer != null)
            e.Graphics.DrawImageUnscaled(_frontBuffer, 0, 0);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        // Skip — every visible pixel is supplied by the front buffer.
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (!_isPreview) RequestExit();
        base.OnKeyDown(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (!_isPreview) RequestExit();
        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!_isPreview)
        {
            // Jitter tolerance — small involuntary movements when the mouse
            // first reports its position would otherwise dismiss the saver.
            if (_firstMousePos == null)
            {
                _firstMousePos = e.Location;
            }
            else
            {
                int dx = e.Location.X - _firstMousePos.Value.X;
                int dy = e.Location.Y - _firstMousePos.Value.Y;
                if (dx * dx + dy * dy > 25) RequestExit();
            }
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _firstMousePos = null;
        base.OnMouseLeave(e);
    }

    private void RequestExit() => ExitRequested?.Invoke(this, EventArgs.Empty);

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _timer?.Stop();
        _timer?.Dispose();
        _backgroundBitmap?.Dispose();
        _frontBuffer?.Dispose();
        _font?.Dispose();
        _blackBrush?.Dispose();
        foreach (var b in _paletteBrushes) b.Dispose();
        _stringFormat?.Dispose();
        if (!_isPreview) Cursor.Show();
        base.OnFormClosed(e);
    }
}
