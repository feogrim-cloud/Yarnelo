using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Polka
{
    // The dock remains a process-owned window. Native hiding removes both its
    // pixels and its input surface while leaving WPF's requested visibility intact.
    internal sealed class DesktopHost : IDisposable
    {
        private const uint PositionFlags = 0x0001 | 0x0002 | 0x0010; // NOSIZE, NOMOVE, NOACTIVATE
        private readonly Window window;
        private readonly Func<bool> isTop;
        private readonly Func<bool> hasOpenMenu;
        private readonly bool toolWindow;
        private readonly DispatcherTimer timer;
        private readonly WinEventProc callback;
        private readonly uint processId;
        private IntPtr handle;
        private IntPtr eventHook;
        private bool started;
        private bool disposed;
        private bool haveTop;
        private bool previousTop;
        private bool previousDesktop;
        private bool nativeVisible;
        private DateTime requestedUntil;

        public DesktopHost(Window window, Func<bool> isTop, bool toolWindow = true, Func<bool> hasOpenMenu = null)
        {
            if (window == null) throw new ArgumentNullException("window");
            if (isTop == null) throw new ArgumentNullException("isTop");
            this.window = window;
            this.isTop = isTop;
            this.hasOpenMenu = hasOpenMenu;
            this.toolWindow = toolWindow;
            processId = unchecked((uint)Process.GetCurrentProcess().Id);
            callback = OnForegroundChanged;
            timer = new DispatcherTimer(DispatcherPriority.Background, window.Dispatcher);
            timer.Interval = TimeSpan.FromMilliseconds(200);
            timer.Tick += OnTimer;
        }

        public void Start()
        {
            if (disposed || started) return;
            handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero)
            {
                window.SourceInitialized -= OnSourceInitialized;
                window.SourceInitialized += OnSourceInitialized;
                return;
            }
            started = true;
            int style = GetStyle(handle, -16);
            SetStyle(handle, -16, style & ~0x00020000); // no WS_MINIMIZEBOX
            if (toolWindow)
            {
                int extended = GetStyle(handle, -20);
                SetStyle(handle, -20, (extended | 0x00000080) & ~0x00040000);
            }
            SetWindowPos(handle, IntPtr.Zero, 0, 0, 0, 0, PositionFlags | 0x0004 | 0x0020);
            nativeVisible = IsWindowVisible(handle);
            eventHook = SetWinEventHook(3, 3, IntPtr.Zero, callback, 0, 0, 0);
            timer.Start();
            Refresh();
        }

        private void OnSourceInitialized(object sender, EventArgs args)
        {
            window.SourceInitialized -= OnSourceInitialized;
            Start();
        }

        private void OnTimer(object sender, EventArgs args) { Evaluate(false, false); }

        private void OnForegroundChanged(IntPtr hook, uint eventType, IntPtr foreground,
            int objectId, int childId, uint thread, uint time)
        {
            if (disposed || window.Dispatcher.HasShutdownStarted) return;
            try
            {
                window.Dispatcher.BeginInvoke(DispatcherPriority.Normal,
                    new Action(delegate { Evaluate(false, false); }));
            }
            catch (InvalidOperationException) { }
        }

        public void Refresh()
        {
            if (started && !disposed) Evaluate(true, false);
        }

        // Explicit tray activation may reveal a desktop-only dock over an
        // ordinary window. It never overrides the fullscreen rule. The caller
        // activates the dock only when this returns true.
        public bool ShowRequested()
        {
            if (disposed || !started) return false;
            requestedUntil = DateTime.UtcNow.AddSeconds(1);
            return Evaluate(true, true);
        }

        internal enum ForegroundState { Unknown, Desktop, OwnWindow, DesktopMenu, ForeignWindow }

        internal static bool ShouldBeVisible(bool desktopOnly, ForegroundState state,
            bool fullscreenOnDockMonitor, bool managedVisible, bool localWindowOverDock)
        {
            if (!managedVisible) return false;
            if (fullscreenOnDockMonitor && state != ForegroundState.OwnWindow) return false;
            if (state == ForegroundState.OwnWindow) return true;
            // An active launcher above the dock, a taskbar focus transition and
            // an application on another display all leave the desktop usable.
            // Visibility follows the actual occupied area, not keyboard focus.
            return !desktopOnly || !localWindowOverDock;
        }

        private bool Evaluate(bool force, bool explicitRequest)
        {
            if (disposed || !started || handle == IntPtr.Zero) return false;
            bool top = isTop();
            bool changedTop = !haveTop || top != previousTop;
            if (changedTop)
            {
                SetWindowPos(handle, new IntPtr(top ? -1 : -2), 0, 0, 0, 0, PositionFlags);
                previousTop = top;
                haveTop = true;
            }

            IntPtr foreground = GetForegroundWindow();
            ForegroundState state = ClassifyForeground(foreground);
            bool ownProcess = state == ForegroundState.OwnWindow;
            bool desktop = state == ForegroundState.Desktop || state == ForegroundState.DesktopMenu;
            MonitorInfo monitor = new MonitorInfo(); monitor.Size = Marshal.SizeOf(typeof(MonitorInfo));
            IntPtr dockMonitor = MonitorFromWindow(handle, 2);
            bool haveMonitor = dockMonitor != IntPtr.Zero && GetMonitorInfo(dockMonitor, ref monitor);
            bool fullscreen = false, localObstruction = false;
            if (state != ForegroundState.OwnWindow && haveMonitor)
            {
                PixelRect dockBounds;
                if (!GetWindowRect(handle, out dockBounds)) dockBounds = monitor.Monitor;
                bool localFullscreen;
                FindLocalObstructions(monitor.Monitor, dockBounds, !top,
                    out localObstruction, out localFullscreen);
                fullscreen = localFullscreen;
            }
            // WPF popup HWNDs can briefly leave the foreground unset. A live
            // dock menu remains part of the dock interaction, never a foreign game.
            if (nativeVisible && hasOpenMenu != null && hasOpenMenu() && !fullscreen)
            { state = ForegroundState.OwnWindow; ownProcess = true; desktop = false; }
            bool managedVisible = window.Visibility == Visibility.Visible;
            bool allowRequest = explicitRequest || DateTime.UtcNow < requestedUntil;
            bool visible = managedVisible && (haveMonitor ?
                ShouldBeVisible(!top && !allowRequest, state, fullscreen, true, localObstruction) : nativeVisible);
            if (ownProcess || desktop) requestedUntil = DateTime.MinValue;
            bool actuallyVisible = IsWindowVisible(handle);
            bool becameVisible = visible && (!nativeVisible || !actuallyVisible);
            if (!visible)
            {
                // Check the real HWND as WPF Show() can make it visible between
                // evaluations. Manual Window.Hide() remains hidden as requested.
                if (actuallyVisible) ShowWindow(handle, 0); // SW_HIDE
                nativeVisible = false;
                if (fullscreen || !managedVisible) requestedUntil = DateTime.MinValue;
            }
            else
            {
                if (becameVisible || IsIconic(handle)) ShowWindow(handle, 4); // SW_SHOWNOACTIVATE
                nativeVisible = true;
                // Raise on a reveal even when Windows still focuses the taskbar
                // or the window it just minimized. This restores the dock above
                // the desktop automatically without clicking or taking focus.
                if ((desktop && (!previousDesktop || becameVisible || force)) ||
                    becameVisible ||
                    (explicitRequest && !fullscreen))
                    SetWindowPos(handle, new IntPtr(top ? -1 : 0), 0, 0, 0, 0, PositionFlags);
            }
            previousDesktop = desktop;
            return visible;
        }

        private bool BelongsToThisProcess(IntPtr foreground)
        {
            if (foreground == IntPtr.Zero) return false;
            uint foregroundProcessId;
            GetWindowThreadProcessId(foreground, out foregroundProcessId);
            return foregroundProcessId == processId;
        }

        private ForegroundState ClassifyForeground(IntPtr foreground)
        {
            if (foreground == IntPtr.Zero) return ForegroundState.Unknown;
            uint foregroundProcess;
            uint thread = GetWindowThreadProcessId(foreground, out foregroundProcess);
            if (thread == 0) return ForegroundState.Unknown;
            GuiThreadInfo info = new GuiThreadInfo(); info.Size = Marshal.SizeOf(typeof(GuiThreadInfo));
            bool menu = GetGUIThreadInfo(thread, ref info) && (info.Flags & 0x1c) != 0 && info.MenuOwner != IntPtr.Zero;
            // An owned dialog may be on a different display than its main
            // window. Use the actual dialog/menu owner for geometry, retaining
            // the root owner only to recognize shell and dock ownership.
            IntPtr subject = menu ? info.MenuOwner : foreground;
            IntPtr rootOwner = RootOwner(subject);
            if (foregroundProcess == processId || BelongsToThisProcess(rootOwner)) return ForegroundState.OwnWindow;
            if (IsDesktopWindow(subject) || IsDesktopWindow(rootOwner)) return menu ? ForegroundState.DesktopMenu : ForegroundState.Desktop;
            return ForegroundState.ForeignWindow;
        }

        private static IntPtr RootOwner(IntPtr target)
        {
            IntPtr owner = GetAncestor(target, 3); // GA_ROOTOWNER includes popup ownership.
            return owner == IntPtr.Zero ? target : owner;
        }

        private static bool IsDesktopWindow(IntPtr foreground)
        {
            if (foreground == IntPtr.Zero) return false;
            string className = ClassName(foreground);
            return String.Equals(className, "Progman", StringComparison.Ordinal) ||
                String.Equals(className, "WorkerW", StringComparison.Ordinal);
        }

        private static string ClassName(IntPtr foreground)
        {
            StringBuilder name = new StringBuilder(128);
            GetClassName(foreground, name, name.Capacity);
            return name.ToString();
        }

        private static bool TryGetFrame(IntPtr foreground, out PixelRect bounds)
        {
            bool haveBounds = false;
            try
            {
                haveBounds = DwmGetWindowAttribute(foreground, 9, out bounds,
                    Marshal.SizeOf(typeof(PixelRect))) == 0 &&
                    bounds.Right > bounds.Left && bounds.Bottom > bounds.Top; // DWMWA_EXTENDED_FRAME_BOUNDS
            }
            catch (DllNotFoundException) { bounds = new PixelRect(); }
            catch (EntryPointNotFoundException) { bounds = new PixelRect(); }
            return haveBounds || GetWindowRect(foreground, out bounds);
        }

        private void FindLocalObstructions(PixelRect monitor, PixelRect dockBounds, bool ordinary,
            out bool blocked, out bool fullscreen)
        {
            bool foundBlocked = false, foundFullscreen = false;
            // Resolve the current shell every pass, so restarting Explorer does
            // not leave a cached process identity behind. Modern shell tooltips
            // can be ownerless XAML popups rather than tooltips_class32 windows.
            uint taskbarProcess, secondaryTaskbarProcess;
            GetWindowThreadProcessId(FindWindow("Shell_TrayWnd", null), out taskbarProcess);
            GetWindowThreadProcessId(FindWindow("Shell_SecondaryTrayWnd", null), out secondaryTaskbarProcess);
            EnumWindows(delegate(IntPtr candidate, IntPtr unused) {
                if (!IsWindowVisible(candidate) || IsIconic(candidate) || BelongsToThisProcess(candidate)) return true;
                string name = ClassName(candidate);
                uint candidateProcess;
                GetWindowThreadProcessId(candidate, out candidateProcess);
                bool sameTaskbarProcess = candidateProcess != 0 &&
                    ((taskbarProcess != 0 && candidateProcess == taskbarProcess) ||
                    (secondaryTaskbarProcess != 0 && candidateProcess == secondaryTaskbarProcess));
                if (IsShellObstruction(name, ClassName(RootOwner(candidate)), sameTaskbarProcess)) return true;
                int cloaked;
                try { if (DwmGetWindowAttribute(candidate, 14, out cloaked, sizeof(int)) == 0 && cloaked != 0) return true; }
                catch (DllNotFoundException) { }
                catch (EntryPointNotFoundException) { }
                PixelRect frame;
                if (!TryGetFrame(candidate, out frame) || !Intersects(frame, monitor)) return true;
                if (CoversMonitor(frame, monitor)) foundFullscreen = true;
                if (ordinary && Intersects(frame, dockBounds)) foundBlocked = true;
                return !foundFullscreen;
            }, IntPtr.Zero);
            blocked = foundBlocked; fullscreen = foundFullscreen;
        }

        internal static bool IsShellObstruction(string className, string rootOwnerClass, bool sameTaskbarProcess)
        {
            if (className == "Progman" || className == "WorkerW" || className == "Shell_TrayWnd" ||
                className == "Shell_SecondaryTrayWnd" || className == "tooltips_class32" || className == "#32768" ||
                rootOwnerClass == "Progman" || rootOwnerClass == "WorkerW") return true;
            // Windows 11 taskbar hover labels use the shell's XAML popup class.
            // The class alone is insufficient: other apps can use the same XAML
            // window, and Explorer's file windows must still obstruct the dock.
            return sameTaskbarProcess && className == "Xaml_WindowedPopupClass";
        }

        internal static bool Intersects(PixelRect a, PixelRect b)
        {
            return Math.Max(a.Left, b.Left) < Math.Min(a.Right, b.Right) &&
                Math.Max(a.Top, b.Top) < Math.Min(a.Bottom, b.Bottom);
        }

        internal static bool CoversMonitor(PixelRect bounds, PixelRect monitor)
        {
            return monitor.Right > monitor.Left && monitor.Bottom > monitor.Top &&
                bounds.Right > bounds.Left && bounds.Bottom > bounds.Top &&
                bounds.Left <= monitor.Left + 2 && bounds.Top <= monitor.Top + 2 &&
                bounds.Right >= monitor.Right - 2 && bounds.Bottom >= monitor.Bottom - 2;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            timer.Stop();
            timer.Tick -= OnTimer;
            window.SourceInitialized -= OnSourceInitialized;
            if (eventHook != IntPtr.Zero) UnhookWinEvent(eventHook);
            eventHook = IntPtr.Zero;
        }

        private static int GetStyle(IntPtr target, int index)
        {
            return IntPtr.Size == 8 ? unchecked((int)GetWindowLongPtr64(target, index).ToInt64()) : GetWindowLong32(target, index);
        }

        private static void SetStyle(IntPtr target, int index, int value)
        {
            if (IntPtr.Size == 8) SetWindowLongPtr64(target, index, new IntPtr(value));
            else SetWindowLong32(target, index, value);
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct PixelRect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)]
        private struct MonitorInfo { public int Size; public PixelRect Monitor, Work; public uint Flags; }
        [StructLayout(LayoutKind.Sequential)]
        private struct GuiThreadInfo {
            public int Size; public uint Flags;
            public IntPtr Active, Focus, Capture, MenuOwner, MoveSize, Caret;
            public PixelRect CaretBounds;
        }
        private delegate void WinEventProc(IntPtr hook, uint eventType, IntPtr target, int objectId, int childId, uint thread, uint time);
        private delegate bool EnumWindowProc(IntPtr target, IntPtr parameter);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong32(IntPtr target, int index);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr64(IntPtr target, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] private static extern int SetWindowLong32(IntPtr target, int index, int value);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr64(IntPtr target, int index, IntPtr value);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr target, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr target, StringBuilder name, int size);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string className, string windowName);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr target, out uint processId);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr target);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr target);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr target, int command);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr target, out PixelRect bounds);
        [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr target, uint attribute, out PixelRect value, int size);
        [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr target, uint attribute, out int value, int size);
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr target, uint flags);
        [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr target, uint flags);
        [DllImport("user32.dll")] private static extern bool GetGUIThreadInfo(uint thread, ref GuiThreadInfo info);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowProc callback, IntPtr parameter);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
        [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr module, WinEventProc callback, uint processId, uint threadId, uint flags);
        [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
    }
}
