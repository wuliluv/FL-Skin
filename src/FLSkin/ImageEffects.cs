using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace FLSkin;

/// <summary>背景图的几种「质感」处理：模糊、饱和、叠色、颗粒。</summary>
internal static class ImageEffects
{
    /// <summary>近似高斯模糊：降采样 → 三次方框模糊 → 双三次放大。</summary>
    public static Bitmap Blur(Bitmap src, double radiusPx)
    {
        if (radiusPx < 0.8) return Clone(src);

        // 半径越大，先缩得越小，既快又不容易出现方块感
        int divisor = Math.Max(1, (int)Math.Round(radiusPx / 4.0));
        int w = Math.Max(1, src.Width / divisor);
        int h = Math.Max(1, src.Height / divisor);

        using var small = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(small))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(src, new Rectangle(0, 0, w, h));
        }

        int r = Math.Max(1, (int)Math.Round(radiusPx / divisor));
        BoxBlur(small, r);
        BoxBlur(small, r);
        BoxBlur(small, r);

        var result = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(result))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(small, new Rectangle(0, 0, src.Width, src.Height));
        }

        return result;
    }

    public static Bitmap Clone(Bitmap src)
    {
        var copy = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(copy);
        g.DrawImage(src, new Rectangle(0, 0, src.Width, src.Height));
        return copy;
    }

    /// <summary>提高 / 降低饱和度（保持亮度）。</summary>
    public static void Saturate(Bitmap bmp, float amount)
    {
        float s = amount;
        float r = 0.213f, gg = 0.715f, b = 0.072f;
        var matrix = new ColorMatrix(new[]
        {
            new[] { r + (1 - r) * s, r - r * s, r - r * s, 0f, 0f },
            new[] { gg - gg * s, gg + (1 - gg) * s, gg - gg * s, 0f, 0f },
            new[] { b - b * s, b - b * s, b + (1 - b) * s, 0f, 0f },
            new[] { 0f, 0f, 0f, 1f, 0f },
            new[] { 0f, 0f, 0f, 0f, 1f }
        });

        using var attrs = new ImageAttributes();
        attrs.SetColorMatrix(matrix);
        using var g = Graphics.FromImage(bmp);
        g.DrawImage(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height),
            0, 0, bmp.Width, bmp.Height, GraphicsUnit.Pixel, attrs);
    }

    /// <summary>整层叠一种带透明度的颜色（磨砂白 / 亚克力色调）。</summary>
    public static void OverlayTint(Bitmap bmp, Color color, int alpha)
    {
        if (alpha <= 0) return;
        using var g = Graphics.FromImage(bmp);
        using var brush = new SolidBrush(Color.FromArgb(Math.Clamp(alpha, 0, 255), color));
        g.FillRectangle(brush, 0, 0, bmp.Width, bmp.Height);
    }

    /// <summary>加胶片颗粒（亚克力的经典质感），amplitude 为 ±RGB 幅度。</summary>
    public static void AddNoise(Bitmap bmp, int amplitude)
    {
        if (amplitude <= 0) return;

        var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
        BitmapData data = bmp.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        try
        {
            int stride = data.Stride;
            int bytes = stride * bmp.Height;
            byte[] buffer = new byte[bytes];
            Marshal.Copy(data.Scan0, buffer, 0, bytes);

            uint seed = 0x9E3779B9;
            int amp = Math.Clamp(amplitude, 1, 40);

            for (int y = 0; y < bmp.Height; y++)
            {
                int row = y * stride;
                for (int x = 0; x < bmp.Width; x++)
                {
                    seed ^= seed << 13;
                    seed ^= seed >> 17;
                    seed ^= seed << 5;
                    int n = (int)(seed % (uint)(amp * 2 + 1)) - amp;

                    int i = row + x * 4;
                    buffer[i] = ClampByte(buffer[i] + n);
                    buffer[i + 1] = ClampByte(buffer[i + 1] + n);
                    buffer[i + 2] = ClampByte(buffer[i + 2] + n);
                }
            }

            Marshal.Copy(buffer, 0, data.Scan0, bytes);
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }

    private static byte ClampByte(int value) => (byte)(value < 0 ? 0 : value > 255 ? 255 : value);

    /// <summary>可分离的方框模糊（前缀和实现，O(w·h)）。</summary>
    private static void BoxBlur(Bitmap bmp, int radius)
    {
        if (radius < 1) return;

        int w = bmp.Width;
        int h = bmp.Height;
        var rect = new Rectangle(0, 0, w, h);
        BitmapData data = bmp.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);

        try
        {
            int stride = data.Stride;
            int bytes = stride * h;
            byte[] src = new byte[bytes];
            byte[] tmp = new byte[bytes];
            Marshal.Copy(data.Scan0, src, 0, bytes);
            Buffer.BlockCopy(src, 0, tmp, 0, bytes);

            long[] prefix = new long[Math.Max(w, h) + 1];

            for (int ch = 0; ch < 3; ch++)
            {
                // 水平
                for (int y = 0; y < h; y++)
                {
                    int row = y * stride;
                    long acc = 0;
                    prefix[0] = 0;
                    for (int x = 0; x < w; x++)
                    {
                        acc += src[row + x * 4 + ch];
                        prefix[x + 1] = acc;
                    }

                    for (int x = 0; x < w; x++)
                    {
                        int x0 = Math.Max(0, x - radius);
                        int x1 = Math.Min(w - 1, x + radius);
                        long sum = prefix[x1 + 1] - prefix[x0];
                        tmp[row + x * 4 + ch] = (byte)(sum / (x1 - x0 + 1));
                    }
                }

                // 垂直
                for (int x = 0; x < w; x++)
                {
                    long acc = 0;
                    prefix[0] = 0;
                    for (int y = 0; y < h; y++)
                    {
                        acc += tmp[y * stride + x * 4 + ch];
                        prefix[y + 1] = acc;
                    }

                    for (int y = 0; y < h; y++)
                    {
                        int y0 = Math.Max(0, y - radius);
                        int y1 = Math.Min(h - 1, y + radius);
                        long sum = prefix[y1 + 1] - prefix[y0];
                        src[y * stride + x * 4 + ch] = (byte)(sum / (y1 - y0 + 1));
                    }
                }
            }

            Marshal.Copy(src, 0, data.Scan0, bytes);
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }
}
