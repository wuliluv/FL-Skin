using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace FLSkin;

/// <summary>定位 FL Studio 主窗口，并给它加减透明度。</summary>
internal static class FlStudio
{
    private const string MainWindowClass = "TFruityLoopsMainForm";

    /// <summary>把进程 ID 翻译成可读的进程名（FL64.exe 之类）。</summary>
    public static string GetProcessName(uint pid)
    {
        try
        {
            using var process = Process.GetProcessById((int)pid);
            return process.ProcessName + ".exe";
        }
        catch
        {
            return "FL Studio";
        }
    }

    /// <summary>找所有 FL Studio 主窗口；找不到返回空列表。</summary>
    public static List<IntPtr> FindMainWindows()
    {
        var list = new List<IntPtr>();

        Native.EnumWindows((hWnd, _) =>
        {
            var cls = new StringBuilder(256);
            Native.GetClassName(hWnd, cls, cls.Capacity);
            if (!cls.ToString().StartsWith(MainWindowClass, StringComparison.Ordinal))
                return true;

            if (Native.IsWindowVisible(hWnd))
                list.Add(hWnd);

            return true;
        }, IntPtr.Zero);

        return list;
    }

    public static bool IsMainWindow(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero) return false;

        var cls = new StringBuilder(256);
        Native.GetClassName(hWnd, cls, cls.Capacity);
        return cls.ToString().StartsWith(MainWindowClass, StringComparison.Ordinal);
    }

    /// <summary>把窗口设成半透明（percent: 30..100）。</summary>
    public static void ApplyAlpha(IntPtr hWnd, int percent)
    {
        if (hWnd == IntPtr.Zero) return;

        byte alpha = PercentToAlpha(percent);
        long ex = Native.ExStyle(hWnd);
        bool changed = false;

        if ((ex & Native.WS_EX_LAYERED) == 0)
        {
            Native.SetExStyle(hWnd, ex | Native.WS_EX_LAYERED);
            changed = true;
        }

        if (!Native.SetLayeredWindowAttributes(hWnd, 0, alpha, Native.LWA_ALPHA))
            return;

        if (changed)
            Repaint(hWnd);
    }

    /// <summary>恢复完全不透明，并去掉分层窗口标记。</summary>
    public static void ClearAlpha(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero || !Native.IsWindow(hWnd)) return;

        Native.SetLayeredWindowAttributes(hWnd, 0, 255, Native.LWA_ALPHA);

        long ex = Native.ExStyle(hWnd);
        if ((ex & Native.WS_EX_LAYERED) != 0)
            Native.SetExStyle(hWnd, ex & ~(long)Native.WS_EX_LAYERED);

        Repaint(hWnd);
    }

    private static void Repaint(IntPtr hWnd)
    {
        Native.SetWindowPos(hWnd, IntPtr.Zero, 0, 0, 0, 0,
            Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOZORDER |
            Native.SWP_NOACTIVATE | Native.SWP_FRAMECHANGED);
    }

    public static byte PercentToAlpha(int percent)
    {
        int clamped = Math.Clamp(percent, 30, 100);
        return (byte)Math.Round(clamped * 255.0 / 100.0);
    }

    /// <summary>从注册表读 FL Studio 安装位置，用于「启动 FL Studio」按钮。</summary>
    public static string TryGetInstallPath()
    {
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
                using var key = root.OpenSubKey(@"SOFTWARE\Image-Line\Shared\Paths");
                if (key?.GetValue("FL Studio") is string path && !string.IsNullOrWhiteSpace(path))
                    return path;
            }
            catch
            {
                // 没装 / 无权限，继续下一个
            }
        }

        return null;
    }

    public static bool TryLaunch()
    {
        string path = TryGetInstallPath();
        if (string.IsNullOrEmpty(path)) return false;

        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            return true;
        }
        catch
        {
            return false;
        }
    }
}
