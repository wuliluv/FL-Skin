using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using FLSkin.Ui;

namespace FLSkin;

internal sealed class SettingsForm : Form
{
    private const int HotkeyOpacityUp = 0x5101;
    private const int HotkeyOpacityDown = 0x5102;
    private const int HotkeyToggleBackdrop = 0x5103;
    private const int HotkeyShow = 0x5104;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    private const int WM_NCLBUTTONDOWN = 0x00A1;
    private const int HTCAPTION = 2;

    private readonly AppConfig _config;
    private bool _loading;
    private int _fadeToken;

    private RoundedPicture _preview;
    private FlatButton _btnPick;
    private FlatButton _btnFolder;
    private Label _lblFile;
    private ToggleSwitch _toggleBackdrop;
    private Panel _imagePanel;
    private Panel _videoPanel;
    private Label _lblVideoFile;
    private SelectBox _cmbFill;
    private SelectBox _cmbStyle;
    private ModernSlider _sliderBlur;
    private Label _lblBlurVal;
    private ModernSlider _sliderDim;
    private Label _lblDimVal;
    private ModernSlider _sliderOpacity;
    private Label _lblOpacityVal;
    private Label _lblStatus;
    private FlatButton _btnLaunch;
    private Label _lblStatusDot;

    public event Action BackdropChanged;
    public event Action OpacityChanged;
    public event Action ResetRequested;
    public event Action QuitRequested;
    public event Action HideRequested;
    public event Action LaunchRequested;
    public event Action<int> OpacityStepRequested;
    public event Action ToggleBackdropRequested;

    public SettingsForm(AppConfig config)
    {
        _config = config;

        Text = "FLSkin";
        ClientSize = new Size(470, 664);
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Window;
        Font = Theme.Body;
        DoubleBuffered = true;
        Icon = AppIcon.Create();

        BuildUi();
        LoadFromConfig();
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;

            // 无边框窗口默认没有最小化/系统菜单位，加上之后
            // 点任务栏按钮才会正常最小化（再点一次还原）。
            cp.Style |= 0x00020000;   // WS_MINIMIZEBOX
            cp.Style |= 0x00080000;   // WS_SYSMENU
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);

        // Windows 11：让无边框窗口也有圆角
        try
        {
            int round = 2; // DWMWCP_ROUND
            DwmSetWindowAttribute(Handle, 33, ref round, sizeof(int));
        }
        catch
        {
            // 老系统没有这个属性，忽略
        }

