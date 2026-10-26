using System;

namespace Polka
{
    // These tests exercise only policy and synthetic rectangles. They never
    // construct a Window/DesktopHost, enumerate HWNDs, or inspect the desktop.
    internal static class DesktopHostTests
    {
        private static int assertions;

        private static void Check(bool value, string message)
        {
            if (!value) throw new Exception(message);
            assertions++;
        }

        private static DesktopHost.PixelRect Rect(int left, int top, int right, int bottom)
        {
            return new DesktopHost.PixelRect { Left = left, Top = top, Right = right, Bottom = bottom };
        }

        private static void ShellObstructions()
        {
            foreach (string name in new[] { "Progman", "WorkerW", "Shell_TrayWnd",
                "Shell_SecondaryTrayWnd", "tooltips_class32", "#32768" })
            {
                Check(DesktopHost.IsShellObstruction(name, "", false),
                    "Existing shell/tooltip/menu exemption remains: " + name);
                Check(DesktopHost.IsShellObstruction(name, "ForeignWindow", true),
                    "Existing class exemption does not depend on a root owner: " + name);
            }
            foreach (string root in new[] { "Progman", "WorkerW" })
                Check(DesktopHost.IsShellObstruction("DesktopPopup", root, false),
                    "Desktop-owned window remains exempt: " + root);

            const string taskbarPopup = "Xaml_WindowedPopupClass";
            Check(DesktopHost.IsShellObstruction(taskbarPopup, taskbarPopup, true),
                "An ownerless Windows 11 hover label in the live taskbar process is exempt");
            foreach (string root in new[] { "Shell_TrayWnd", "Shell_SecondaryTrayWnd" })
                Check(DesktopHost.IsShellObstruction(taskbarPopup, root, true),
                    "A taskbar-owned Windows 11 hover label is exempt: " + root);
            Check(!DesktopHost.IsShellObstruction(taskbarPopup, taskbarPopup, false),
                "The identical XAML popup class from another app remains an obstruction");
            Check(!DesktopHost.IsShellObstruction(taskbarPopup, "Shell_TrayWnd", false),
                "Taskbar ownership cannot exempt the XAML popup without matching its process");
            foreach (string name in new[] { "XamlExplorerHostIslandWindow", "XamlExplorerHostIslandWindow_WASDK", "Windows.UI.Core.CoreWindow" })
                Check(!DesktopHost.IsShellObstruction(name, "Shell_TrayWnd", true),
                    "A generic XAML host is not mistaken for a hover label: " + name);

            Check(!DesktopHost.IsShellObstruction("CabinetWClass", "CabinetWClass", true),
                "An Explorer file window is still an obstruction despite its shell process");
            Check(!DesktopHost.IsShellObstruction("ApplicationWindow", "ApplicationWindow", false),
                "A regular app window remains an obstruction");
            Check(!DesktopHost.IsShellObstruction("ApplicationWindow", "Shell_TrayWnd", true),
                "Taskbar ownership alone does not exempt an arbitrary app class");
            Check(!DesktopHost.IsShellObstruction("ApplicationWindow", "Shell_SecondaryTrayWnd", true),
                "Secondary-taskbar ownership alone does not exempt an arbitrary app class");
            Check(!DesktopHost.IsShellObstruction("", "", false),
                "An unknown class is not silently treated as a shell tooltip");

            var dock = Rect(300, 950, 1620, 1030);
            var tooltip = Rect(500, 930, 580, 975);
            Check(DesktopHost.Intersects(tooltip, dock),
                "Regression fixture reproduces the hover label overlapping dock space");
            bool labelBlocked = !DesktopHost.IsShellObstruction(taskbarPopup, taskbarPopup, true)
                && DesktopHost.Intersects(tooltip, dock);
            Check(DesktopHost.ShouldBeVisible(true, DesktopHost.ForegroundState.ForeignWindow, false, true, labelBlocked),
                "An overlapping taskbar hover label no longer hides the desktop-only dock");
            bool foreignPopupBlocked = !DesktopHost.IsShellObstruction(taskbarPopup, taskbarPopup, false)
                && DesktopHost.Intersects(tooltip, dock);
            Check(!DesktopHost.ShouldBeVisible(true, DesktopHost.ForegroundState.ForeignWindow, false, true, foreignPopupBlocked),
                "An app popup with identical class and geometry can still hide the desktop-only dock");
        }

