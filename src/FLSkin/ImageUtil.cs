using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace FLSkin;

internal static class ImageUtil
{
    /// <summary>读成内存副本，避免锁住用户的原图文件。</summary>
    public static Bitmap LoadCopy(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        using var ms = new MemoryStream(bytes);
        using var tmp = Image.FromStream(ms);
        var copy = new Bitmap(tmp.Width, tmp.Height, PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(copy))
            g.DrawImage(tmp, new Rectangle(0, 0, tmp.Width, tmp.Height));
        return copy;
    }

    /// <summary>从现有 Image 复制一份，交给后台线程去画，避免和 UI 线程抢同一个对象。</summary>
    public static Bitmap LoadCopy(Image source)
    {
        var copy = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(copy))
            g.DrawImage(source, new Rectangle(0, 0, source.Width, source.Height));
        return copy;
    }
}
