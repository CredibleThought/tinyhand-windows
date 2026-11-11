using System.Drawing.Drawing2D;

namespace TinyHand;

/// <summary>
/// Turns a picture into a new hand. It offers a template and a prompt for
/// ChatGPT or Gemini, removes the background of the picture you choose,
/// shows where TinyHand thinks the fingertip and wrist are, and lets you
/// move the fingertip marker before saving.
/// </summary>
internal sealed class AddHandForm : Form
{
    private readonly Settings _settings;
    private readonly HandPreview _preview = new();
    private readonly PictureBox _thumb = new() { SizeMode = PictureBoxSizeMode.Zoom, BorderStyle = BorderStyle.FixedSingle };
    private readonly TextBox _name = new();
    private readonly Button _ink = new() { Text = "Change…", AutoSize = true };
    private readonly Panel _inkSwatch = new() { BorderStyle = BorderStyle.FixedSingle };
    private readonly Label _message = new() { AutoSize = true, ForeColor = Color.DarkGoldenrod };
    private readonly Button _add = new() { Text = "Add hand", AutoSize = true, Enabled = false };
    private Bitmap? _canvas;
    private HandGeometry? _geometry;
    private Color _inkColor = Color.Purple;

    /// <summary>Shows the window; if a hand is added it becomes the current hand.</summary>
    public static void Run(Settings settings, IWin32Window? owner = null, string? picture = null)
    {
        using var form = new AddHandForm(settings);
        if (picture != null)
            form.Shown += (_, _) => form.LoadPicture(picture);
        if (owner != null)
            form.ShowDialog(owner);
        else
            form.ShowDialog();
    }

