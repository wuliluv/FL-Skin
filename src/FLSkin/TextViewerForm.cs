using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using FLSkin.Ui;

namespace FLSkin;

/// <summary>纯文本 / Markdown 查看窗口（协议、License、README 都用它）。</summary>
internal sealed class TextViewerForm : Form
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    private const int WM_NCLBUTTONDOWN = 0x00A1;
    private const int HTCAPTION = 2;

    private DocPanel _doc;

    public TextViewerForm(string title, string text)
    {
        Text = title;
        ClientSize = new Size(720, 620);
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Theme.Window;
        Font = Theme.Body;
        DoubleBuffered = true;
        Icon = AppIcon.Create();
        KeyPreview = true;
        Opacity = 0;

        BuildUi(title, text);

        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape)
            {
                DialogResult = DialogResult.Cancel;
                Close();
            }
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
        }, () =>
        {
            Top = targetTop;
            // 布局完成后强制回到顶部（添加控件时 AutoScroll 会自己滚到底部）
            if (_doc != null) _doc.AutoScrollPosition = new Point(0, 0);
        });
    }

    private void BuildUi(string title, string text)
    {
        var lblTitle = new Label
        {
            Text = title,
            Font = new Font("Microsoft YaHei UI", 11.5f, FontStyle.Bold),
            ForeColor = Theme.Text,
            AutoSize = true,
            Location = new Point(20, 16),
            BackColor = Color.Transparent
        };

        var btnCloseTop = new FlatButton
        {
            Text = "✕",
            Location = new Point(674, 14),
            Size = new Size(32, 24),
            Compact = true,
            IdleBack = Theme.Window,
            HoverBackColor = Color.FromArgb(196, 43, 28),
            DrawBorder = false,
            Font = new Font("Segoe UI", 9f)
        };
        btnCloseTop.Click += (_, _) => Close();

        _doc = new DocPanel(700) { Location = new Point(10, 56), Size = new Size(700, 500) };
        Markdown.Render(_doc, text, 700);
        _doc.AutoScrollPosition = new Point(0, 0);

        var btnClose = new FlatButton
        {
            Text = "关闭",
            Location = new Point(590, 570),
            Size = new Size(110, 34)
        };
        btnClose.Click += (_, _) => Close();

        Controls.AddRange(new Control[] { lblTitle, btnCloseTop, _doc, btnClose });

        MouseDown += DragWindow;
        lblTitle.MouseDown += DragWindow;
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

/// <summary>极简 Markdown：只认标题、列表、代码块和链接。</summary>
internal static class Markdown
{
    private static readonly Regex ImagePattern = new Regex(@"!\[([^\]]*)\]\([^)]*\)", RegexOptions.Compiled);
    private static readonly Regex LinkPattern = new Regex(@"\[([^\]]+)\]\(([^)]+)\)", RegexOptions.Compiled);

    public static void Render(DocPanel doc, string text, int width)
    {
        bool inCode = false;
        var codeBlock = new System.Text.StringBuilder();

        foreach (string rawLine in text.Replace("\r\n", "\n").Split('\n'))
        {
            string line = rawLine.TrimEnd();

            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                if (inCode)
                {
                    doc.Code(codeBlock.ToString().TrimEnd());
                    codeBlock.Clear();
                    inCode = false;
                }
                else
                {
                    inCode = true;
                }
                continue;
            }

            if (inCode)
            {
                codeBlock.AppendLine(rawLine);
                continue;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                doc.Gap(6);
                continue;
            }

            // 去掉 Markdown 记号，让纯文本窗口读起来干净
            string stripped = ImagePattern.Replace(line, "（图：$1）");
            stripped = LinkPattern.Replace(stripped, "$1（$2）");
            stripped = stripped.Replace("**", string.Empty).Replace("`", string.Empty);
            stripped = stripped.TrimEnd();

            if (stripped.StartsWith("---", StringComparison.Ordinal) || stripped.StartsWith("```", StringComparison.Ordinal))
                continue;

            if (stripped.StartsWith("### ", StringComparison.Ordinal))
                doc.SubHeading(stripped.Substring(4).Trim());
            else if (stripped.StartsWith("## ", StringComparison.Ordinal))
                doc.SubHeading(stripped.Substring(3).Trim());
            else if (stripped.StartsWith("# ", StringComparison.Ordinal))
                doc.Heading(stripped.Substring(2).Trim());
            else if (stripped.StartsWith("- ", StringComparison.Ordinal) || stripped.StartsWith("* ", StringComparison.Ordinal))
                doc.Paragraph("· " + stripped.Substring(2).Trim());
            else if (stripped.StartsWith("|", StringComparison.Ordinal))
                continue;   // 表格先跳过，别把竖线堆成一片
            else
                doc.Paragraph(stripped);
        }

        if (inCode && codeBlock.Length > 0)
            doc.Code(codeBlock.ToString().TrimEnd());
    }
}
