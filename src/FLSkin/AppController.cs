using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Linq;
using System.Windows.Forms;

namespace FLSkin;

internal sealed class AppController : ApplicationContext
{
    private readonly AppConfig _config;
    private readonly SettingsForm _form;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly System.Windows.Forms.Timer _fastTimer;
    private readonly System.Windows.Forms.Timer _rebuildTimer;
    private readonly System.Windows.Forms.Timer _saveTimer;
    private readonly WinEventWatcher _watcher;
    private readonly NotifyIcon _tray;

    // 可能同时开着多个 FL Studio 实例：每个主窗口一份透明度 + 一份背景层
    private readonly List<IntPtr> _flWindows = new List<IntPtr>();
    private readonly Dictionary<IntPtr, BackdropForm> _backdrops = new Dictionary<IntPtr, BackdropForm>();
    private readonly Dictionary<IntPtr, int> _appliedAlpha = new Dictionary<IntPtr, int>();

    private int _tick;
    private bool _exiting;

    public AppController()
    {
        _config = AppConfig.Load();
        _config.EnsureLibrary();

        _form = new SettingsForm(_config);
        _form.Icon = AppIcon.Create();
        _form.BackdropChanged += OnBackdropChanged;
        _form.OpacityChanged += OnOpacityChanged;
        _form.ResetRequested += ResetFlOpacity;
        _form.HideRequested += HideSettings;
        _form.QuitRequested += Exit;
        _form.LaunchRequested += LaunchFl;
        _form.OpacityStepRequested += StepOpacity;
        _form.ToggleBackdropRequested += ToggleBackdrop;

        _tray = BuildTray();

        _watcher = new WinEventWatcher();

        _watcher.LocationChanged += hWnd =>
        {
            if (_backdrops.TryGetValue(hWnd, out BackdropForm backdrop))
                backdrop.Follow(hWnd);
        };

        _watcher.MoveSizeStarted += hWnd =>
        {
            if (!_backdrops.ContainsKey(hWnd)) return;
            _fastTimer.Start();       // 拖动 / 缩放期间改 10ms 高频跟手
            FollowAll();
        };

        _watcher.MoveSizeEnded += hWnd =>
        {
            if (!_backdrops.ContainsKey(hWnd)) return;
            _fastTimer.Stop();
            FollowAll();
        };

        _watcher.ForegroundChanged += hWnd =>
        {
            if (_backdrops.ContainsKey(hWnd)) FollowAll();   // FL 被激活时立刻重排 z 序
        };

        // 又开了一个 FL 实例时立刻接上
        _watcher.ObjectShown += hWnd =>
        {
            if (_exiting || !FlStudio.IsMainWindow(hWnd)) return;
            RefreshWindows();
            ApplyAllOpacity();
            FollowAll();
        };

        _fastTimer = new System.Windows.Forms.Timer { Interval = 10 };
        _fastTimer.Tick += (_, _) => FollowAll();

        _timer = new System.Windows.Forms.Timer { Interval = 60 };
        _timer.Tick += (_, _) => Tick();

        // 背景重算（模糊 / 颗粒）比较贵：拖动时先不动，停手 150ms 后再算一次，
        // 这样拖滑块的过程是零卡顿的。
        _rebuildTimer = new System.Windows.Forms.Timer { Interval = 150 };
        _rebuildTimer.Tick += (_, _) =>
        {
            _rebuildTimer.Stop();
            ApplyBackdropToAll();
        };

        // 写盘也延后，避免每移动一像素就写一次 config.json
        _saveTimer = new System.Windows.Forms.Timer { Interval = 400 };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            _config.Save();
        };

        RefreshWindows();
        ApplyAllOpacity();
        _form.ShowWithFade();

