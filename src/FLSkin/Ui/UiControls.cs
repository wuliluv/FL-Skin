using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace FLSkin.Ui;

internal static class Draw
{
    public static GraphicsPath Round(Rectangle r, int radius)
    {
        int d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        var path = new GraphicsPath();

        if (d <= 0)
        {
            path.AddRectangle(r);
            return path;
        }

        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}

/// <summary>圆角卡片容器，负责画底和 1px 描边。</summary>
internal sealed class Card : Panel
{
    public Card()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Surface;
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int Radius { get; set; } = 14;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string Title { get; set; } = string.Empty;

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using GraphicsPath path = Draw.Round(rect, Radius);
        using var fill = new SolidBrush(Theme.Surface);
        e.Graphics.FillPath(fill, path);

        using var pen = new Pen(Theme.Border);
        e.Graphics.DrawPath(pen, path);

        if (!string.IsNullOrEmpty(Title))
        {
            TextRenderer.DrawText(e.Graphics, Title, Theme.BodyBold, new Point(18, 14), Theme.TextDim,
                TextFormatFlags.NoPadding);
        }
    }
}

/// <summary>圆角缩略图（等比填满 + 圆角裁剪 + 描边）。</summary>
internal sealed class RoundedPicture : Control
{
    private Image _image;

