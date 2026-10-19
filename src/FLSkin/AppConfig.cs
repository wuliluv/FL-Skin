using System;
using System.IO;
using System.Text.Json;

namespace FLSkin;

public enum FillMode
{
    Cover = 0,     // 填满（裁掉多余部分）
    Contain = 1,   // 完整显示（留黑边）
    Stretch = 2,   // 拉伸铺满
    Tile = 3,      // 平铺
    Center = 4     // 原始大小居中
}

public enum BackdropStyle
{
    Normal = 0,    // 原图
    Glass = 1,     // 玻璃模糊（磨砂）
    Acrylic = 2    // 亚克力（模糊 + 饱和 + 颗粒 + 色调）
}

public sealed class AppConfig
{
    public string ImagePath { get; set; } = string.Empty;
    public FillMode FillMode { get; set; } = FillMode.Cover;
    public BackdropStyle Style { get; set; } = BackdropStyle.Normal;
    public int BlurStrength { get; set; } = 35;       // 0..100，玻璃 / 亚克力时生效
    public int Dim { get; set; } = 12;                // 0..80 压暗 %
    public bool BackdropEnabled { get; set; } = true;
    public int OpacityPercent { get; set; } = 75;     // 30..100（默认 75%）

    // 动态背景（MP4）
    public bool UseVideo { get; set; } = false;
    public string VideoPath { get; set; } = string.Empty;
    public bool VideoMuted { get; set; } = true;

    // 首次启动是否已经给用户看过 README
    public bool ReadmeShown { get; set; } = false;

    // 背景库目录（默认 D:\FL Skin\Backgrounds，可在界面里改）
    public string LibraryFolder { get; set; } = string.Empty;

    private static string Folder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FLSkin");

    public static string ConfigPath => Path.Combine(Folder, "config.json");

    /// <summary>
    /// 背景库默认位置：分发时和程序放在一起，所以放 D 盘。
    /// 没有 D 盘时才退回「图片\FLSkin」。
    /// </summary>
    public static string DefaultLibraryFolder
    {
        get
        {
            try
            {
                if (Directory.Exists("D:\\"))
                    return "D:\\FL Skin\\Backgrounds";
            }
            catch
            {
                // 忽略
            }

            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "FLSkin");
        }
    }

    /// <summary>实际使用的背景库目录（可以在界面里改）。</summary>
    public string LibraryPath =>
        string.IsNullOrWhiteSpace(LibraryFolder) ? DefaultLibraryFolder : LibraryFolder;

    /// <summary>建目录，并把老版本（图片\FLSkin）里的东西搬过来一次。</summary>
    public void EnsureLibrary()
    {
        try
        {
            Directory.CreateDirectory(LibraryPath);

            string legacy = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "FLSkin");

            bool copyNeeded = !string.Equals(legacy, LibraryPath, StringComparison.OrdinalIgnoreCase)
                              && Directory.Exists(legacy)
                              && !Directory.EnumerateFiles(LibraryPath).GetEnumerator().MoveNext();

            if (copyNeeded)
            {
                foreach (string file in Directory.EnumerateFiles(legacy))
                {
                    var info = new FileInfo(file);
                    if (info.Length > 200L * 1024 * 1024) continue;   // 太大的视频不搬，省时间
                    File.Copy(file, Path.Combine(LibraryPath, info.Name), true);
                }
            }

            // 当前背景如果还指向老目录，改指到库里，免得显示的是库外的路径
            string newImage = MigratePath(legacy, ImagePath);
            string newVideo = MigratePath(legacy, VideoPath);
            bool changed = !string.Equals(newImage, ImagePath, StringComparison.Ordinal)
                           || !string.Equals(newVideo, VideoPath, StringComparison.Ordinal);

            ImagePath = newImage;
            VideoPath = newVideo;

            if (changed) Save();
        }
        catch
        {
            // 建不了 / 搬不了就算了
        }
    }

    private string MigratePath(string legacyRoot, string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return path;
        if (!path.StartsWith(legacyRoot, StringComparison.OrdinalIgnoreCase)) return path;

        string dest = Path.Combine(LibraryPath, Path.GetFileName(path));
        return File.Exists(dest) ? dest : path;
    }

    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var cfg = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(ConfigPath));
                if (cfg != null)
                {
                    cfg.Clamp();
                    return cfg;
                }
            }
        }
        catch
        {
            // 配置坏了就用默认值，不影响启动
        }

        return new AppConfig();
    }

    public void Save()
    {
        try
        {
            Clamp();
            Directory.CreateDirectory(Folder);
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(this, new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            }));
        }
        catch
        {
            // 写不进去就算了，不值得弹窗打断用户
        }
    }

    public void Clamp()
    {
        BlurStrength = Math.Clamp(BlurStrength, 0, 100);
        Dim = Math.Clamp(Dim, 0, 80);
        OpacityPercent = Math.Clamp(OpacityPercent, 30, 100);
        if (!Enum.IsDefined(typeof(FillMode), FillMode)) FillMode = FillMode.Cover;
        if (!Enum.IsDefined(typeof(BackdropStyle), Style)) Style = BackdropStyle.Normal;
        if (UseVideo && string.IsNullOrWhiteSpace(VideoPath)) UseVideo = false;
        if (string.IsNullOrWhiteSpace(LibraryFolder)) LibraryFolder = DefaultLibraryFolder;
    }
}
