using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Forms;

// Synthetic installer test window only. No dock source, registry, profile,
// imported shortcuts, desktop integration, network or child process is used.
internal static class MaintenanceFixture
{
    private sealed class FixtureWindow : Form
    {
        internal bool ReceivedClose;
        internal bool Cleanup;
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x0010) ReceivedClose = true; // WM_CLOSE
            base.WndProc(ref message);
        }
    }

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 4) return 80;
        string title = args[0] == "en" ? "Yarnelo — shortcut dock" :
            args[0] == "ru" ? "Yarnelo — панель ярлыков" : "Unrelated fixture window";
        string ready = Path.GetFullPath(args[1]);
        string closed = Path.GetFullPath(args[2]);
        bool receivedClose = false;
        using (EventWaitHandle cleanup = EventWaitHandle.OpenExisting(args[3]))
        using (FixtureWindow window = new FixtureWindow())
        using (System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer())
        {
            // The native HWND exists for EnumWindows but never obscures the runner.
            window.Text = title;
            window.ShowInTaskbar = false;
            window.Opacity = 0;
            window.Width = window.Height = 16;
            DateTime deadline = DateTime.UtcNow.AddSeconds(120);
            timer.Interval = 100;
            timer.Tick += delegate
            {
                if (cleanup.WaitOne(0) || DateTime.UtcNow >= deadline)
                {
                    window.Cleanup = true;
                    window.Close();
                }
            };
            window.Shown += delegate
            {
                File.WriteAllText(ready + ".tmp", Process.GetCurrentProcess().Id.ToString() + "|" + title);
                File.Move(ready + ".tmp", ready);
                timer.Start();
            };
            window.FormClosed += delegate
            {
                timer.Stop();
                receivedClose = window.ReceivedClose && !window.Cleanup;
                File.WriteAllText(closed, receivedClose ? "WM_CLOSE" : "test-cleanup");
            };
            Application.Run(window);
        }
        return receivedClose ? 0 : 10;
    }
}
