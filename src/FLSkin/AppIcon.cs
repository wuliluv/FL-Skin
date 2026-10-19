using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace FLSkin;

internal static class AppIcon
{
    private static Icon _cached;

    public static Icon Create()
    {
        if (_cached != null) return _cached;

        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.Clear(Color.Transparent);

            using var path = new GraphicsPath();
            const int r = 7;
            path.AddArc(1, 1, r, r, 180, 90);
            path.AddArc(31 - r - 1, 1, r, r, 270, 90);
            path.AddArc(31 - r - 1, 31 - r - 1, r, r, 0, 90);
            path.AddArc(1, 31 - r - 1, r, r, 90, 90);
            path.CloseFigure();

            using var brush = new LinearGradientBrush(new Rectangle(0, 0, 32, 32),
                Color.FromArgb(255, 108, 92, 231), Color.FromArgb(255, 45, 156, 219), 45f);
            g.FillPath(brush, path);

            using var font = new Font("Segoe UI", 12f, FontStyle.Bold, GraphicsUnit.Pixel);
            using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString("FL", font, Brushes.White, new RectangleF(0, 0, 32, 32), format);
        }

        _cached = Icon.FromHandle(bmp.GetHicon());
        return _cached;
    }
}
