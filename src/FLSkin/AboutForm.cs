using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using FLSkin.Ui;

namespace FLSkin;

/// <summary>关于窗口：说明 + 依赖致谢 + 协议 / License / README 入口。</summary>
internal sealed class AboutForm : Form
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    private const int WM_NCLBUTTONDOWN = 0x00A1;
    private const int HTCAPTION = 2;

    private const string EulaText = @"软件用户许可协议

欢迎使用本软件。安装、复制或使用本软件，即表示您已阅读并同意本协议的全部条款。

一、软件性质
1. 本软件是个人开发的免费开源工具，用于给 FL Studio 添加自定义背景与窗口透明度，与 Image-Line 官方无关。
2. 本软件源代码以 MIT 许可证发布（见同目录 LICENSE 文件），版权归作者所有。
3. 本软件不修改 FL Studio 的任何文件，也不向其进程注入 DLL；只在运行时调整窗口属性。
4. 本软件在开发过程中使用了 AI 辅助编码，但运行时不调用任何第三方 AI 服务，不联网、不上传任何数据。

二、使用权限
1. 您可以免费、非独占地在任意数量的设备上安装和使用本软件。
2. 依据 MIT 许可证，您可以复制、修改、合并、发布、再分发本软件及其衍生作品，但必须保留原始版权声明与许可声明。
3. 再分发时请一并附上 LICENSE 文件。

三、禁止行为
1. 不得移除或篡改版权声明、许可声明后宣称是原创作品。
2. 不得利用本软件从事违反法律法规、侵犯他人权益或危害网络安全的行为。

四、免责声明
1. 本软件按「现状」提供，不附带任何明示或暗示的担保，包括但不限于稳定性、可用性、安全性、适用性。
2. 因使用或无法使用本软件而产生的任何直接或间接后果，由使用者自行承担。
3. 软件功能、版本、可用性可能随时更新或调整，恕不另行通知。

五、其他
1. 本协议适用中华人民共和国法律。
2. 本协议最终解释权归软件作者所有。";

    public AboutForm()
    {
        Text = "关于";
        ClientSize = new Size(660, 640);
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Theme.Window;
        Font = Theme.Body;
        DoubleBuffered = true;
        Icon = AppIcon.Create();
        KeyPreview = true;
        Opacity = 0;

        BuildUi();

        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape) Close();
        };
    }

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

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);

        int targetTop = Top;
        Top = targetTop + 14;
        Motion.Run(200, t =>
        {
            Opacity = t;
            Top = targetTop + 14 - (int)Math.Round(14 * t);
        }, () => Top = targetTop);
    }

    private void BuildUi()
    {
        var lblTitle = new Label
        {
            Text = "关于",
            Font = new Font("Microsoft YaHei UI", 11.5f, FontStyle.Bold),
            ForeColor = Theme.Text,
            AutoSize = true,
            Location = new Point(20, 16),
            BackColor = Color.Transparent
        };

        var btnCloseTop = new FlatButton
        {
            Text = "✕",
            Location = new Point(614, 14),
            Size = new Size(32, 24),
            Compact = true,
            IdleBack = Theme.Window,
            HoverBackColor = Color.FromArgb(196, 43, 28),
            DrawBorder = false,
            Font = new Font("Segoe UI", 9f)
        };
        btnCloseTop.Click += (_, _) => Close();

        var doc = new DocPanel(640) { Location = new Point(10, 56), Size = new Size(640, 520) };

        doc.Heading("FLSkin");
        doc.Paragraph("给 FL Studio 加自定义背景（图片 / MP4）和窗口透明度的外部小工具。");

        doc.Gap(8);
        doc.SubHeading("【关于本软件】");
        doc.Paragraph("不改 FL 的任何文件、不注入 DLL，靠 Windows 自己的窗口合成能力实现：");
        doc.Paragraph("· 把 FL 主窗口设为分层窗口（WS_EX_LAYERED），按百分比与下层画面混合；");
        doc.Paragraph("· 在 FL 窗口正下方贴一个背景窗口，FL 变透明后就把它透出来；");
        doc.Paragraph("· 监听窗口移动 / 缩放 / 激活事件，背景层毫秒级跟随，多开也各管各的。");
        doc.Gap(4);
        doc.Paragraph("技术栈：C# / .NET 9、WinForms（界面全部自绘）、WPF MediaElement（MP4 解码）、Win32（SetLayeredWindowAttributes / SetWinEventHook / DWM）。");

        doc.Gap(10);
        doc.SubHeading("感谢以下项目对本软件开发过程中的支持");
        doc.Paragraph("· .NET 9（MIT）");
        doc.Link("https://github.com/dotnet/runtime", () => OpenUrl("https://github.com/dotnet/runtime"));
        doc.Paragraph("· Windows Forms / WPF（MIT，随 .NET 提供）");
        doc.Paragraph("· DeepSeek-V4.1-Flash（开发阶段用于代码辅助编写）");

        doc.Gap(10);
        doc.Paragraph("本软件为免费开源软件，源代码以 MIT 许可证发布。");

        doc.Gap(10);
        doc.SubHeading("协议与文档");
        doc.Link("软件用户许可协议（点击查看）", ShowEula);
        doc.Link("License（MIT License，点击查看）", ShowLicense);
        doc.Link("README（点击查看）", ShowReadme);

        doc.Gap(14);
        doc.Paragraph("Developer: wuli　·　版本 " + GetVersion(), Theme.TextFaint);
        doc.AutoScrollPosition = new Point(0, 0);

        var btnClose = new FlatButton
        {
            Text = "关闭",
            Location = new Point(530, 590),
            Size = new Size(110, 34)
        };
        btnClose.Click += (_, _) => Close();

        Controls.AddRange(new Control[] { lblTitle, btnCloseTop, doc, btnClose });

        MouseDown += DragWindow;
        lblTitle.MouseDown += DragWindow;
    }

    private static string GetVersion()
    {
        try
        {
            Version version = Assembly.GetExecutingAssembly().GetName().Version;
            return version == null ? "1.0.0" : $"{version.Major}.{version.Minor}.{version.Build}";
        }
        catch
        {
            return "1.0.0";
        }
    }

    private void ShowEula()
    {
        using var viewer = new TextViewerForm("软件用户许可协议", EulaText);
        viewer.ShowDialog(this);
    }

    private void ShowLicense()
    {
        string text = ReadFile("LICENSE");
        if (text == null)
        {
            MessageBox.Show(this, "没有找到 LICENSE 文件，它应该和 FLSkin.exe 放在同一个文件夹里。",
                "License", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var viewer = new TextViewerForm("License（MIT）", text);
        viewer.ShowDialog(this);
    }

    private void ShowReadme()
    {
        string text = ReadFile("README.md");
        if (text == null)
        {
            MessageBox.Show(this, "没有找到 README.md，它应该和 FLSkin.exe 放在同一个文件夹里。",
                "README", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var viewer = new TextViewerForm("README", text);
        viewer.ShowDialog(this);
    }

    private static string ReadFile(string name)
    {
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, name);
            return File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : null;
        }
        catch
        {
            return null;
        }
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // 没浏览器就算了
        }
    }

    private void DragWindow(object sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        ReleaseCapture();
        SendMessage(Handle, WM_NCLBUTTONDOWN, HTCAPTION, IntPtr.Zero);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(Theme.Border);
        e.Graphics.DrawLine(pen, 16, 52, Width - 16, 52);
    }
}
