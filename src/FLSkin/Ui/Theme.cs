using System.Drawing;

namespace FLSkin;

/// <summary>界面配色与字体，集中放一处，方便统一调整。</summary>
internal static class Theme
{
    public static readonly Color Window = Color.FromArgb(22, 22, 26);
    public static readonly Color Surface = Color.FromArgb(31, 31, 37);
    public static readonly Color SurfaceAlt = Color.FromArgb(40, 40, 48);
    public static readonly Color SurfaceHover = Color.FromArgb(49, 49, 58);
    public static readonly Color Border = Color.FromArgb(55, 55, 65);
    public static readonly Color Text = Color.FromArgb(236, 237, 242);
    public static readonly Color TextDim = Color.FromArgb(148, 151, 162);
    public static readonly Color TextFaint = Color.FromArgb(110, 113, 124);
    public static readonly Color Accent = Color.FromArgb(108, 92, 231);
    public static readonly Color AccentHover = Color.FromArgb(126, 111, 240);
    public static readonly Color Good = Color.FromArgb(72, 199, 130);
    public static readonly Color Warn = Color.FromArgb(232, 160, 70);

    public static readonly Font Body = new Font("Microsoft YaHei UI", 9f);
    public static readonly Font BodyBold = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold);
    public static readonly Font Small = new Font("Microsoft YaHei UI", 8.5f);
    public static readonly Font Title = new Font("Microsoft YaHei UI", 12f, FontStyle.Bold);
    public static readonly Font Big = new Font("Segoe UI", 20f, FontStyle.Bold);
}