        private static void Visibility()
        {
            foreach (DesktopHost.ForegroundState state in Enum.GetValues(typeof(DesktopHost.ForegroundState)))
            {
                foreach (bool desktopOnly in new[] { false, true })
                {
                    Check(!DesktopHost.ShouldBeVisible(desktopOnly, state, false, false, false),
                        "An explicitly hidden managed window remains hidden: " + state);
                    Check(!DesktopHost.ShouldBeVisible(desktopOnly, state, true, false, true),
                        "A fullscreen transition never reveals a managed-hidden window: " + state);
                    Check(DesktopHost.ShouldBeVisible(desktopOnly, state, false, true, false),
                        "Unoccupied dock area remains visible regardless of foreground focus: " + state);

                    if (state == DesktopHost.ForegroundState.OwnWindow)
                    {
                        Check(DesktopHost.ShouldBeVisible(desktopOnly, state, true, true, true),
                            "Own dock interaction retains the existing fullscreen exception");
                    }
                    else
                    {
                        Check(!DesktopHost.ShouldBeVisible(desktopOnly, state, true, true, false),
                            "Fullscreen game hides dock even when no ordinary obstruction was recorded: " + state);
                        Check(!DesktopHost.ShouldBeVisible(desktopOnly, state, true, true, true),
                            "Fullscreen game hides dock in desktop and above-ordinary-windows modes: " + state);
                    }
                }
            }
            Check(!DesktopHost.ShouldBeVisible(true, DesktopHost.ForegroundState.ForeignWindow, false, true, true),
                "A normal app covering the dock still hides a desktop-only dock");
            Check(DesktopHost.ShouldBeVisible(false, DesktopHost.ForegroundState.ForeignWindow, false, true, true),
                "Above-ordinary-windows mode still permits overlap with a normal app");
            Check(DesktopHost.ShouldBeVisible(true, DesktopHost.ForegroundState.OwnWindow, false, true, true),
                "An active Yarnelo window/menu remains visible over an obstruction");
        }

        private static void Geometry()
        {
            var primary = Rect(0, 0, 1920, 1080);
            var secondary = Rect(-1920, 0, 0, 1080);
            var dock = Rect(300, 950, 1620, 1030);
            Check(DesktopHost.Intersects(Rect(400, 970, 700, 1010), dock),
                "An ordinary app overlapping dock space is detected");
            Check(!DesktopHost.Intersects(Rect(300, 100, 1600, 900), dock),
                "A launcher above the dock does not occupy its space");
            Check(!DesktopHost.Intersects(secondary, primary),
                "Adjacent monitors sharing an edge do not intersect");
            Check(!DesktopHost.Intersects(secondary, dock),
                "A fullscreen window on another monitor does not cover dock space");
            Check(!DesktopHost.CoversMonitor(secondary, primary),
                "A fullscreen window on another monitor is not local fullscreen");
            Check(DesktopHost.CoversMonitor(primary, primary),
                "A borderless game exactly covering the dock monitor is fullscreen");
            Check(DesktopHost.CoversMonitor(Rect(-2, -2, 1922, 1082), primary),
                "A borderless frame extending past monitor bounds is fullscreen");
            Check(DesktopHost.CoversMonitor(Rect(2, 2, 1918, 1078), primary),
                "The existing two-pixel fullscreen tolerance is retained");
            Check(!DesktopHost.CoversMonitor(Rect(3, 0, 1920, 1080), primary),
                "An ordinary window beyond the fullscreen tolerance is not fullscreen");
            Check(!DesktopHost.CoversMonitor(dock, primary),
                "A small tooltip-sized surface is not fullscreen");
            Check(!DesktopHost.CoversMonitor(Rect(0, 0, 0, 0), primary),
                "Empty window geometry does not count as fullscreen");
            Check(!DesktopHost.CoversMonitor(primary, Rect(0, 0, 0, 0)),
                "An invalid monitor does not count as fullscreen");
            Check(!DesktopHost.Intersects(Rect(300, 900, 1620, 950), dock),
                "A window touching only the dock boundary does not hide it");
        }

        public static int Main()
        {
            try
            {
                ShellObstructions();
                Visibility();
                Geometry();
                Console.WriteLine("PASS: " + assertions + " desktop visibility assertions; no HWNDs or desktop state were accessed.");
                return 0;
            }
            catch (Exception error)
            {
                Console.WriteLine(error);
                return 1;
            }
        }
    }
}
