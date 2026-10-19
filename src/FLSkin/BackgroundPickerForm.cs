using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using FLSkin.Ui;

namespace FLSkin;

/// <summary>
/// 背景库窗口：列出背景专用文件夹里的图片 / 视频，点一下就换背景。
/// 文件夹本身可以改（默认 D:\FL Skin\Backgrounds）。
/// </summary>
internal sealed class BackgroundPickerForm : Form
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    private const int WM_NCLBUTTONDOWN = 0x00A1;
    private const int HTCAPTION = 2;

    private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp" };
    private static readonly string[] VideoExtensions = { ".mp4", ".m4v", ".mov", ".wmv", ".avi" };

    private readonly List<BackgroundTile> _tiles = new List<BackgroundTile>();
    private readonly System.Windows.Forms.Timer _thumbTimer;
    private readonly AppConfig _config;
    private readonly string _currentPath;

    private Panel _grid;
    private Label _lblEmpty;
    private Label _lblCount;
    private Label _lblFolder;
    private FlatButton _btnApply;
    private int _thumbIndex;

    public BackgroundPickerForm(AppConfig config, string currentImage, string currentVideo, bool useVideo)
    {
        _config = config;
        _currentPath = useVideo ? currentVideo : currentImage;

        Text = "背景库";
        ClientSize = new Size(640, 600);
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Theme.Window;
        Font = Theme.Body;
        DoubleBuffered = true;
        Icon = AppIcon.Create();
        KeyPreview = true;
        Opacity = 0;   // 先隐着，Show 之后淡入

        _config.EnsureLibrary();

        BuildUi();

        _thumbTimer = new System.Windows.Forms.Timer { Interval = 25 };
        _thumbTimer.Tick += (_, _) => LoadNextThumbnail();

        LoadItems();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);

        int targetTop = Top;
        Top = targetTop + 16;

        Motion.Run(220, t =>
        {
            Opacity = t;
            Top = targetTop + 16 - (int)Math.Round(16 * t);
        }, () => Top = targetTop);
    }

    public string SelectedPath { get; private set; }
    public bool SelectedIsVideo { get; private set; }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);

        try
        {
            int round = 2;
            DwmSetWindowAttribute(Handle, 33, ref round, sizeof(int));
        }
        catch
        {
            // 老系统没有这个属性
        }
    }

    private void BuildUi()
    {
        var lblTitle = new Label
        {
            Text = "背景库",
            Font = new Font("Microsoft YaHei UI", 11.5f, FontStyle.Bold),
            ForeColor = Theme.Text,
            AutoSize = true,
            Location = new Point(20, 16),
            BackColor = Color.Transparent
        };

        var lblSub = new Label
        {
            Text = "图片 / 视频放进这个文件夹，这里就会列出来",
            Font = Theme.Small,
            ForeColor = Theme.TextDim,
            AutoSize = true,
            Location = new Point(20, 40),
            BackColor = Color.Transparent
        };

        var btnClose = new FlatButton
        {
            Text = "✕",
            Location = new Point(594, 15),
            Size = new Size(32, 24),
            Compact = true,
            IdleBack = Theme.Window,
            HoverBackColor = Color.FromArgb(196, 43, 28),
            DrawBorder = false,
            Font = new Font("Segoe UI", 9f)
        };
        btnClose.Click += (_, _) => Cancel();

        _lblFolder = new Label
        {
            Text = "文件夹：" + _config.LibraryPath,
            Location = new Point(20, 74),
            Size = new Size(600, 18),
            Font = Theme.Small,
            ForeColor = Theme.TextFaint,
            AutoEllipsis = true
        };

        _lblCount = new Label
        {
            Location = new Point(20, 98),
            Size = new Size(300, 16),
            Font = Theme.Small,
            ForeColor = Theme.TextFaint
        };

        _grid = new Panel
        {
            Location = new Point(12, 118),
            Size = new Size(616, 400),
            BackColor = Theme.Window,
            AutoScroll = true
        };

        _lblEmpty = new Label
        {
            Text = "这个文件夹里还没有背景\r\n把图片或视频放进去，然后点「刷新」",
            Location = new Point(20, 40),
            Size = new Size(560, 60),
            ForeColor = Theme.TextFaint,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = Theme.Body
        };
        _grid.Controls.Add(_lblEmpty);

        var btnRefresh = new FlatButton
        {
            Text = "刷新",
            Location = new Point(16, 534),
            Size = new Size(84, 36)
        };
        btnRefresh.Click += (_, _) => LoadItems();

        var btnFolder = new FlatButton
        {
            Text = "打开文件夹",
            Location = new Point(108, 534),
            Size = new Size(104, 36),
            Compact = true
        };
        btnFolder.Click += (_, _) => OpenFolder();

        var btnChange = new FlatButton
        {
            Text = "更改文件夹…",
            Location = new Point(220, 534),
            Size = new Size(116, 36),
            Compact = true
        };
        btnChange.Click += (_, _) => ChangeFolder();

        var btnCancel = new FlatButton
        {
            Text = "取消",
            Location = new Point(400, 534),
            Size = new Size(100, 36)
        };
        btnCancel.Click += (_, _) => Cancel();

        _btnApply = new FlatButton
        {
            Text = "使用这个",
            Location = new Point(510, 534),
            Size = new Size(114, 36),
            Primary = true,
            Enabled = false
        };
        _btnApply.Click += (_, _) => ApplySelection();

        Controls.AddRange(new Control[]
        {
            lblTitle, lblSub, btnClose, _lblFolder, _lblCount, _grid,
            btnRefresh, btnFolder, btnChange, btnCancel, _btnApply
        });

        MouseDown += DragWindow;
        lblTitle.MouseDown += DragWindow;
        lblSub.MouseDown += DragWindow;

        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape) Cancel();
        };
    }

    private void DragWindow(object sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        ReleaseCapture();
        SendMessage(Handle, WM_NCLBUTTONDOWN, HTCAPTION, IntPtr.Zero);
    }

    // ─────────────────────────── 列表 ───────────────────────────

    private void LoadItems()
    {
        _thumbTimer.Stop();
        _thumbIndex = 0;

        foreach (BackgroundTile tile in _tiles)
        {
            tile.Thumb = null;
            tile.Dispose();
        }
        _tiles.Clear();
        _grid.Controls.Clear();
        _grid.Controls.Add(_lblEmpty);

        SelectedPath = null;
        SelectedIsVideo = false;
        _btnApply.Enabled = false;

        List<string> files;
        try
        {
            files = Directory.EnumerateFiles(_config.LibraryPath)
                .Where(f => IsImage(f) || IsVideo(f))
                .OrderBy(f => Path.GetFileName(f), StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
        catch
        {
            files = new List<string>();
        }

        _lblFolder.Text = "文件夹：" + _config.LibraryPath;
        _lblEmpty.Visible = files.Count == 0;
        _lblCount.Text = files.Count == 0 ? "文件夹可能是空的，或者没有可用的图片 / 视频" : files.Count + " 个背景";

        const int pad = 10;
        const int cols = 3;
        int x = pad, y = pad, col = 0;

        foreach (string file in files)
        {
            var tile = new BackgroundTile(file, IsVideo(file));
            tile.Location = new Point(x, y);
            tile.Click += (_, _) => Select(tile);
            tile.DoubleClick += (_, _) =>
            {
                Select(tile);
                ApplySelection();
            };

            _grid.Controls.Add(tile);
            _tiles.Add(tile);

            if (string.Equals(file, _currentPath, StringComparison.OrdinalIgnoreCase))
                Select(tile);

            col++;
            if (col >= cols)
            {
                col = 0;
                x = pad;
                y += tile.Height + pad;
            }
            else
            {
                x += tile.Width + pad;
            }
        }

        if (_tiles.Count > 0) _thumbTimer.Start();
    }

    /// <summary>缩略图一张一张补，避免一次读几十张 4K 图卡住窗口。</summary>
    private void LoadNextThumbnail()
    {
        if (_thumbIndex >= _tiles.Count)
        {
            _thumbTimer.Stop();
            return;
        }

        BackgroundTile tile = _tiles[_thumbIndex++];
        if (tile.IsVideo) return;

        try
        {
            using Bitmap copy = ImageUtil.LoadCopy(tile.FilePath);
            const int w = 164, h = 90;
            var thumb = new Bitmap(w, h);
            using (var g = Graphics.FromImage(thumb))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                double scale = Math.Max(w / (double)copy.Width, h / (double)copy.Height);
                int tw = Math.Max(1, (int)Math.Round(copy.Width * scale));
                int th = Math.Max(1, (int)Math.Round(copy.Height * scale));
                g.DrawImage(copy, new Rectangle((w - tw) / 2, (h - th) / 2, tw, th));
            }

            tile.Thumb = thumb;
            tile.Invalidate();
        }
        catch
        {
            // 读不出来的图就留空
        }
    }

    private void Select(BackgroundTile tile)
    {
        foreach (BackgroundTile other in _tiles)
        {
            other.Selected = other == tile;
            other.Invalidate();
        }

        SelectedPath = tile.FilePath;
        SelectedIsVideo = tile.IsVideo;
        _btnApply.Enabled = true;
    }

    private void ApplySelection()
    {
        if (string.IsNullOrEmpty(SelectedPath)) return;

        _thumbTimer.Stop();
        DialogResult = DialogResult.OK;
        Close();
    }

    private void Cancel()
    {
        _thumbTimer.Stop();
        DialogResult = DialogResult.Cancel;
        Close();
    }

    private void OpenFolder()
    {
        try
        {
            _config.EnsureLibrary();
            Process.Start(new ProcessStartInfo("explorer.exe", _config.LibraryPath) { UseShellExecute = true });
        }
        catch
        {
            // 打不开就算了
        }
    }

    /// <summary>选一个新的背景库目录，记进配置并刷新列表。</summary>
    private void ChangeFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "选择背景库文件夹（图片 / 视频放这里）",
            UseDescriptionForTitle = true,
            SelectedPath = _config.LibraryPath,
            ShowNewFolderButton = true
        };

        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        _config.LibraryFolder = dialog.SelectedPath;
        _config.EnsureLibrary();
        _config.Save();
        LoadItems();
    }

    private static bool IsImage(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        return ImageExtensions.Contains(ext);
    }

    private static bool IsVideo(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        return VideoExtensions.Contains(ext);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _thumbTimer.Stop();
        foreach (BackgroundTile tile in _tiles)
            tile.Dispose();
        _tiles.Clear();
        base.OnFormClosed(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(Theme.Border);
        e.Graphics.DrawLine(pen, 16, 56, Width - 16, 56);
    }
}
