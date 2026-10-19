using System;
using System.Runtime.InteropServices;

namespace FLSkin;

/// <summary>
/// 用 SetWinEventHook 的「进程外」钩子实时感知目标窗口的移动 / 缩放 / 激活。
/// 90ms 轮询在拖动时最多会落后上百像素，这套事件是实时的，基本看不出延迟。
/// </summary>
internal sealed class WinEventWatcher : IDisposable
{
    private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    private const uint EVENT_SYSTEM_MOVESIZESTART = 0x000A;
    private const uint EVENT_SYSTEM_MOVESIZEEND = 0x000B;
    private const uint EVENT_OBJECT_CREATE = 0x8000;
    private const uint EVENT_OBJECT_SHOW = 0x8002;
    private const uint EVENT_OBJECT_LOCATIONCHANGE = 0x800B;

    private const int OBJID_WINDOW = 0;
    private const uint WINEVENT_OUTOFCONTEXT = 0x0000;

    private delegate void WinEventDelegate(IntPtr hook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint thread, uint time);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr module,
        WinEventDelegate callback, uint processId, uint threadId, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWinEvent(IntPtr hook);

    [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
    private static extern uint TimeBeginPeriod(uint ms);

    [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
    private static extern uint TimeEndPeriod(uint ms);

    private readonly WinEventDelegate _callback; // 必须保活，否则回调会被 GC 回收
    private readonly IntPtr _locationHook;
    private readonly IntPtr _moveSizeHook;
    private readonly IntPtr _foregroundHook;
    private readonly IntPtr _objectHook;

    private bool _highResolutionTimer;

    public event Action<IntPtr> LocationChanged;
    public event Action<IntPtr> MoveSizeStarted;
    public event Action<IntPtr> MoveSizeEnded;
    public event Action<IntPtr> ForegroundChanged;
    public event Action<IntPtr> ObjectShown;

    public bool IsActive => _locationHook != IntPtr.Zero || _moveSizeHook != IntPtr.Zero;

    public WinEventWatcher()
    {
        _callback = OnWinEvent;

        _locationHook = SetWinEventHook(EVENT_OBJECT_LOCATIONCHANGE, EVENT_OBJECT_LOCATIONCHANGE,
            IntPtr.Zero, _callback, 0, 0, WINEVENT_OUTOFCONTEXT);

        _moveSizeHook = SetWinEventHook(EVENT_SYSTEM_MOVESIZESTART, EVENT_SYSTEM_MOVESIZEEND,
            IntPtr.Zero, _callback, 0, 0, WINEVENT_OUTOFCONTEXT);

        _foregroundHook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero, _callback, 0, 0, WINEVENT_OUTOFCONTEXT);

        // 新建 / 显示对象：菜单、二级菜单、对话框一出现就能被套上透明度
        _objectHook = SetWinEventHook(EVENT_OBJECT_CREATE, EVENT_OBJECT_SHOW,
            IntPtr.Zero, _callback, 0, 0, WINEVENT_OUTOFCONTEXT);
    }

    private void OnWinEvent(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild,
        uint thread, uint time)
    {
        if (idObject != OBJID_WINDOW) return;

        switch (eventType)
        {
            case EVENT_OBJECT_CREATE:
            case EVENT_OBJECT_SHOW:
                ObjectShown?.Invoke(hwnd);
                break;
            case EVENT_OBJECT_LOCATIONCHANGE:
                LocationChanged?.Invoke(hwnd);
                break;
            case EVENT_SYSTEM_MOVESIZESTART:
                BeginHighResolutionTimer();
                MoveSizeStarted?.Invoke(hwnd);
                break;
            case EVENT_SYSTEM_MOVESIZEEND:
                MoveSizeEnded?.Invoke(hwnd);
                EndHighResolutionTimer();
                break;
            case EVENT_SYSTEM_FOREGROUND:
                ForegroundChanged?.Invoke(hwnd);
                break;
        }
    }

    /// <summary>拖动期间把系统定时器精度提到 1ms，让跟手的轮询更快。</summary>
    public void BeginHighResolutionTimer()
    {
        if (_highResolutionTimer) return;
        TimeBeginPeriod(1);
        _highResolutionTimer = true;
    }

    public void EndHighResolutionTimer()
    {
        if (!_highResolutionTimer) return;
        TimeEndPeriod(1);
        _highResolutionTimer = false;
    }

    public void Dispose()
    {
        if (_locationHook != IntPtr.Zero) UnhookWinEvent(_locationHook);
        if (_moveSizeHook != IntPtr.Zero) UnhookWinEvent(_moveSizeHook);
        if (_foregroundHook != IntPtr.Zero) UnhookWinEvent(_foregroundHook);
        if (_objectHook != IntPtr.Zero) UnhookWinEvent(_objectHook);
        EndHighResolutionTimer();
    }
}
