using System;
using System.Drawing;
using System.Windows.Forms;

namespace FLSkin.Ui;

/// <summary>文档式滚动面板：往里面按顺序加标题 / 段落 / 链接就行。</summary>
internal sealed class DocPanel : Panel
{
    private readonly int _pad = 22;
    private readonly int _contentWidth;
    private int _y = 18;

    public DocPanel(int contentWidth)
    {
        _contentWidth = Math.Max(120, contentWidth);
        BackColor = Theme.Surface;
        AutoScroll = true;
    }

    public void Heading(string text)
    {
        Add(new Label
        {
            Text = text,
            Font = new Font("Microsoft YaHei UI", 13f, FontStyle.Bold),
            ForeColor = Theme.Text
        }, 14);
    }

    public void SubHeading(string text)
    {
        Add(new Label
        {
            Text = text,
            Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold),
            ForeColor = Theme.Text
        }, 8);
    }

    public void Paragraph(string text, Color? color = null)
    {
        Add(new Label
        {
            Text = text,
            Font = Theme.Body,
            ForeColor = color ?? Theme.TextDim
        }, 8);
    }

    public void Link(string text, Action click)
    {
        var label = new Label
        {
            Text = text,
            Font = new Font(Theme.Body, FontStyle.Underline),
            ForeColor = Theme.Accent,
            Cursor = Cursors.Hand,
            AutoSize = true
        };

        label.MouseEnter += (_, _) => label.ForeColor = Theme.AccentHover;
        label.MouseLeave += (_, _) => label.ForeColor = Theme.Accent;
        label.Click += (_, _) => click();

        Add(label, 6);
    }

    public void Code(string text)
    {
        Add(new Label
        {
            Text = text,
            Font = new Font("Consolas", 9f),
            ForeColor = Theme.TextFaint
        }, 4);
    }

    public void Gap(int height)
    {
        _y += height;
    }

    private void Add(Label label, int spacing)
    {
        label.AutoSize = true;
        label.MaximumSize = new Size(_contentWidth - _pad * 2, 0);
        label.Location = new Point(_pad, _y);
        label.BackColor = Theme.Surface;

        Controls.Add(label);
        _y += label.Height + spacing;
    }
}
