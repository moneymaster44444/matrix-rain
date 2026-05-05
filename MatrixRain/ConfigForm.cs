namespace MatrixRain;

public class ConfigForm : Form
{
    private readonly TrackBar _density;
    private readonly TrackBar _speed;
    private readonly ComboBox _language;
    private readonly Label _speedValue;
    private readonly Panel _colorSwatch;
    private readonly Config _config;

    public ConfigForm()
    {
        _config = Config.Load();

        Text = "Matrix Rain Settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(420, 340);

        // Characters
        var lblLanguage = new Label
        {
            Text = "Characters:",
            Location = new Point(12, 18),
            AutoSize = true,
        };
        _language = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(110, 14),
            Width = 290,
        };
        foreach (var (preset, name) in Languages.All())
            _language.Items.Add(new LanguageOption(preset, name));
        _language.SelectedIndex = FindLanguageIndex(_config.Language);

        // Density
        var lblDensity = new Label
        {
            Text = "Density:",
            Location = new Point(12, 60),
            AutoSize = true,
        };
        _density = new TrackBar
        {
            Minimum = 1,
            Maximum = 10,
            Value = Math.Clamp(_config.Density, 1, 10),
            TickFrequency = 1,
            Location = new Point(110, 54),
            Width = 290,
        };

        // Speed — slider value × 0.1 = multiplier (range 0.1 .. 5.0).
        var lblSpeed = new Label
        {
            Text = "Speed:",
            Location = new Point(12, 130),
            AutoSize = true,
        };
        _speed = new TrackBar
        {
            Minimum = 1,
            Maximum = 50,
            Value = Math.Clamp((int)Math.Round(_config.Speed * 10), 1, 50),
            TickFrequency = 5,
            LargeChange = 5,
            Location = new Point(110, 124),
            Width = 290,
        };
        _speedValue = new Label
        {
            Text = FormatSpeed(_speed.Value),
            Location = new Point(110, 168),
            AutoSize = true,
        };
        _speed.ValueChanged += (_, _) => _speedValue.Text = FormatSpeed(_speed.Value);

        // Color — clickable swatch showing current colour, plus a button that
        // opens the standard Windows ColorDialog with the full RGB editor.
        var lblColor = new Label
        {
            Text = "Color:",
            Location = new Point(12, 204),
            AutoSize = true,
        };
        _colorSwatch = new Panel
        {
            Location = new Point(110, 200),
            Size = new Size(60, 26),
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.FromArgb(_config.ColorR, _config.ColorG, _config.ColorB),
            Cursor = Cursors.Hand,
        };
        var pickBtn = new Button
        {
            Text = "Pick…",
            Location = new Point(180, 199),
            Size = new Size(80, 28),
        };
        EventHandler openPicker = (_, _) =>
        {
            using var dlg = new ColorDialog
            {
                Color = _colorSwatch.BackColor,
                FullOpen = true,    // expand the custom-colour RGB editor up-front
                AnyColor = true,
                SolidColorOnly = true,
            };
            if (dlg.ShowDialog(this) == DialogResult.OK)
                _colorSwatch.BackColor = dlg.Color;
        };
        _colorSwatch.Click += openPicker;
        pickBtn.Click += openPicker;

        // Hint — order matches the controls top-to-bottom.
        var hint = new Label
        {
            Text =
                "Density: number of rain columns.\n" +
                "Speed: multiplier on fall rate (lower = slower).",
            Location = new Point(12, 246),
            Size = new Size(396, 40),
        };

        var ok = new Button
        {
            Text = "OK",
            DialogResult = DialogResult.OK,
            Location = new Point(236, 298),
            Size = new Size(80, 28),
        };
        var cancel = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            Location = new Point(322, 298),
            Size = new Size(80, 28),
        };
        ok.Click += (_, _) => Save();

        Controls.AddRange(new Control[]
        {
            lblLanguage, _language,
            lblDensity, _density,
            lblSpeed, _speed, _speedValue,
            lblColor, _colorSwatch, pickBtn,
            hint,
            ok, cancel,
        });
        AcceptButton = ok;
        CancelButton = cancel;
    }

    private int FindLanguageIndex(LanguagePreset preset)
    {
        for (int i = 0; i < _language.Items.Count; i++)
        {
            if (_language.Items[i] is LanguageOption opt && opt.Preset == preset)
                return i;
        }
        return 0;
    }

    private static string FormatSpeed(int sliderValue) => $"× {sliderValue / 10.0:0.0}";

    private void Save()
    {
        _config.Density = _density.Value;
        _config.Speed = _speed.Value / 10.0;
        if (_language.SelectedItem is LanguageOption opt)
            _config.Language = opt.Preset;
        var c = _colorSwatch.BackColor;
        _config.ColorR = c.R;
        _config.ColorG = c.G;
        _config.ColorB = c.B;
        _config.Save();
    }

    private sealed class LanguageOption
    {
        public LanguagePreset Preset { get; }
        public string DisplayName { get; }
        public LanguageOption(LanguagePreset preset, string displayName)
        {
            Preset = preset;
            DisplayName = displayName;
        }
        public override string ToString() => DisplayName;
    }
}