        BindHotkeys();
    }

    // ─────────────────────────── 布局 ───────────────────────────

    private void BuildUi()
    {
        BuildTitleBar();
        BuildBackgroundCard();
        BuildOpacityCard();
        BuildStatusCard();
        BuildBottomButtons();
    }

    private void BuildTitleBar()
    {
        var icon = new RoundedPicture
        {
            Location = new Point(16, 14),
            Size = new Size(26, 26),
            ShowBorder = false
        };

        try
        {
            icon.Image = AppIcon.Create().ToBitmap();
        }
        catch
        {
            // 图标取不到就算了
        }

        var lblTitle = new Label
        {
            Text = "FLSkin",
            Font = new Font("Microsoft YaHei UI", 11.5f, FontStyle.Bold),
            ForeColor = Theme.Text,
            AutoSize = true,
            Location = new Point(52, 16),
            BackColor = Color.Transparent,
            Cursor = Cursors.Hand
        };

        lblTitle.MouseEnter += (_, _) => lblTitle.ForeColor = Theme.Accent;
        lblTitle.MouseLeave += (_, _) => lblTitle.ForeColor = Theme.Text;
        lblTitle.Click += (_, _) => ShowAbout();

        int titleWidth = TextRenderer.MeasureText(lblTitle.Text, lblTitle.Font).Width;

        var lblSub = new Label
        {
            Text = "FL Studio 背景与透明度",
            Font = Theme.Small,
            ForeColor = Theme.TextDim,
            AutoSize = true,
            Location = new Point(52 + titleWidth + 12, 20),
            BackColor = Color.Transparent
        };

        var btnMin = new FlatButton
        {
            Text = "\u2014",
            Location = new Point(386, 15),
            Size = new Size(32, 24),
            Compact = true,
            IdleBack = Theme.Window,
            HoverBackColor = Theme.SurfaceHover,
            DrawBorder = false,
            Font = new Font("Segoe UI", 10f)
        };
        btnMin.Click += (_, _) => HideRequested?.Invoke();

        var btnClose = new FlatButton
        {
            Text = "\u2715",
            Location = new Point(424, 15),
            Size = new Size(32, 24),
            Compact = true,
            IdleBack = Theme.Window,
            HoverBackColor = Color.FromArgb(196, 43, 28),
            DrawBorder = false,
            Font = new Font("Segoe UI", 9f)
        };
        btnClose.Click += (_, _) => HideRequested?.Invoke();

        Controls.AddRange(new Control[] { icon, lblTitle, lblSub, btnMin, btnClose });

        // 拖动顶栏移动窗口
        // 标题只负责「点开关于」，拖动交给图标 / 副标题 / 空白区域，
        // 否则 MouseDown 会进入窗口拖动循环，Click 永远不会触发。
        MouseDown += DragWindow;
        icon.MouseDown += DragWindow;
        lblSub.MouseDown += DragWindow;
    }

    private void ShowAbout()
    {
        using var about = new AboutForm();
        about.ShowDialog(this);
    }

    private void DragWindow(object sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        ReleaseCapture();
        SendMessage(Handle, WM_NCLBUTTONDOWN, HTCAPTION, IntPtr.Zero);
    }

    private void BuildBackgroundCard()
    {
        var card = new Card
        {
            Location = new Point(16, 60),
            Size = new Size(438, 338),
            Title = "背景"
        };

        _preview = new RoundedPicture { Location = new Point(16, 50), Size = new Size(120, 68) };

        _btnPick = new FlatButton
        {
            Text = "选择背景…",
            Location = new Point(150, 50),
            Size = new Size(150, 30),
            Compact = true,
            Primary = true
        };
        _btnPick.Click += (_, _) => OpenPicker();

        _btnFolder = new FlatButton
        {
            Text = "背景文件夹",
            Location = new Point(312, 50),
            Size = new Size(110, 30),
            Compact = true,
            IdleBack = Theme.SurfaceAlt
        };
        _btnFolder.Click += (_, _) => OpenBackgroundFolder();

        _lblFile = new Label
        {
            Location = new Point(16, 124),
            Size = new Size(406, 16),
            Font = Theme.Small,
            ForeColor = Theme.TextFaint,
            AutoEllipsis = true,
            Text = string.Empty
        };

        _toggleBackdrop = new ToggleSwitch { Location = new Point(16, 150) };
        _toggleBackdrop.CheckedChanged += (_, _) => RaiseBackdrop();

        var lblToggle = new Label
        {
            Text = "启用自定义背景（贴在 FL 窗口后面）",
            Location = new Point(68, 151),
            AutoSize = true,
            ForeColor = Theme.Text
        };

        var separator = new Panel
        {
            Location = new Point(16, 186),
            Size = new Size(406, 1),
            BackColor = Theme.Border
        };

        _imagePanel = new Panel
        {
            Location = new Point(0, 196),
            Size = new Size(438, 132),
            BackColor = Theme.Surface
        };

        var lblFill = new Label { Text = "填充", Location = new Point(16, 8), AutoSize = true, ForeColor = Theme.TextDim };
        _cmbFill = CreateSelect(new Point(64, 4), new Size(150, 26));
        _cmbFill.AddItems("填满（裁剪）", "适配（留边）", "拉伸", "平铺", "居中");
        _cmbFill.SelectedIndexChanged += (_, _) => RaiseBackdrop();

        var lblStyle = new Label { Text = "模式", Location = new Point(232, 8), AutoSize = true, ForeColor = Theme.TextDim };
        _cmbStyle = CreateSelect(new Point(280, 4), new Size(142, 26));
        _cmbStyle.AddItems("正常", "玻璃模糊", "亚克力");
        _cmbStyle.SelectedIndexChanged += (_, _) =>
        {
            UpdateStrengthEnabled();
            RaiseBackdrop();
        };

        var lblBlur = new Label { Text = "模糊", Location = new Point(16, 50), AutoSize = true, ForeColor = Theme.TextDim };
        _sliderBlur = new ModernSlider { Location = new Point(64, 44), Size = new Size(270, 28), Minimum = 0, Maximum = 100 };
        _sliderBlur.ValueChanged += (_, _) => RaiseBackdrop();
        _lblBlurVal = new Label { Location = new Point(346, 50), AutoSize = true, ForeColor = Theme.TextDim };

        var lblDim = new Label { Text = "压暗", Location = new Point(16, 88), AutoSize = true, ForeColor = Theme.TextDim };
        _sliderDim = new ModernSlider { Location = new Point(64, 82), Size = new Size(270, 28), Minimum = 0, Maximum = 80 };
        _sliderDim.ValueChanged += (_, _) => RaiseBackdrop();
        _lblDimVal = new Label { Location = new Point(346, 88), AutoSize = true, ForeColor = Theme.TextDim };

        _imagePanel.Controls.AddRange(new Control[]
        {
            lblFill, _cmbFill, lblStyle, _cmbStyle,
            lblBlur, _sliderBlur, _lblBlurVal,
            lblDim, _sliderDim, _lblDimVal
        });

        _videoPanel = new Panel
        {
            Location = new Point(0, 196),
            Size = new Size(438, 132),
            BackColor = Theme.Surface,
            Visible = false
        };

        var lblVideoHint = new Label
        {
            Text = "循环播放 · 默认静音 · FL 隐藏时自动暂停",
            Location = new Point(16, 14),
            AutoSize = true,
            Font = Theme.Small,
            ForeColor = Theme.TextDim
        };

        _lblVideoFile = new Label
        {
            Location = new Point(16, 46),
            Size = new Size(406, 18),
            Font = Theme.Small,
            ForeColor = Theme.TextFaint,
            AutoEllipsis = true
        };

        var lblVideoNote = new Label
        {
            Text = "视频模式下，模糊 / 压暗 / 模式不生效",
            Location = new Point(16, 74),
            AutoSize = true,
            Font = Theme.Small,
            ForeColor = Theme.TextFaint
        };

        _videoPanel.Controls.AddRange(new Control[] { lblVideoHint, _lblVideoFile, lblVideoNote });

        card.Controls.AddRange(new Control[]
        {
            _preview, _btnPick, _btnFolder, _lblFile, _toggleBackdrop, lblToggle, separator,
            _imagePanel, _videoPanel
        });

        Controls.Add(card);
    }

    private void BuildOpacityCard()
    {
        var card = new Card
        {
            Location = new Point(16, 410),
            Size = new Size(438, 104),
            Title = "窗口透明度"
        };

        _sliderOpacity = new ModernSlider
        {
            Location = new Point(16, 58),
            Size = new Size(406, 30),
            Minimum = 30,
            Maximum = 100
        };
        _sliderOpacity.ValueChanged += (_, _) =>
        {
            UpdateValueLabels();
            if (_loading) return;
            _config.OpacityPercent = _sliderOpacity.Value;
            OpacityChanged?.Invoke();
        };

        _lblOpacityVal = new Label
        {
            Location = new Point(330, 16),
            Size = new Size(92, 34),
            Font = Theme.Big,
            ForeColor = Theme.Text,
            TextAlign = ContentAlignment.MiddleRight
        };

        card.Controls.AddRange(new Control[] { _sliderOpacity, _lblOpacityVal });
        Controls.Add(card);
    }

    private void BuildStatusCard()
    {
        var card = new Card
        {
            Location = new Point(16, 526),
            Size = new Size(438, 72),
            Title = "状态"
        };

        _lblStatusDot = new Label
        {
            Text = "●",
            Location = new Point(16, 40),
            AutoSize = true,
            ForeColor = Theme.Good
        };

        _lblStatus = new Label
        {
            Location = new Point(34, 41),
            Size = new Size(280, 20),
            ForeColor = Theme.TextDim,
            AutoEllipsis = true,
            Text = "正在查找 FL Studio…"
        };

        _btnLaunch = new FlatButton
        {
            Text = "启动 FL",
            Location = new Point(326, 36),
            Size = new Size(96, 28),
            Compact = true,
            IdleBack = Theme.SurfaceAlt,
            Visible = false
        };
        _btnLaunch.Click += (_, _) => LaunchRequested?.Invoke();

        card.Controls.AddRange(new Control[] { _lblStatusDot, _lblStatus, _btnLaunch });
        Controls.Add(card);
    }

    private void BuildBottomButtons()
    {
        var btnTray = new FlatButton
        {
            Text = "收进托盘",
            Location = new Point(16, 610),
            Size = new Size(140, 36)
        };
        btnTray.Click += (_, _) => HideRequested?.Invoke();

        var btnReset = new FlatButton
        {
            Text = "恢复不透明",
            Location = new Point(164, 610),
            Size = new Size(150, 36)
        };
        btnReset.Click += (_, _) => ResetRequested?.Invoke();

        var btnQuit = new FlatButton
        {
            Text = "退出并还原",
            Location = new Point(322, 610),
            Size = new Size(132, 36)
        };
        btnQuit.Click += (_, _) => QuitRequested?.Invoke();

        Controls.AddRange(new Control[] { btnTray, btnReset, btnQuit });
    }

    private static SelectBox CreateSelect(Point location, Size size)
    {
        return new SelectBox
        {
            Location = location,
            Size = size,
            BackColor = Theme.Surface
        };
    }

    // ─────────────────────────── 数据绑定 ───────────────────────────

    public void LoadFromConfig()
    {
        _loading = true;
        try
        {
            _toggleBackdrop.Checked = _config.BackdropEnabled;
            _cmbFill.SelectedIndex = (int)_config.FillMode;
            _cmbStyle.SelectedIndex = (int)_config.Style;
            _sliderBlur.Value = _config.BlurStrength;
            _sliderDim.Value = _config.Dim;
            _sliderOpacity.Value = _config.OpacityPercent;
        }
        finally
        {
            _loading = false;
        }

        UpdateValueLabels();
        UpdateStrengthEnabled();
        UpdateVideoState();
        UpdatePreview();
    }

    private void RaiseBackdrop()
    {
        if (_loading) return;

        _config.FillMode = (FillMode)Math.Max(0, _cmbFill.SelectedIndex);
        _config.Style = (BackdropStyle)Math.Max(0, _cmbStyle.SelectedIndex);
        _config.BlurStrength = _sliderBlur.Value;
        _config.Dim = _sliderDim.Value;
        _config.OpacityPercent = _sliderOpacity.Value;
        _config.BackdropEnabled = _toggleBackdrop.Checked;

        UpdateValueLabels();
        BackdropChanged?.Invoke();
    }

    private void UpdateValueLabels()
    {
        _lblBlurVal.Text = _sliderBlur.Value.ToString();
        _lblDimVal.Text = _sliderDim.Value + "%";
        _lblOpacityVal.Text = _sliderOpacity.Value + "%";
    }

    private void UpdateStrengthEnabled()
    {
        bool hasBlur = _cmbStyle.SelectedIndex > 0;
        _sliderBlur.Enabled = hasBlur;
        _lblBlurVal.Enabled = hasBlur;
        _lblBlurVal.ForeColor = hasBlur ? Theme.TextDim : Theme.TextFaint;
    }

    private void UpdateVideoState()
    {
        bool video = _config.UseVideo;

        _imagePanel.Visible = !video;
        _videoPanel.Visible = video;
        _cmbStyle.Enabled = !video;
        _sliderBlur.Enabled = !video && _cmbStyle.SelectedIndex > 0;
        _lblBlurVal.Enabled = _sliderBlur.Enabled;
        _sliderDim.Enabled = !video;
        _lblDimVal.Enabled = !video;

        _lblVideoFile.Text = string.IsNullOrWhiteSpace(_config.VideoPath)
            ? "还没有选择视频"
            : "[动态] " + Path.GetFileName(_config.VideoPath);

        _lblFile.Text = _config.UseVideo
            ? (string.IsNullOrWhiteSpace(_config.VideoPath) ? "未选择背景" : "[动态] " + Path.GetFileName(_config.VideoPath))
            : (string.IsNullOrWhiteSpace(_config.ImagePath) ? "未选择背景" : "[静态] " + Path.GetFileName(_config.ImagePath));
    }

    private void OpenPicker()
    {
        using var picker = new BackgroundPickerForm(_config, _config.ImagePath, _config.VideoPath, _config.UseVideo);
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        if (string.IsNullOrEmpty(picker.SelectedPath)) return;

        _config.UseVideo = picker.SelectedIsVideo;
        if (picker.SelectedIsVideo)
            _config.VideoPath = picker.SelectedPath;
        else
            _config.ImagePath = picker.SelectedPath;

        _loading = true;
        LoadFromConfig();
        _loading = false;

        BackdropChanged?.Invoke();
    }

    private void OpenBackgroundFolder()
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

    private void UpdatePreview()
    {
        _preview.Image = null;

        if (string.IsNullOrWhiteSpace(_config.ImagePath)) return;

        try
        {
            _preview.Image = ImageUtil.LoadCopy(_config.ImagePath);
        }
        catch
        {
            _preview.Image = null;
        }
    }

    // ─────────────────────────── 对外接口 ───────────────────────────

    public void SetStatus(string text, bool warn = false)
    {
        if (IsDisposed) return;
        _lblStatus.Text = text;
        _lblStatus.ForeColor = warn ? Theme.Warn : Theme.TextDim;
        _lblStatusDot.ForeColor = warn ? Theme.Warn : Theme.Good;

        // 没找到 FL 的时候才露出发射按钮，平时把位置让给状态文字
        bool notFound = text.StartsWith("未找到", StringComparison.Ordinal);
        _btnLaunch.Visible = notFound;
        _lblStatus.Width = notFound ? 286 : 386;
    }

    public void SetOpacitySilently(int percent)
    {
        _loading = true;
        _sliderOpacity.Value = Math.Clamp(percent, _sliderOpacity.Minimum, _sliderOpacity.Maximum);
        _loading = false;
        UpdateValueLabels();
    }

    public void SetBackdropSilently(bool enabled)
    {
        _loading = true;
        _toggleBackdrop.Checked = enabled;
        _loading = false;
    }

    public void ShowSettings()
    {
        ShowWithFade();
    }

    /// <summary>带淡入地显示（Opacity 要在 Show 之前设，否则第一帧已经是全不透明）。</summary>
    public void ShowWithFade()
    {
        int token = ++_fadeToken;

        Opacity = 0;
        Show();
        WindowState = FormWindowState.Normal;
        Activate();

        int targetTop = Top;
        Top = targetTop + 16;

        Motion.Run(240, t =>
        {
            if (token != _fadeToken) return;
            Opacity = t;
            Top = targetTop + 16 - (int)Math.Round(16 * t);
        }, () =>
        {
            if (token == _fadeToken) Top = targetTop;
        });
    }

    /// <summary>淡出后再藏到托盘。</summary>
    public void HideWithFade()
    {
        if (!Visible) return;

        int token = ++_fadeToken;
        float from = (float)Opacity;
        int top0 = Top;

        Motion.Run(150, t =>
        {
            if (token != _fadeToken) return;
            Opacity = from * (1f - t);
            Top = top0 + (int)Math.Round(10 * t);
        }, () =>
        {
            if (token != _fadeToken) return;
            Hide();
            Opacity = 1;
            Top = top0;
        });
    }

    // ─────────────────────────── 热键 / 关闭 ───────────────────────────

    private void BindHotkeys()
    {
        if (!IsHandleCreated) return;
        Native.RegisterHotKey(Handle, HotkeyOpacityUp, Native.MOD_CONTROL | Native.MOD_ALT, 0x26);   // ↑
        Native.RegisterHotKey(Handle, HotkeyOpacityDown, Native.MOD_CONTROL | Native.MOD_ALT, 0x28); // ↓
        Native.RegisterHotKey(Handle, HotkeyToggleBackdrop, Native.MOD_CONTROL | Native.MOD_ALT, 0x42); // B
        Native.RegisterHotKey(Handle, HotkeyShow, Native.MOD_CONTROL | Native.MOD_ALT, 0x53);          // S
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        Native.UnregisterHotKey(Handle, HotkeyOpacityUp);
        Native.UnregisterHotKey(Handle, HotkeyOpacityDown);
        Native.UnregisterHotKey(Handle, HotkeyToggleBackdrop);
        Native.UnregisterHotKey(Handle, HotkeyShow);
        base.OnHandleDestroyed(e);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Native.WM_HOTKEY)
        {
            switch (m.WParam.ToInt32())
            {
                case HotkeyOpacityUp:
                    OpacityStepRequested?.Invoke(+5);
                    break;
                case HotkeyOpacityDown:
                    OpacityStepRequested?.Invoke(-5);
                    break;
                case HotkeyToggleBackdrop:
                    ToggleBackdropRequested?.Invoke();
                    break;
                case HotkeyShow:
                    ShowSettings();
                    break;
            }
        }

        base.WndProc(ref m);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            HideRequested?.Invoke();
            return;
        }

        base.OnFormClosing(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        // 顶部一条细分割线，让标题栏和内容分开
        using var pen = new Pen(Theme.Border);
        e.Graphics.DrawLine(pen, 16, 52, Width - 16, 52);
    }
}
