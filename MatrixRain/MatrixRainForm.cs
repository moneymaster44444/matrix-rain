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
    private int _cols;
    private int _rows;
    private int _cellWidth;
    private int _cellHeight;
    private readonly List<(int col, int row)> _dirtyCells = new();

    private Stream[] _streams = Array.Empty<Stream>();

    private Font _font = null!;
    private SolidBrush _bgGreenBrush = null!;
    private SolidBrush _blackBrush = null!;
    // Index 0 = head (white), 1..N-1 = green ramp from bright down to background dim.
    private SolidBrush[] _trailBrushes = Array.Empty<SolidBrush>();
    private StringFormat _stringFormat = null!;

    private LanguageDefinition _language = null!;
    private System.Windows.Forms.Timer? _timer;
    private readonly Random _rng = new();
    private Point? _firstMousePos;

    private const int TrailLength = 20;

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

        // Color palette derived from the user-picked bright trail color:
        //   bright = picker
        //   dim    = picker * 0.20  (background, & trail tail end)
        //   head   = picker * 0.15 + white * 0.85  (near-white tinted by picker)
        // For the canonical Matrix green (#00FF41) this reproduces the
        // original (0,51,13) dim and (~217,255,~227) head colours.
        var picker = Color.FromArgb(255, _config.ColorR, _config.ColorG, _config.ColorB);
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

        _bgGreenBrush = new SolidBrush(dim);
        _blackBrush = new SolidBrush(Color.Black);

        // Trail ramp: head at index 0, then a smooth lerp from bright (picker)
        // at index 1 down to dim at index N-1, so the tail dissolves into the
        // background layer.
        _trailBrushes = new SolidBrush[TrailLength];
        _trailBrushes[0] = new SolidBrush(head);
        for (int i = 1; i < TrailLength; i++)
        {
            float t = (i - 1) / (float)(TrailLength - 1);
            int r = (int)Math.Round((1 - t) * picker.R + t * dim.R);
            int g = (int)Math.Round((1 - t) * picker.G + t * dim.G);
            int b = (int)Math.Round((1 - t) * picker.B + t * dim.B);
            _trailBrushes[i] = new SolidBrush(Color.FromArgb(255, r, g, b));
        }

        _cols = Math.Max(1, ClientSize.Width / _cellWidth);
        _rows = Math.Max(1, ClientSize.Height / _cellHeight);
        _grid = new char[_cols, _rows];
        for (int c = 0; c < _cols; c++)
            for (int r = 0; r < _rows; r++)
                _grid[c, r] = RandomChar();

        int w = Math.Max(1, ClientSize.Width);
        int h = Math.Max(1, ClientSize.Height);
        _backgroundBitmap = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
        _frontBuffer = new Bitmap(w, h, PixelFormat.Format32bppPArgb);

        // Initial paint of the dim background — every cell in the grid drawn
        // once. After this, only cells that mutate are repainted.
        using (var g = Graphics.FromImage(_backgroundBitmap))
        {
            g.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;
            g.Clear(Color.Black);
            for (int c = 0; c < _cols; c++)
                for (int r = 0; r < _rows; r++)
                    g.DrawString(_grid[c, r].ToString(), _font, _bgGreenBrush,
                        c * _cellWidth, r * _cellHeight, _stringFormat);
        }

        InitStreams();
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
        // Density 1..10 → spacing 10..1 columns between active streams.
        // Background fills the whole screen regardless; density only controls
        // how many bright "rain" columns are falling.
        int density = Math.Clamp(_config.Density, 1, 10);
        int spacing = Math.Max(1, 11 - density);

        var list = new List<Stream>();
        for (int c = 0; c < _cols; c += spacing)
            list.Add(NewStream(c));
        _streams = list.ToArray();
    }

    private Stream NewStream(int col)
    {
        float speedMul = (float)Math.Clamp(_config.Speed, 0.1, 5.0);
        // Base advance rate is 1–3 frames per row. Speed multiplier scales it.
        int baseFps = 1 + _rng.Next(3);
        int fps = Math.Max(1, (int)Math.Round(baseFps / speedMul));
        return new Stream
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
        };
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
                    s.HeadRow = -_rng.Next(0, _rows / 2 + TrailLength);
                    s.Active = true;
                    int baseFps = 1 + _rng.Next(3);
                    s.FramesPerStep = Math.Max(1, (int)Math.Round(baseFps / speedMul));
                    s.FrameCounter = 0;
                }
                continue;
            }

            s.FrameCounter++;
            if (s.FrameCounter >= s.FramesPerStep)
            {
                s.FrameCounter = 0;
                s.HeadRow++;

                if (s.HeadRow >= 0 && s.HeadRow < _rows)
                {
                    _grid[s.Col, s.HeadRow] = RandomChar();
                    _dirtyCells.Add((s.Col, s.HeadRow));
                }

                // Stream finished crossing the screen.
                if (s.HeadRow - TrailLength >= _rows)
                {
                    s.Active = false;
                    s.ResetDelay = _rng.Next(0, 90);
                }
            }
        }

        // 2. Subtle background mutation so the dim layer isn't perfectly
        //    static — a small fraction of cells re-roll each frame.
        int mutCount = Math.Max(1, (_cols * _rows) / 2500);
        for (int i = 0; i < mutCount; i++)
        {
            int c = _rng.Next(_cols);
            int r = _rng.Next(_rows);
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
                bg.DrawString(_grid[c, r].ToString(), _font, _bgGreenBrush,
                    c * _cellWidth, r * _cellHeight, _stringFormat);
            }
            _dirtyCells.Clear();
        }

        // 4. Build the front buffer: blit the dim background, then paint
        //    bright trail cells over the top. The bright text uses the
        //    same character at the same coordinates as the dim background,
        //    so with grid-fit (no anti-aliasing) the bright pixels overwrite
        //    the dim ones exactly — no halos, no fill needed.
        using (var fg = Graphics.FromImage(_frontBuffer))
        {
            fg.DrawImageUnscaled(_backgroundBitmap, 0, 0);
            fg.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;

            foreach (var s in _streams)
            {
                if (!s.Active) continue;
                for (int i = 0; i < TrailLength; i++)
                {
                    int row = s.HeadRow - i;
                    if (row < 0 || row >= _rows) continue;
                    fg.DrawString(_grid[s.Col, row].ToString(), _font, _trailBrushes[i],
                        s.Col * _cellWidth, row * _cellHeight, _stringFormat);
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
        _bgGreenBrush?.Dispose();
        _blackBrush?.Dispose();
        foreach (var b in _trailBrushes) b.Dispose();
        _stringFormat?.Dispose();
        if (!_isPreview) Cursor.Show();
        base.OnFormClosed(e);
    }
}