    public RoundedPicture()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool ShowBorder { get; set; } = true;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Image Image
    {
        get => _image;
        set
        {
            if (_image != null && _image != value) _image.Dispose();
            _image = value;
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using GraphicsPath path = Draw.Round(rect, 10);

        using (var brush = new SolidBrush(Theme.SurfaceAlt))
            e.Graphics.FillPath(brush, path);

        if (_image != null)
        {
            var state = e.Graphics.Save();
            e.Graphics.SetClip(path);
            e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;

            double scale = Math.Max(Width / (double)_image.Width, Height / (double)_image.Height);
            int w = Math.Max(1, (int)Math.Round(_image.Width * scale));
            int h = Math.Max(1, (int)Math.Round(_image.Height * scale));
            e.Graphics.DrawImage(_image, new Rectangle((Width - w) / 2, (Height - h) / 2, w, h));

            e.Graphics.Restore(state);
        }
        else
        {
            TextRenderer.DrawText(e.Graphics, "无预览", Theme.Small, rect, Theme.TextFaint,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        if (ShowBorder)
        {
            using var pen = new Pen(Theme.Border);
            e.Graphics.DrawPath(pen, path);
        }
    }
}

/// <summary>扁平按钮：圆角、悬停/按下状态、可选强调色。</summary>
internal sealed class FlatButton : Control
{
    private bool _pressed;
    private float _hoverT;

    public FlatButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Font = Theme.Body;
        ForeColor = Theme.Text;
        Cursor = Cursors.Hand;
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Primary { get; set; }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Compact { get; set; }

    /// <summary>静止时的底色（顶栏那种“无边框”按钮可以传窗口底色）。</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Color IdleBack { get; set; } = Theme.SurfaceAlt;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool DrawBorder { get; set; } = true;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Color HoverBackColor { get; set; } = Theme.SurfaceHover;

    protected override void OnMouseEnter(EventArgs e)
    {
        AnimateHover(1f, 70);
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _pressed = false;
        AnimateHover(0f, 120);
        base.OnMouseLeave(e);
    }

    private void AnimateHover(float target, int duration)
    {
        float from = _hoverT;
        Motion.Run(duration, t =>
        {
            _hoverT = from + (target - from) * t;
            Invalidate();
        });
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            _pressed = true;
            Invalidate();
        }
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _pressed = false;
        Invalidate();
        base.OnMouseUp(e);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        Cursor = Enabled ? Cursors.Hand : Cursors.Default;
        Invalidate();
        base.OnEnabledChanged(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using GraphicsPath path = Draw.Round(rect, Compact ? 8 : 10);

        float glow = _pressed ? 1f : _hoverT;
        Color back;
        if (!Enabled) back = Theme.SurfaceAlt;
        else if (Primary) back = Motion.Lerp(Theme.Accent, Theme.AccentHover, glow);
        else back = Motion.Lerp(IdleBack, HoverBackColor, glow);

        using (var brush = new SolidBrush(back))
            e.Graphics.FillPath(brush, path);

        if (!Primary && DrawBorder)
        {
            using var pen = new Pen(Theme.Border);
            e.Graphics.DrawPath(pen, path);
        }

        Color text = Enabled ? Theme.Text : Theme.TextFaint;
        var textRect = _pressed ? new Rectangle(rect.X, rect.Y + 1, rect.Width, rect.Height) : rect;
        TextRenderer.DrawText(e.Graphics, Text, Font, textRect, text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}

/// <summary>开关（胶囊 + 圆点），用来替代勾选框。</summary>
internal sealed class ToggleSwitch : Control
{
    private bool _checked;
    private float _knob;
    private float _hoverT;

    public ToggleSwitch()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Size = new Size(44, 22);
        Cursor = Cursors.Hand;
    }

    public event EventHandler CheckedChanged;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value) return;
            _checked = value;

            float from = _knob;
            float to = _checked ? 1f : 0f;
            Motion.Run(210, t =>
            {
                _knob = from + (to - from) * t;
                Invalidate();
            }, null, Motion.EaseOutBack);

            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    protected override void OnClick(EventArgs e)
    {
        if (Enabled) Checked = !Checked;
        base.OnClick(e);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        AnimateHover(1f);
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        AnimateHover(0f);
        base.OnMouseLeave(e);
    }

    private void AnimateHover(float target)
    {
        float from = _hoverT;
        Motion.Run(130, t =>
        {
            _hoverT = from + (target - from) * t;
            Invalidate();
        });
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var track = new Rectangle(0, 0, Width - 1, Height - 1);
        using GraphicsPath path = Draw.Round(track, Height / 2);

        Color offBack = Enabled ? Theme.SurfaceAlt : Theme.Window;
        Color onBack = Enabled ? Motion.Lerp(Theme.Accent, Theme.AccentHover, _hoverT) : Theme.SurfaceHover;
        Color back = Motion.Lerp(offBack, onBack, _knob);

        using (var brush = new SolidBrush(back))
            e.Graphics.FillPath(brush, path);

        if (_knob < 0.98f)
        {
            using var pen = new Pen(Motion.Lerp(Theme.Border, onBack, _knob));
            e.Graphics.DrawPath(pen, path);
        }

        int knob = Height - 6;
        int travel = Width - knob - 6;
        int x = 3 + Math.Clamp((int)Math.Round(travel * _knob), 0, travel);
        using var knobBrush = new SolidBrush(Motion.Lerp(Theme.TextDim, Color.White, _knob));
        e.Graphics.FillEllipse(knobBrush, x, 3, knob, knob);
    }
}

/// <summary>现代滑块：细轨道 + 强调色进度 + 圆形拖柄。</summary>
internal sealed class ModernSlider : Control
{
    private int _min;
    private int _max = 100;
    private int _value = 50;
    private bool _dragging;
    private float _hoverT;

    public ModernSlider()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Height = 28;
        Cursor = Cursors.Hand;
    }

    public event EventHandler ValueChanged;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int Minimum
    {
        get => _min;
        set { _min = value; Value = _value; }
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int Maximum
    {
        get => _max;
        set { _max = Math.Max(_min + 1, value); Value = _value; }
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int Value
    {
        get => _value;
        set
        {
            int v = Math.Clamp(value, _min, _max);
            if (v == _value) return;
            _value = v;
            Invalidate();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private int TrackLeft => 8;
    private int TrackRight => Width - 8;

    private int ValueToX(int value)
    {
        if (_max <= _min) return TrackLeft;
        double t = (value - _min) / (double)(_max - _min);
        return (int)Math.Round(TrackLeft + t * (TrackRight - TrackLeft));
    }

    private void SetFromX(int x)
    {
        if (_max <= _min) return;
        double t = (x - TrackLeft) / (double)Math.Max(1, TrackRight - TrackLeft);
        Value = _min + (int)Math.Round(Math.Clamp(t, 0, 1) * (_max - _min));
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            _dragging = true;
            Capture = true;
            SetFromX(e.X);
        }
        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_dragging) SetFromX(e.X);
        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _dragging = false;
        Capture = false;
        base.OnMouseUp(e);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        AnimateHover(1f);
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        AnimateHover(0f);
        base.OnMouseLeave(e);
    }

    private void AnimateHover(float target)
    {
        float from = _hoverT;
        Motion.Run(130, t =>
        {
            _hoverT = from + (target - from) * t;
            Invalidate();
        });
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Left) Value -= 1;
        else if (e.KeyCode == Keys.Right) Value += 1;
        base.OnKeyDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        int cy = Height / 2;
        int x = ValueToX(_value);

        var track = new Rectangle(TrackLeft, cy - 2, Math.Max(1, TrackRight - TrackLeft), 4);
        using (GraphicsPath path = Draw.Round(track, 2))
        using (var brush = new SolidBrush(Theme.SurfaceHover))
            e.Graphics.FillPath(brush, path);

        var filled = new Rectangle(track.X, track.Y, Math.Max(1, x - track.X), track.Height);
        using (GraphicsPath path = Draw.Round(filled, 2))
        using (var brush = new SolidBrush(Enabled ? Theme.Accent : Theme.Border))
            e.Graphics.FillPath(brush, path);

        int r = (int)Math.Round(7 + 2.2 * _hoverT + (_dragging ? 1 : 0));
        var knob = new Rectangle(x - r, cy - r, r * 2, r * 2);
        using (var brush = new SolidBrush(Enabled ? Color.White : Theme.TextFaint))
            e.Graphics.FillEllipse(brush, knob);

        using var ring = new Pen(Theme.Accent, 2f);
        e.Graphics.DrawEllipse(ring, knob);
    }
}

/// <summary>深色下拉选择：自绘按钮 + 深色弹出菜单（替代系统 ComboBox）。</summary>
internal sealed class SelectBox : Control
{
    private readonly System.Collections.Generic.List<string> _items = new System.Collections.Generic.List<string>();
    private int _selectedIndex = -1;
    private float _hoverT;

    public SelectBox()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Height = 26;
        Font = Theme.Body;
        Cursor = Cursors.Hand;
        BackColor = Theme.Surface;
    }

    public event EventHandler SelectedIndexChanged;

    public string SelectedText =>
        _selectedIndex >= 0 && _selectedIndex < _items.Count ? _items[_selectedIndex] : string.Empty;

    public void AddItems(params string[] items)
    {
        _items.AddRange(items);
        if (_selectedIndex < 0 && _items.Count > 0) _selectedIndex = 0;
        Invalidate();
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            if (value == _selectedIndex) return;
            _selectedIndex = value;
            Invalidate();
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        AnimateHover(1f);
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        AnimateHover(0f);
        base.OnMouseLeave(e);
    }

    private void AnimateHover(float target)
    {
        float from = _hoverT;
        Motion.Run(130, t =>
        {
            _hoverT = from + (target - from) * t;
            Invalidate();
        });
    }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        if (!Enabled || _items.Count == 0) return;

        var menu = new ContextMenuStrip
        {
            BackColor = Theme.Surface,
            ForeColor = Theme.Text,
            ShowImageMargin = false,
            Font = Theme.Body,
            Renderer = new ToolStripProfessionalRenderer(new DarkMenuColors())
        };

        for (int i = 0; i < _items.Count; i++)
        {
            int index = i;
            var item = new ToolStripMenuItem(_items[i], null, (_, _) => SelectedIndex = index)
            {
                Checked = i == _selectedIndex,
                CheckOnClick = false,
                ForeColor = i == _selectedIndex ? Color.White : Theme.Text
            };
            menu.Items.Add(item);
        }

        menu.Show(this, new Point(0, Height + 2));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using GraphicsPath path = Draw.Round(rect, 8);

        Color back = !Enabled ? Theme.Window : Motion.Lerp(Theme.SurfaceAlt, Theme.SurfaceHover, _hoverT);
        using (var brush = new SolidBrush(back))
            e.Graphics.FillPath(brush, path);

        using (var pen = new Pen(Theme.Border))
            e.Graphics.DrawPath(pen, path);

        var textRect = new Rectangle(10, 0, Width - 34, Height);
        TextRenderer.DrawText(e.Graphics, SelectedText, Font, textRect,
            Enabled ? Theme.Text : Theme.TextFaint,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

        TextRenderer.DrawText(e.Graphics, "\u25BE", Theme.Small, new Rectangle(Width - 26, 0, 18, Height),
            Enabled ? Theme.TextDim : Theme.TextFaint,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    private sealed class DarkMenuColors : ProfessionalColorTable
    {
        public override Color MenuItemSelected => Theme.Accent;
        public override Color MenuItemSelectedGradientBegin => Theme.Accent;
        public override Color MenuItemSelectedGradientEnd => Theme.Accent;
        public override Color MenuItemBorder => Theme.Accent;
        public override Color ToolStripDropDownBackground => Theme.Surface;
        public override Color MenuBorder => Theme.Border;
        public override Color ImageMarginGradientBegin => Theme.Surface;
        public override Color ImageMarginGradientMiddle => Theme.Surface;
        public override Color ImageMarginGradientEnd => Theme.Surface;
    }
}