        // 新用户第一次打开：自动弹一次 README
        _form.BeginInvoke(new Action(ShowFirstRunReadme));
        _timer.Start();
    }

    /// <summary>首次运行给用户看一遍 README，看完就不再来。</summary>
    private void ShowFirstRunReadme()
    {
        if (_exiting || _config.ReadmeShown) return;

        _config.ReadmeShown = true;
        _config.Save();

        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "README.md");
            if (!File.Exists(path)) return;

            using var viewer = new TextViewerForm("README · 使用说明", File.ReadAllText(path, Encoding.UTF8));
            viewer.ShowDialog(_form);
        }
        catch
        {
            // 读不到 README 就算了，不影响使用
        }
    }

    private NotifyIcon BuildTray()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("显示设置…", null, (_, _) => _form.ShowSettings());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("背景开关", null, (_, _) => ToggleBackdrop());
        menu.Items.Add("透明度 +5%", null, (_, _) => StepOpacity(+5));
        menu.Items.Add("透明度 -5%", null, (_, _) => StepOpacity(-5));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("恢复 FL 不透明", null, (_, _) => ResetFlOpacity());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出并还原", null, (_, _) => Exit());

        var tray = new NotifyIcon
        {
            Icon = AppIcon.Create(),
            Text = "FLSkin · FL Studio 背景与透明度",
            Visible = true,
            ContextMenuStrip = menu
        };
        tray.DoubleClick += (_, _) => _form.ShowSettings();
        return tray;
    }

    // ─────────────────────────── 窗口发现 ───────────────────────────

    private void RefreshWindows()
    {
        List<IntPtr> found = FlStudio.FindMainWindows();

        bool changed = found.Count != _flWindows.Count || found.Exists(w => !_flWindows.Contains(w));
        if (!changed) return;

        foreach (var gone in _flWindows.Where(w => !found.Contains(w)).ToList())
        {
            _flWindows.Remove(gone);
            _appliedAlpha.Remove(gone);
            if (_backdrops.TryGetValue(gone, out BackdropForm backdrop))
            {
                _backdrops.Remove(gone);
                backdrop.Dispose();
            }
        }

        foreach (var w in found)
        {
            if (_flWindows.Contains(w)) continue;
            _flWindows.Add(w);
            _backdrops[w] = CreateBackdrop();
        }
    }

    private BackdropForm CreateBackdrop()
    {
        var backdrop = new BackdropForm();
        ApplyBackdropConfig(backdrop);
        return backdrop;
    }

    private void ApplyBackdropConfig(BackdropForm backdrop)
    {
        backdrop.FillMode = _config.FillMode;
        backdrop.Style = _config.Style;
        backdrop.BlurStrength = _config.BlurStrength;
        backdrop.Dim = _config.Dim;
        backdrop.SetImage(_config.ImagePath);
        backdrop.SetVideo(_config.UseVideo ? _config.VideoPath : null, _config.VideoMuted);
        backdrop.InvalidateCache();
    }

    // ─────────────────────────── 主循环 ───────────────────────────

    private void Tick()
    {
        if (_exiting) return;

        try
        {
            _tick++;

            if (_tick % 15 == 0)   // 约 0.9s 兜底扫一次，主要靠 ObjectShown 事件
                RefreshWindows();

            foreach (var w in _flWindows.ToList())
            {
                if (!Native.IsWindow(w))
                {
                    RefreshWindows();
                    continue;
                }

                if (!_backdrops.ContainsKey(w))
                    _backdrops[w] = CreateBackdrop();

                ApplyOpacity(w);
            }

            if (_flWindows.Count == 0)
            {
                HideAllBackdrops();
                if (_tick % 11 == 0)
                    _form.SetStatus("未找到 FL Studio。请先启动 FL Studio，本程序会自动接上。", warn: true);
                return;
            }

            FollowAll();

            if (_tick % 11 == 0)
                UpdateStatus();
        }
        catch
        {
            // 任何异常都不该让工具崩掉：下一拍继续
        }
    }

    private void UpdateStatus()
    {
        string state = _config.OpacityPercent >= 100 ? "不透明" : "不透明度 " + _config.OpacityPercent + "%";
        string instances = _flWindows.Count > 1 ? "（" + _flWindows.Count + " 个实例）" : "";
        string exe = FlStudio.GetProcessName((uint)GetPid(_flWindows[0]));

        bool placementBroken = _config.BackdropEnabled && _backdrops.Values.Any(b => b.PlacementFailed);

        if (placementBroken)
        {
            _form.SetStatus("背景没能排到 FL 窗口正下方 —— FL 多半是以管理员身份运行的，" +
                            "请右键 FLSkin 选择「以管理员身份运行」。", warn: true);
            return;
        }

        string kind = _config.UseVideo
            ? (_backdrops.Values.Any(b => b.HasVideo) ? " · MP4 动态背景" : " · 视频未能播放，已退回静态图")
            : (_config.BackdropEnabled ? " · 自定义背景已开启" : "");

        _form.SetStatus("已连接 FL Studio" + instances + "（" + exe + "）· " + state + kind);
    }

    private static int GetPid(IntPtr hWnd)
    {
        uint pid;
        Native.GetWindowThreadProcessId(hWnd, out pid);
        return (int)pid;
    }

    // ─────────────────────────── 透明度 ───────────────────────────

    private void ApplyOpacity(IntPtr hWnd)
    {
        if (_appliedAlpha.TryGetValue(hWnd, out int current) && current == _config.OpacityPercent)
            return;

        if (_config.OpacityPercent >= 100)
            FlStudio.ClearAlpha(hWnd);
        else
            FlStudio.ApplyAlpha(hWnd, _config.OpacityPercent);

        _appliedAlpha[hWnd] = _config.OpacityPercent;
    }

    private void ApplyAllOpacity()
    {
        foreach (var w in _flWindows)
        {
            if (Native.IsWindow(w)) ApplyOpacity(w);
        }
    }

    // ─────────────────────────── 背景层 ───────────────────────────

    private void FollowAll()
    {
        if (_exiting) return;

        if (!_config.BackdropEnabled)
        {
            HideAllBackdrops();
            return;
        }

        foreach (KeyValuePair<IntPtr, BackdropForm> pair in _backdrops)
        {
            if (!Native.IsWindow(pair.Key)) continue;
            pair.Value.Follow(pair.Key);
        }
    }

    private void HideAllBackdrops()
    {
        foreach (var backdrop in _backdrops.Values)
            backdrop.HideBackdrop();
    }

    // ─────────────────────────── 事件 ───────────────────────────

    /// <summary>不透明度：立刻套到窗口上，不等定时器（拖动时也跟手）。</summary>
    private void OnOpacityChanged()
    {
        ScheduleSave();
        ApplyAllOpacity();
    }

    /// <summary>背景参数变了：限流重算 + 延后写盘。</summary>
    private void OnBackdropChanged()
    {
        ScheduleSave();
        _rebuildTimer.Stop();
        _rebuildTimer.Start();
    }

    private void ScheduleSave()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void ApplyBackdropToAll()
    {
        foreach (var backdrop in _backdrops.Values)
            ApplyBackdropConfig(backdrop);
    }

    private void StepOpacity(int delta)
    {
        int next = Math.Clamp(_config.OpacityPercent + delta, 30, 100);
        _config.OpacityPercent = next;
        _form.SetOpacitySilently(next);
        _appliedAlpha.Clear();
        _config.Save();
    }

    private void ToggleBackdrop()
    {
        _config.BackdropEnabled = !_config.BackdropEnabled;
        _form.SetBackdropSilently(_config.BackdropEnabled);
        if (!_config.BackdropEnabled) HideAllBackdrops();
        _config.Save();
    }

    private void ResetFlOpacity()
    {
        _config.OpacityPercent = 100;
        _form.SetOpacitySilently(100);
        foreach (var w in _flWindows)
        {
            if (Native.IsWindow(w)) FlStudio.ClearAlpha(w);
            _appliedAlpha[w] = 100;
        }
        _config.Save();
    }

    private void HideSettings()
    {
        _form.HideWithFade();
        _tray.ShowBalloonTip(1500, "FLSkin 仍在运行",
            "右键托盘图标可以随时调整透明度或背景。", ToolTipIcon.Info);
    }

    private void LaunchFl()
    {
        if (!FlStudio.TryLaunch())
            MessageBox.Show("没有找到 FL Studio 的安装路径，请手动启动它。", "FLSkin",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void Exit()
    {
        _exiting = true;
        _timer.Stop();
        _fastTimer.Stop();
        _rebuildTimer.Stop();
        _saveTimer.Stop();
        _watcher.Dispose();

        try
        {
            foreach (var w in _flWindows)
            {
                if (Native.IsWindow(w)) FlStudio.ClearAlpha(w);
            }
        }
        catch
        {
            // 退出时别再抛
        }

        _config.Save();
        _tray.Visible = false;
        _tray.Dispose();

        foreach (var backdrop in _backdrops.Values)
            backdrop.Dispose();
        _backdrops.Clear();

        _form.Dispose();
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer?.Dispose();
            _fastTimer?.Dispose();
            _rebuildTimer?.Dispose();
            _saveTimer?.Dispose();
            _watcher?.Dispose();
            _tray?.Dispose();
        }
        base.Dispose(disposing);
    }
}
