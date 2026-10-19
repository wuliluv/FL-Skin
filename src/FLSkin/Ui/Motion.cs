using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace FLSkin.Ui;

/// <summary>
/// 一个很小的补间动画驱动：只有真的在动的时候才开定时器，停了自己关。
/// 所有控件共用这一条时间轴，避免每个控件各开一个 Timer。
/// </summary>
internal static class Motion
{
    private sealed class Entry
    {
        public Action<float> Frame;
        public Action Done;
        public int Duration;
        public long Start;
        public Func<float, float> Ease;
    }

    private static readonly List<Entry> Running = new List<Entry>();
    private static readonly System.Windows.Forms.Timer Timer = new System.Windows.Forms.Timer { Interval = 15 };
    private static bool _hooked;

    public static bool Busy => Running.Count > 0;

    /// <summary>durationMs 毫秒内把 0→1（已缓动）喂给 frame。</summary>
    public static void Run(int durationMs, Action<float> frame, Action done = null, Func<float, float> ease = null)
    {
        if (frame == null) return;

        Running.Add(new Entry
        {
            Frame = frame,
            Done = done,
            Duration = Math.Max(1, durationMs),
            Start = Environment.TickCount64,
            Ease = ease ?? EaseOutCubic
        });

        if (!_hooked)
        {
            Timer.Tick += OnTick;
            _hooked = true;
        }

        if (!Timer.Enabled) Timer.Start();
    }

    private static void OnTick(object sender, EventArgs e)
    {
        long now = Environment.TickCount64;

        for (int i = Running.Count - 1; i >= 0; i--)
        {
            Entry entry = Running[i];
            float raw = Math.Min(1f, (now - entry.Start) / (float)entry.Duration);
            float eased = entry.Ease(raw);

            try
            {
                entry.Frame(eased);
            }
            catch
            {
                // 控件已经销毁之类的，直接丢掉这一条
                Running.RemoveAt(i);
                continue;
            }

            if (raw >= 1f)
            {
                Running.RemoveAt(i);
                try { entry.Done?.Invoke(); } catch { }
            }
        }

        if (Running.Count == 0) Timer.Stop();
    }

    /// <summary>带一点回弹的缓动，用在开关圆点这种“有实体感”的地方。</summary>
    public static float EaseOutBack(float t)
    {
        const float c1 = 1.42f;
        const float c3 = c1 + 1f;
        float inv = t - 1f;
        return 1f + c3 * inv * inv * inv + c1 * inv * inv;
    }

    public static float EaseOutCubic(float t)
    {
        float inv = 1f - t;
        return 1f - inv * inv * inv;
    }

    public static Color Lerp(Color from, Color to, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return Color.FromArgb(
            (int)Math.Round(from.A + (to.A - from.A) * t),
            (int)Math.Round(from.R + (to.R - from.R) * t),
            (int)Math.Round(from.G + (to.G - from.G) * t),
            (int)Math.Round(from.B + (to.B - from.B) * t));
    }
}