    /// <summary>Asks, then deletes a hand the user added.</summary>
    public static void Remove(Settings settings, Hand hand, IWin32Window? owner = null)
    {
        if (MessageBox.Show(owner, $"Remove the hand \"{hand.Title}\"? This deletes its pictures.", "Remove hand",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
            return;
        // Switch away first so nothing is still showing the pictures.
        settings.Update(d =>
        {
            d.PointerStyle = "lavender";
            d.PinnedStyles.Remove(hand.Id);
        });
        try
        {
            HandStore.Delete(hand);
        }
        catch (IOException e)
        {
            MessageBox.Show(owner, $"Could not delete the hand's folder:\n{e.Message}", "Remove hand");
        }
        Catalog.ReloadCustom();
        settings.Update(_ => { });
    }

    private AddHandForm(Settings settings)
    {
        _settings = settings;
        Text = "Add a hand";
        Icon = Assets.AppIcon();
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(16);
        Font = SystemFonts.MessageBoxFont ?? Font;
        ShowInTaskbar = true;

        var layout = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Fill };
        Controls.Add(layout);

        var intro = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(LogicalToDeviceUnits(620), 0),
            Text = "Choose a picture of a hand holding a stick, with one finger pointing up, on a white or " +
                   "transparent background.\n\nTo make one: save the template, open ChatGPT or Gemini, attach the " +
                   "template, paste the prompt and fill in the hand you want. Save the picture it makes, then choose it here.",
            Margin = new Padding(0, 0, 0, 10),
        };
        layout.Controls.Add(intro);
        layout.SetColumnSpan(intro, 2);

        var steps = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 10) };
        var template = new Button { Text = "Save template…", AutoSize = true };
        template.Click += (_, _) => SaveTemplate();
        var prompt = new Button { Text = "Copy prompt", AutoSize = true };
        prompt.Click += (_, _) =>
        {
            Clipboard.SetText(HandImporter.Prompt);
            prompt.Text = "Prompt copied";
        };
        var choose = new Button { Text = "Choose picture…", AutoSize = true };
        choose.Click += (_, _) => ChoosePicture();
        steps.Controls.AddRange(new Control[] { template, prompt, choose });
        layout.Controls.Add(steps);
        layout.SetColumnSpan(steps, 2);

        _preview.Size = new Size(LogicalToDeviceUnits(300), LogicalToDeviceUnits(450));
        _preview.TipMoved += OnTipMoved;
        layout.Controls.Add(_preview);

        var side = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, Margin = new Padding(16, 0, 0, 0) };
        _thumb.Size = new Size(LogicalToDeviceUnits(96), LogicalToDeviceUnits(96));
        side.Controls.Add(new Label { Text = "Toolbar icon", AutoSize = true });
        side.Controls.Add(_thumb);
        side.Controls.Add(new Label { Text = "Name", AutoSize = true, Margin = new Padding(0, 12, 0, 0) });
        _name.Width = LogicalToDeviceUnits(240);
        _name.TextChanged += (_, _) => UpdateAddButton();
        side.Controls.Add(_name);
        side.Controls.Add(new Label { Text = "Ink colour", AutoSize = true, Margin = new Padding(0, 12, 0, 0) });
        var inkRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        _inkSwatch.Size = new Size(LogicalToDeviceUnits(24), LogicalToDeviceUnits(24));
        _inkSwatch.Margin = new Padding(0, 3, 8, 0);
        _ink.Click += (_, _) => PickInk();
        inkRow.Controls.AddRange(new Control[] { _inkSwatch, _ink });
        side.Controls.Add(inkRow);
        side.Controls.Add(new Label
        {
            AutoSize = true,
            MaximumSize = new Size(LogicalToDeviceUnits(260), 0),
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(0, 16, 0, 0),
            Text = "The circle marks the fingertip: the exact point that touches what you point at. " +
                   "Click or drag in the picture to move it. The dashed line marks the wrist, where the hand meets the stick.",
        });
        _message.MaximumSize = new Size(LogicalToDeviceUnits(260), 0);
        _message.Margin = new Padding(0, 12, 0, 0);
        side.Controls.Add(_message);
        layout.Controls.Add(side);

        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, Margin = new Padding(0, 12, 0, 0) };
        var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        _add.Click += (_, _) => AddHand();
        buttons.Controls.AddRange(new Control[] { cancel, _add });
        layout.Controls.Add(buttons);
        layout.SetColumnSpan(buttons, 2);
        AcceptButton = _add;
        CancelButton = cancel;
        SetInk(_inkColor);
    }

    private void SaveTemplate()
    {
        using var dialog = new SaveFileDialog
        {
            Title = "Save the hand template",
            FileName = "tinyhand-template.png",
            Filter = "PNG image|*.png",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        using var image = HandImporter.Template();
        image.Save(dialog.FileName, System.Drawing.Imaging.ImageFormat.Png);
    }

    private void ChoosePicture()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Choose a hand picture",
            Filter = "Pictures|*.png;*.jpg;*.jpeg;*.webp;*.bmp|All files|*.*",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            LoadPicture(dialog.FileName);
    }

    /// <summary>Prepares and measures a picture. Public so tests can skip the file dialog.</summary>
    public void LoadPicture(string path)
    {
        Cursor = Cursors.WaitCursor;
        try
        {
            var canvas = HandImporter.Prepare(path);
            var geometry = HandImporter.Measure(canvas);
            _canvas?.Dispose();
            _canvas = canvas;
            _geometry = geometry;
            _preview.SetImage(canvas, geometry);
            SetInk(HandImporter.GuessInk(canvas, geometry));
            if (_name.Text.Length == 0)
                _name.Text = TitleFrom(path);
            Remeasure();
        }
        catch (Exception e) when (e is InvalidDataException or ArgumentException or IOException or OutOfMemoryException)
        {
            _message.Text = e is InvalidDataException ? e.Message : $"Could not read that picture: {e.Message}";
            _message.ForeColor = Color.Firebrick;
        }
        finally
        {
            Cursor = Cursors.Default;
            UpdateAddButton();
        }
    }

    private static string TitleFrom(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path).Replace('_', ' ').Replace('-', ' ').Trim();
        name = string.Join(' ', name.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (name.Length == 0)
            return "My hand";
        return char.ToUpper(name[0]) + name[1..];
    }

    private void OnTipMoved(PointF tip)
    {
        if (_geometry == null)
            return;
        // The fingertip must be on the hand, above the wrist.
        var y = Math.Clamp(tip.Y, _geometry.HandTop, _geometry.Wrist - 0.02);
        _geometry = _geometry with { Tip = y, TipX = Math.Clamp(tip.X, 0, 1), HandTop = Math.Min(_geometry.HandTop, y) };
        _preview.SetImage(_canvas!, _geometry);
        Remeasure();
    }

    private void Remeasure()
    {
        if (_canvas == null || _geometry == null)
            return;
        _thumb.Image?.Dispose();
        _thumb.Image = HandImporter.Thumbnail(_canvas, _geometry);
        var warning = HandImporter.Warning(_geometry);
        _message.Text = warning ?? "Looks good.";
        _message.ForeColor = warning == null ? Color.SeaGreen : Color.DarkGoldenrod;
    }

    private void PickInk()
    {
        using var dialog = new ColorDialog { Color = _inkColor, FullOpen = true };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            SetInk(dialog.Color);
    }

    private void SetInk(Color color)
    {
        _inkColor = color;
        _inkSwatch.BackColor = color;
    }

    private void UpdateAddButton() => _add.Enabled = _canvas != null && _geometry != null && _name.Text.Trim().Length > 0;

    private void AddHand()
    {
        if (_canvas == null || _geometry == null)
            return;
        try
        {
            var hand = HandImporter.Save(_canvas, _geometry, _name.Text.Trim(), _inkColor);
            Catalog.ReloadCustom();
            _settings.Update(d => d.PointerStyle = hand.Id);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _message.Text = $"Could not save the hand: {e.Message}";
            _message.ForeColor = Color.Firebrick;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _canvas?.Dispose();
            _thumb.Image?.Dispose();
        }
        base.Dispose(disposing);
    }

    /// <summary>The picture with the fingertip and wrist marked; click or drag to move the fingertip.</summary>
    private sealed class HandPreview : Control
    {
        private Bitmap? _image;
        private HandGeometry? _g;

        public event Action<PointF>? TipMoved;

        public HandPreview()
        {
            DoubleBuffered = true;
            BackColor = Color.White;
            Cursor = Cursors.Cross;
        }

        public void SetImage(Bitmap image, HandGeometry g)
        {
            _image = image;
            _g = g;
            Invalidate();
        }

        private RectangleF ImageRect()
        {
            var scale = Math.Min(Width / (float)HandImporter.Width, Height / (float)HandImporter.Height);
            var w = HandImporter.Width * scale;
            var h = HandImporter.Height * scale;
            return new RectangleF((Width - w) / 2, (Height - h) / 2, w, h);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var r = ImageRect();
            // A light checkerboard shows which parts are see-through.
            var cell = Math.Max(6, LogicalToDeviceUnits(8));
            using (var light = new SolidBrush(Color.FromArgb(236, 236, 236)))
                for (var y = 0; y < Height; y += cell)
                    for (var x = (y / cell) % 2 * cell; x < Width; x += cell * 2)
                        g.FillRectangle(light, x, y, cell, cell);
            using (var border = new Pen(Color.Silver))
                g.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
            if (_image == null || _g == null)
            {
                TextRenderer.DrawText(g, "Choose a picture to see it here", Font, ClientRectangle, Color.Gray,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
                return;
            }
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(_image, r);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var wristY = r.Y + (float)_g.Wrist * r.Height;
            using (var dash = new Pen(Color.FromArgb(200, 30, 144, 255), 2) { DashStyle = DashStyle.Dash })
                g.DrawLine(dash, r.X, wristY, r.Right, wristY);

            var tip = new PointF(r.X + (float)_g.TipX * r.Width, r.Y + (float)_g.Tip * r.Height);
            var radius = LogicalToDeviceUnits(9);
            using var ring = new Pen(Color.Red, 2.5f);
            g.DrawEllipse(ring, tip.X - radius, tip.Y - radius, radius * 2, radius * 2);
            g.DrawLine(ring, tip.X - radius * 1.6f, tip.Y, tip.X - radius * 0.5f, tip.Y);
            g.DrawLine(ring, tip.X + radius * 0.5f, tip.Y, tip.X + radius * 1.6f, tip.Y);
            g.DrawLine(ring, tip.X, tip.Y - radius * 1.6f, tip.X, tip.Y - radius * 0.5f);
            g.DrawLine(ring, tip.X, tip.Y + radius * 0.5f, tip.X, tip.Y + radius * 1.6f);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            MoveTip(e.Location);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (e.Button == MouseButtons.Left)
                MoveTip(e.Location);
        }

        private void MoveTip(Point p)
        {
            if (_image == null)
                return;
            var r = ImageRect();
            TipMoved?.Invoke(new PointF((p.X - r.X) / r.Width, (p.Y - r.Y) / r.Height));
        }
    }
}
