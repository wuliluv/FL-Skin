using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;
using FLSkin.Ui;

namespace FLSkin;

/// <summary>背景库里的一个条目：静态图片或动态视频。</summary>
internal sealed class BackgroundTile : Control
{
    private static readonly Color CaptionColor = Theme.TextDim;

    public BackgroundTile(string path, bool isVideo)
    {
        FilePath = path;
        IsVideo = isVideo;
        Caption = (isVideo ? "[动态] " : "[静态] ") + Path.GetFileName(path);

        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Size = new Size(180, 132);
        Cursor = Cursors.Hand;
        BackColor = Theme.Surface;
    }

    public string FilePath { get; }
    public bool IsVideo { get; }
    public string Caption { get; }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Image Thumb { get; set; }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Selected { get; set; }

    private float _hoverT;

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
        Motion.Run(110, t =>
        {
            _hoverT = from + (target - from) * t;
            Invalidate();
        });
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
        using (GraphicsPath card = Draw.Round(bounds, 10))
        {
            Color baseColor = Motion.Lerp(Theme.SurfaceAlt, Theme.SurfaceHover, _hoverT);
            Color back = Selected ? Motion.Lerp(baseColor, Color.FromArgb(46, 43, 74), 1f) : baseColor;
            using (var brush = new SolidBrush(back))
                e.Graphics.FillPath(brush, card);

            using var pen = new Pen(Selected ? Theme.Accent : Theme.Border, Selected ? 2f : 1f);
            e.Graphics.DrawPath(pen, card);
        }

        var thumb = new Rectangle(8, 8, Width - 16, 90);
        using (GraphicsPath clip = Draw.Round(thumb, 8))
        {
            using (var brush = new SolidBrush(Color.FromArgb(18, 18, 22)))
                e.Graphics.FillPath(brush, clip);

            if (Thumb != null)
            {
                var state = e.Graphics.Save();
                e.Graphics.SetClip(clip);
                e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                double scale = Math.Max(thumb.Width / (double)Thumb.Width, thumb.Height / (double)Thumb.Height);
                int w = Math.Max(1, (int)Math.Round(Thumb.Width * scale));
                int h = Math.Max(1, (int)Math.Round(Thumb.Height * scale));
                e.Graphics.DrawImage(Thumb, new Rectangle(thumb.X + (thumb.Width - w) / 2,
                    thumb.Y + (thumb.Height - h) / 2, w, h));
                e.Graphics.Restore(state);
            }
            else if (IsVideo)
            {
                DrawPlayGlyph(e.Graphics, thumb);
            }
            else
            {
                using var brush = new SolidBrush(Theme.SurfaceHover);
                e.Graphics.FillPath(brush, clip);
            }
        }

        if (IsVideo && Thumb != null)
            DrawPlayGlyph(e.Graphics, thumb, 0.55f);

        TextRenderer.DrawText(e.Graphics, Caption, Theme.Small,
            new Rectangle(10, 102, Width - 20, 22), CaptionColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    private static void DrawPlayGlyph(Graphics g, Rectangle area, float scale = 1f)
    {
        int r = (int)(22 * scale);
        var center = new Point(area.X + area.Width / 2, area.Y + area.Height / 2);

        using (var brush = new SolidBrush(Color.FromArgb(150, 12, 12, 16)))
            g.FillEllipse(brush, center.X - r, center.Y - r, r * 2, r * 2);

        using var pen = new Pen(Color.FromArgb(230, 255, 255, 255), 2f);
        g.DrawEllipse(pen, center.X - r, center.Y - r, r * 2, r * 2);

        var triangle = new[]
        {
            new Point(center.X - r / 3, center.Y - r / 2),
            new Point(center.X - r / 3, center.Y + r / 2),
            new Point(center.X + r / 2, center.Y)
        };
        using var fill = new SolidBrush(Color.White);
        g.FillPolygon(fill, triangle);
    }
}
