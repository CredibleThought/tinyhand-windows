namespace TinyHand;

/// <summary>The settings window. Every change applies straight away.</summary>
internal sealed class SettingsForm : Form
{
    private readonly Settings _settings;
    private readonly TableLayoutPanel _table;

    public SettingsForm(Settings settings)
    {
        _settings = settings;
        Text = "TinyHand Settings";
        Icon = Assets.AppIcon();
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(16);
        Font = SystemFonts.MessageBoxFont ?? Font;

        _table = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Fill };
        _table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        Controls.Add(_table);

        var d = settings.Data;
        Header("Pointer");
        Combo("Hand", Catalog.Styles, () => _settings.Style, v => _settings.Update(s => s.PointerStyle = v));
        Number("Size (share of screen height)", 0.08m, 0.20m, 0.01m, 2, () => d.PointerSize, v => _settings.Update(s => s.PointerSize = v));
        Check("Shadow", () => d.ShowShadow, v => _settings.Update(s => s.ShowShadow = v));

        Header("Drawing");
        Combo("Drag to draw (hold Shift for circles, squares and straight arrows)", Catalog.Shapes,
            () => _settings.Shape, v => _settings.Update(s => s.DrawShape = v));
        Number("Fade after (seconds; 0 keeps drawings)", 0m, 60m, 0.5m, 1, () => d.FadeSeconds, v => _settings.Update(s => s.FadeSeconds = v));
        Combo("Ink colour", Catalog.InkChoices.Select(c => (c.Id, c.Title)), () => d.InkChoice, v => _settings.Update(s => s.InkChoice = v));
        Combo("Double-click effect", Catalog.ClickEffects, () => _settings.ClickEffect, v => _settings.Update(s => s.DoubleClickEffect = v));

        Header("Sound");
        Combo("Tap sound", Catalog.TapSounds, () => d.TapSound, v => _settings.Update(s => s.TapSound = v));
        Check("Sound with hands", () => d.HandSound, v => _settings.Update(s => s.HandSound = v));
        Check("Sound with the laser dot", () => d.LaserSound, v => _settings.Update(s => s.LaserSound = v));

        Header("Controls");
        Combo("Click-through key (hold it to click apps for real)", Catalog.InteractKeys, () => d.InteractKey,
            v => _settings.Update(s => s.InteractKey = v));
        if (Settings.Portable)
            Note("Portable mode: settings are saved next to TinyHand.exe.");
        else
            Check("Start with Windows", () => Startup.Enabled, v => Startup.Enabled = v);
        Note("Ctrl+Alt+H turns the pointer on or off. Ctrl+Alt+X clears the drawing. Esc turns the pointer off.");
    }

    private void Add(Control label, Control? field)
    {
        label.Margin = new Padding(0, 6, 16, 6);
        _table.Controls.Add(label);
        if (field != null)
        {
            field.Margin = new Padding(0, 3, 0, 3);
            _table.Controls.Add(field);
        }
        else
        {
            _table.SetColumnSpan(label, 2);
        }
    }

    private void Header(string text) =>
        Add(new Label { Text = text, AutoSize = true, Font = new Font(Font, FontStyle.Bold), Padding = new Padding(0, 10, 0, 0) }, null);

    private void Note(string text) =>
        Add(new Label { Text = text, AutoSize = true, MaximumSize = new Size(LogicalToDeviceUnits(520), 0), ForeColor = SystemColors.GrayText }, null);

    private static Label Caption(string text) => new() { Text = text, AutoSize = true, MaximumSize = new Size(320, 0) };

    private void Combo(string label, IEnumerable<(string Id, string Title)> items, Func<string> get, Action<string> set)
    {
        var list = items.ToList();
        var box = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = LogicalToDeviceUnits(200) };
        box.Items.AddRange(list.Select(i => (object)i.Title).ToArray());
        box.SelectedIndex = Math.Max(0, list.FindIndex(i => i.Id == get()));
        box.SelectedIndexChanged += (_, _) =>
        {
            var id = list[box.SelectedIndex].Id;
            if (id != get())
                set(id);
        };
        Add(Caption(label), box);
    }

    private void Number(string label, decimal min, decimal max, decimal step, int digits, Func<double> get, Action<double> set)
    {
        var box = new NumericUpDown
        {
            Minimum = min, Maximum = max, Increment = step, DecimalPlaces = digits, Width = LogicalToDeviceUnits(80),
            Value = Math.Clamp((decimal)get(), min, max),
        };
        box.ValueChanged += (_, _) => set((double)box.Value);
        Add(Caption(label), box);
    }

    private void Check(string label, Func<bool> get, Action<bool> set)
    {
        var box = new CheckBox { Checked = get(), AutoSize = true };
        box.CheckedChanged += (_, _) => set(box.Checked);
        Add(Caption(label), box);
    }
}
