using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FLSkin;

/// <summary>
/// 贴在 FL Studio 主窗口正下方的无边框窗口。
/// 可以是静态图片（正常 / 玻璃 / 亚克力，可压暗），也可以是 MP4 循环播放。
/// FL 窗口变透明后，这一层就透出来，形成「自定义背景」。
///
/// 静态图的模糊 / 颗粒在后台线程算，算完再换上去 —— 拖滑块时不会卡住 UI。
/// </summary>
internal sealed class BackdropForm : Form
{
    private Image _source;
    private string _sourceStamp = string.Empty;

    private Bitmap _sourceClone;
    private Bitmap _cache;
    private string _cacheKey = string.Empty;
    private string _pendingKey = string.Empty;
    private Size _cacheSize = Size.Empty;

    // 动态背景（MP4）
    private System.Windows.Forms.Integration.ElementHost _videoHost;
    private System.Windows.Controls.MediaElement _video;
    private string _videoPath = string.Empty;
    private bool _videoPlaying;

    private bool _allowClose;

    public BackdropForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Text = "FLSkin.Backdrop";
        BackColor = Color.FromArgb(20, 20, 24);
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        Bounds = new Rectangle(0, 0, 1, 1);
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW;
            return cp;
        }
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public FillMode FillMode { get; set; } = FillMode.Cover;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public BackdropStyle Style { get; set; } = BackdropStyle.Normal;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int BlurStrength { get; set; } = 35;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int Dim { get; set; }

    public bool HasImage => _source != null;

    public bool HasVideo => _video != null;

    /// <summary>上一次 Follow 没能把背景窗口排到 FL 正下方。</summary>
    public bool PlacementFailed { get; private set; }

    // ─────────────────────────── 素材 ───────────────────────────

    public void SetImage(string path)
    {
        if (string.Equals(path, _sourceStamp, StringComparison.OrdinalIgnoreCase) && _source != null)
            return;

        _source?.Dispose();
        _source = null;
        _sourceStamp = string.Empty;

        if (!string.IsNullOrWhiteSpace(path))
        {
            try
            {
                _source = ImageUtil.LoadCopy(path);
                _sourceClone = ImageUtil.LoadCopy(_source);
                _sourceStamp = path;
            }
            catch
            {
                _source = null;
                _sourceClone = null;
            }
        }

        InvalidateCache();
    }

    /// <summary>切换背景来源：path 为空表示回到静态图片。</summary>
    public void SetVideo(string path, bool muted)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            DisposeVideo();
            Invalidate();
            return;
        }

        if (_video != null && string.Equals(path, _videoPath, StringComparison.OrdinalIgnoreCase))
        {
            _video.IsMuted = muted;
            _video.Volume = muted ? 0 : 1;
            return;
        }

        EnsureVideoHost();

        try
        {
            _videoPath = path;
            _video.IsMuted = muted;
            _video.Volume = muted ? 0 : 1;
            _video.Source = new Uri(path);
            _video.Play();
            _videoPlaying = true;
        }
        catch
        {
            DisposeVideo();
        }

        Invalidate();
    }

    private void EnsureVideoHost()
    {
        if (_videoHost != null) return;

        _video = new System.Windows.Controls.MediaElement
        {
            LoadedBehavior = System.Windows.Controls.MediaState.Manual,
            ScrubbingEnabled = false,
            Stretch = System.Windows.Media.Stretch.UniformToFill,
            IsMuted = true,
            Volume = 0
        };

        _video.MediaEnded += (_, _) =>
        {
            if (_video == null) return;
            _video.Position = TimeSpan.Zero;
            _video.Play();
        };

        _video.MediaFailed += (_, _) => DisposeVideo();

        _videoHost = new System.Windows.Forms.Integration.ElementHost
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Black,
            Child = _video
        };

        Controls.Add(_videoHost);
    }

    private void PlayVideo()
    {
        if (_video == null || _videoPlaying) return;
        _video.Play();
        _videoPlaying = true;
    }

    private void PauseVideo()
    {
        if (_video == null || !_videoPlaying) return;
        _video.Pause();
        _videoPlaying = false;
    }

    private void DisposeVideo()
    {
        if (_videoHost != null)
        {
            Controls.Remove(_videoHost);
            _videoHost.Dispose();
            _videoHost = null;
        }

        _video = null;
        _videoPath = string.Empty;
        _videoPlaying = false;
        Invalidate();
    }

    // ─────────────────────────── 跟随 FL ───────────────────────────

    public bool Follow(IntPtr flWindow)
    {
        var rect = new Native.RECT();
        if (flWindow == IntPtr.Zero
            || !Native.IsWindow(flWindow)
            || Native.IsIconic(flWindow)
            || !Native.IsWindowVisible(flWindow)
            || !Native.GetWindowRect(flWindow, out rect)
            || rect.Width < 120 || rect.Height < 120)
        {
            HideBackdrop();
            return false;
        }

        if (!Visible)
            Show();

        PlayVideo();

        bool alreadyBelow = Native.GetWindow(Handle, Native.GW_HWNDPREV) == flWindow;
        uint flags = Native.SWP_NOACTIVATE | Native.SWP_NOOWNERZORDER;
        if (alreadyBelow)
            flags |= Native.SWP_NOZORDER;

        Native.SetWindowPos(Handle, flWindow, rect.Left, rect.Top, rect.Width, rect.Height, flags);

        PlacementFailed = Native.GetWindow(Handle, Native.GW_HWNDPREV) != flWindow;

        if (PlacementFailed)
        {
            Native.SetWindowPos(Handle, flWindow, rect.Left, rect.Top, rect.Width, rect.Height,
                Native.SWP_NOACTIVATE | Native.SWP_NOOWNERZORDER);
            PlacementFailed = Native.GetWindow(Handle, Native.GW_HWNDPREV) != flWindow;

            if (PlacementFailed)
            {
                HideBackdrop();
                return false;
            }
        }

        if (ClientSize.Width != rect.Width || ClientSize.Height != rect.Height)
            InvalidateCache();

        return true;
    }

    public void HideBackdrop()
    {
        if (!Visible) return;
        PauseVideo();
        Hide();
    }

    public void InvalidateCache()
    {
        _cacheKey = string.Empty;
        _pendingKey = string.Empty;
        Invalidate();
    }

    // ─────────────────────────── 绘制 ───────────────────────────

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        InvalidateCache();
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        // 完全由 OnPaint 负责，避免闪白
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (HasVideo)
        {
            e.Graphics.Clear(Color.Black);
            return;
        }

        EnsureCache();

        if (_cache != null)
            e.Graphics.DrawImageUnscaled(_cache, 0, 0);
        else
            e.Graphics.Clear(Color.FromArgb(18, 18, 22));

        // 压暗只是盖一层半透明黑，不重算模糊图 —— 拖滑块零延迟
        if (Dim > 0)
        {
            int alpha = Math.Clamp(Dim, 0, 80) * 255 / 100;
            using var brush = new SolidBrush(Color.FromArgb(alpha, 0, 0, 0));
            e.Graphics.FillRectangle(brush, ClientRectangle);
        }
    }

    private string CacheKey(Size size) =>
        string.Join("|", _sourceStamp, FillMode, Style, BlurStrength, size.Width, size.Height);

    /// <summary>看看现有缓存是否匹配，不匹配就丢后台去算。</summary>
    private void EnsureCache()
    {
        if (HasVideo) return;

        Size size = ClientSize;
        if (size.Width <= 0 || size.Height <= 0) return;

        string key = CacheKey(size);
        if (_cache != null && key == _cacheKey) return;
        if (key == _pendingKey) return;

        StartRebuild(key, size);
    }

    private void StartRebuild(string key, Size size)
    {
        _pendingKey = key;

        Image source = _sourceClone;   // 只读共享，后台线程只读不释放
        FillMode fill = FillMode;
        BackdropStyle style = Style;
        int blur = BlurStrength;

        Task.Run(() =>
        {
            Bitmap bitmap = null;
            try
            {
                bitmap = BuildCore(source, size, fill, style, blur);
            }
            catch
            {
                bitmap = null;
            }
            try
            {
                BeginInvoke(new Action(() =>
                {
                    if (IsDisposed)
                    {
                        bitmap?.Dispose();
                        return;
                    }

                    if (_pendingKey != key)
                    {
                        bitmap?.Dispose();
                        return;
                    }

                    _pendingKey = string.Empty;
                    _cache?.Dispose();
                    _cache = bitmap;
                    _cacheKey = key;
                    _cacheSize = size;
                    Invalidate();
                }));
            }
            catch
            {
                bitmap?.Dispose();
            }
        });
    }

    /// <summary>纯计算，不碰控件状态，可以放心在后台线程跑。</summary>
    private static Bitmap BuildCore(Image source, Size size, FillMode fill, BackdropStyle style, int blur)
    {
        int width = Math.Max(1, size.Width);
        int height = Math.Max(1, size.Height);

        Bitmap canvas = new Bitmap(width, height, PixelFormat.Format32bppPArgb);

        using (var g = Graphics.FromImage(canvas))
        {
            g.Clear(Color.FromArgb(16, 16, 20));
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.SmoothingMode = SmoothingMode.HighQuality;

            Image src = source ?? CreateDefaultImage(width, height);
            try
            {
                Draw(g, src, width, height, fill);
            }
            finally
            {
                if (source == null) src.Dispose();
            }
        }

        Bitmap result = canvas;

        if (style == BackdropStyle.Glass)
        {
            double radius = 6 + blur * 0.55;
            Bitmap blurred = ImageEffects.Blur(result, radius);
            result.Dispose();
            result = blurred;
            ImageEffects.OverlayTint(result, Color.White, 16);
        }
        else if (style == BackdropStyle.Acrylic)
        {
            double radius = 10 + blur * 0.95;
            Bitmap blurred = ImageEffects.Blur(result, radius);
            result.Dispose();
            result = blurred;
            ImageEffects.Saturate(result, 1.35f);
            ImageEffects.OverlayTint(result, Color.FromArgb(22, 26, 34), 26);
            ImageEffects.AddNoise(result, 6);
        }

        return result;
    }

    private static void Draw(Graphics g, Image src, int width, int height, FillMode fillMode)
    {
        switch (fillMode)
        {
            case FillMode.Stretch:
                g.DrawImage(src, new Rectangle(0, 0, width, height));
                break;

            case FillMode.Tile:
                using (var brush = new TextureBrush(src, WrapMode.Tile))
                    g.FillRectangle(brush, 0, 0, width, height);
                break;

            case FillMode.Center:
            {
                int x = (width - src.Width) / 2;
                int y = (height - src.Height) / 2;
                using (var brush = new SolidBrush(Color.FromArgb(20, 20, 24)))
                    g.FillRectangle(brush, 0, 0, width, height);
                g.DrawImageUnscaled(src, x, y);
                break;
            }

            case FillMode.Contain:
            {
                double scale = Math.Min(width / (double)src.Width, height / (double)src.Height);
                int w = Math.Max(1, (int)Math.Round(src.Width * scale));
                int h = Math.Max(1, (int)Math.Round(src.Height * scale));
                int x = (width - w) / 2;
                int y = (height - h) / 2;
                using (var brush = new SolidBrush(Color.FromArgb(20, 20, 24)))
                    g.FillRectangle(brush, 0, 0, width, height);
                g.DrawImage(src, new Rectangle(x, y, w, h));
                break;
            }

            default: // Cover
            {
                double scale = Math.Max(width / (double)src.Width, height / (double)src.Height);
                int w = Math.Max(1, (int)Math.Round(src.Width * scale));
                int h = Math.Max(1, (int)Math.Round(src.Height * scale));
                int x = (width - w) / 2;
                int y = (height - h) / 2;
                g.DrawImage(src, new Rectangle(x, y, w, h));
                break;
            }
        }
    }

    /// <summary>没选图片时用的默认背景：深色渐变 + 一点冷光晕。</summary>
    private static Bitmap CreateDefaultImage(int width, int height)
    {
        var bmp = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(bmp);

        using (var brush = new LinearGradientBrush(
                   new Rectangle(0, 0, width, height),
                   Color.FromArgb(32, 38, 58),
                   Color.FromArgb(12, 12, 16),
                   45f))
        {
            g.FillRectangle(brush, 0, 0, width, height);
        }

        using (var glow = new GraphicsPath())
        {
            int size = (int)(Math.Max(width, height) * 1.15);
            glow.AddEllipse(width * 0.62f - size / 2f, height * 0.22f - size / 2f, size, size);
            using var path = new PathGradientBrush(glow)
            {
                CenterColor = Color.FromArgb(120, 88, 140, 255),
                SurroundColors = new[] { Color.FromArgb(0, 88, 140, 255) }
            };
            g.FillPath(path, glow);
        }

        using (var glow2 = new GraphicsPath())
        {
            int size = (int)(Math.Max(width, height) * 0.9);
            glow2.AddEllipse(width * 0.15f - size / 2f, height * 0.95f - size / 2f, size, size);
            using var path = new PathGradientBrush(glow2)
            {
                CenterColor = Color.FromArgb(90, 255, 96, 160),
                SurroundColors = new[] { Color.FromArgb(0, 255, 96, 160) }
            };
            g.FillPath(path, glow2);
        }

        return bmp;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_allowClose && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            return;
        }

        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _allowClose = true;
            DisposeVideo();
            _cache?.Dispose();
            _source?.Dispose();
            _sourceClone?.Dispose();
        }
        base.Dispose(disposing);
    }
}
