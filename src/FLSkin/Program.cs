using System;
using System.Threading;
using System.Windows.Forms;

namespace FLSkin;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(true, @"Local\FLSkin.SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show("FLSkin 已经在运行了，请在系统托盘（右下角）里找它。", "FLSkin",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new AppController());
    }
}
