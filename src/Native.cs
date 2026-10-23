using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Polka
{
    internal static class Native
    {
        private const string StartupKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string StartupName = "Yarnelo";
        private static readonly object iconQueueLock = new object();
        private static readonly Queue<IconRequest> iconQueue = new Queue<IconRequest>();
        private static readonly AutoResetEvent iconWake = new AutoResetEvent(false);
        private static Thread iconThread;

        private sealed class IconRequest
        {
            public string Path;
            public ImageSource Result;
            public readonly ManualResetEvent Completed = new ManualResetEvent(false);
        }

        public static string ExePath
        {
            get { return Process.GetCurrentProcess().MainModule.FileName; }
        }

        public static bool GetStartupEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(StartupKey, false))
                {
                    string command = key == null ? null : key.GetValue(StartupName) as string;
                    return String.Equals(command, Quote(ExePath), StringComparison.OrdinalIgnoreCase);
                }
            }
            catch { return false; }
        }

        public static void SetStartup(bool enabled)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(StartupKey))
            {
                if (key == null) throw new IOException("Не удалось открыть параметры автозапуска.");
                if (enabled) key.SetValue(StartupName, Quote(ExePath), RegistryValueKind.String);
                else key.DeleteValue(StartupName, false);
            }
        }

        public static void Launch(DockItem item)
        {
            if (item == null || String.IsNullOrWhiteSpace(item.Path)) return;
            if (IsShellPath(item.Path))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", Quote(item.Path)) { UseShellExecute = true });
                return;
            }
            // Open the shortcut itself: the shell retains its arguments, working
            // directory, elevation flags, and non-filesystem target information.
            Process.Start(new ProcessStartInfo(item.Path) { UseShellExecute = true });
        }

        public static void Reveal(DockItem item)
        {
            if (item == null || String.IsNullOrWhiteSpace(item.Path)) return;
            if (IsShellPath(item.Path))
            {
                Launch(item);
                return;
            }
            if (File.Exists(item.Path) || Directory.Exists(item.Path))
                Process.Start(new ProcessStartInfo("explorer.exe", "/select," + Quote(item.Path)) { UseShellExecute = true });
            else throw new FileNotFoundException("Этот ярлык больше не найден.", item.Path);
        }

        private static bool IsShellPath(string path)
        {
            return path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("::{", StringComparison.Ordinal);
        }

        private static string Quote(string value)
        {
            // File and shell paths cannot contain a double quote on Windows.
            return "\"" + value.Replace("\"", String.Empty) + "\"";
        }

        public static ImageSource GetIcon(string path)
        {
            // Shell icon handlers, including Recycle Bin, can require a true
            // STA apartment. Task.Run uses MTA threads, so its requests are
            // serialized on a dedicated background STA without blocking the UI.
            if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
                return ReadIcon(path);
            IconRequest request = new IconRequest { Path = path };
            lock (iconQueueLock)
            {
                if (iconThread == null)
                {
                    iconThread = new Thread(ProcessIconRequests);
                    iconThread.Name = "Yarnelo Shell Icons";
                    iconThread.IsBackground = true;
                    iconThread.SetApartmentState(ApartmentState.STA);
                    iconThread.Start();
                }
                iconQueue.Enqueue(request);
            }
            iconWake.Set();
            request.Completed.WaitOne();
            request.Completed.Dispose();
            return request.Result;
        }

        private static void ProcessIconRequests()
        {
            while (true)
            {
                IconRequest request = null;
                lock (iconQueueLock)
                {
                    if (iconQueue.Count != 0) request = iconQueue.Dequeue();
                }
                if (request == null)
                {
                    iconWake.WaitOne();
                    continue;
                }
                try { request.Result = ReadIcon(request.Path); }
                catch { request.Result = FallbackIcon(); }
                finally { request.Completed.Set(); }
            }
        }

        private static ImageSource ReadIcon(string path)
        {
            IntPtr itemId = IntPtr.Zero;
            IntPtr icon = IntPtr.Zero;
            bool initialized = false;
            try
            {
                int result = CoInitializeEx(IntPtr.Zero, 2);
                initialized = result >= 0;
                SHFILEINFO info = new SHFILEINFO();
                IntPtr found;
                const uint iconFlags = 0x00000100 | 0x00004000; // ICON | SYSICONINDEX | LARGEICON
                if (IsShellPath(path))
                {
                    uint attributes;
                    // Use canonical namespace parsing names for these built-in
                    // items; shell: aliases depend on shell alias resolution.
                    string parsingName = path;
                    if (String.Equals(path, "shell:MyComputerFolder", StringComparison.OrdinalIgnoreCase))
                        parsingName = "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}";
                    else if (String.Equals(path, "shell:RecycleBinFolder", StringComparison.OrdinalIgnoreCase))
                        parsingName = "::{645FF040-5081-101B-9F08-00AA002F954E}";
                    int parsed = SHParseDisplayName(parsingName, IntPtr.Zero, out itemId, 0, out attributes);
                    if (parsed < 0 || itemId == IntPtr.Zero) return FallbackIcon();
                    found = SHGetFileInfoPidl(itemId, 0, out info, (uint)Marshal.SizeOf(typeof(SHFILEINFO)), iconFlags | 8);
                }
                else
                {
                    found = SHGetFileInfoPath(path, 0, out info, (uint)Marshal.SizeOf(typeof(SHFILEINFO)), iconFlags);
                }
                icon = info.hIcon;
                if (found == IntPtr.Zero) return FallbackIcon();
                // System image-list indices are shared across the shell's size
                // variants. Read the cached full-resolution icon when supplied;
                // older applications still have the 48px and 32px fallbacks.
                BitmapSource larger = GetImageListIcon(info.iIcon, 4); // SHIL_JUMBO
                if (larger == null || ContentSpan(larger) < 64)
                {
                    // Some older icon handlers put a 32px image in a transparent
                    // 256px canvas. Their 48px variant can contain more detail.
                    BitmapSource extraLarge = GetImageListIcon(info.iIcon, 2); // SHIL_EXTRALARGE
                    if (extraLarge != null && (larger == null || ContentSpan(extraLarge) > ContentSpan(larger)))
                        larger = extraLarge;
                }
                if (larger != null) return NormalizeIconBitmap(larger);
                if (icon == IntPtr.Zero) return FallbackIcon();
                BitmapSource source = Imaging.CreateBitmapSourceFromHIcon(icon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                return NormalizeIconBitmap(source);
            }
            catch { return FallbackIcon(); }
            finally
            {
                if (icon != IntPtr.Zero) DestroyIcon(icon);
                if (itemId != IntPtr.Zero) Marshal.FreeCoTaskMem(itemId);
                if (initialized) CoUninitialize();
            }
        }

        private static BitmapSource GetImageListIcon(int index, int listSize)
        {
            IImageList images = null;
            IntPtr icon = IntPtr.Zero;
            try
            {
                Guid interfaceId = typeof(IImageList).GUID;
                if (SHGetImageList(listSize, ref interfaceId, out images) < 0 || images == null) return null;
                if (images.GetIcon(index, 1, out icon) < 0 || icon == IntPtr.Zero) return null; // ILD_TRANSPARENT
                BitmapSource source = Imaging.CreateBitmapSourceFromHIcon(icon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                return source;
            }
            catch { return null; }
            finally
            {
                if (icon != IntPtr.Zero) DestroyIcon(icon);
                if (images != null) Marshal.ReleaseComObject(images);
            }
        }

        private static Int32Rect IconContentBounds(BitmapSource source)
        {
            BitmapSource pixels = source;
            if (pixels.Format != PixelFormats.Bgra32 && pixels.Format != PixelFormats.Pbgra32)
                pixels = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            int width = pixels.PixelWidth, height = pixels.PixelHeight;
            int stride = checked(width * 4);
            byte[] buffer = new byte[checked(stride * height)];
            pixels.CopyPixels(buffer, stride, 0);
            int left = width, top = height, right = -1, bottom = -1;
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    // Ignore nearly invisible antialiasing and distant shadows.
                    if (buffer[y * stride + x * 4 + 3] < 16) continue;
                    if (x < left) left = x;
                    if (x > right) right = x;
                    if (y < top) top = y;
                    if (y > bottom) bottom = y;
                }
            return right < left ? Int32Rect.Empty : new Int32Rect(left, top, right - left + 1, bottom - top + 1);
        }

        private static int ContentSpan(BitmapSource source)
        {
            Int32Rect bounds = IconContentBounds(source);
            return Math.Max(bounds.Width, bounds.Height);
        }

        internal static BitmapSource NormalizeIconBitmap(BitmapSource source)
        {
            Int32Rect bounds = IconContentBounds(source);
            int span = Math.Max(bounds.Width, bounds.Height);
            int canvas = Math.Max(source.PixelWidth, source.PixelHeight);
            BitmapSource result = source;
            // Keep normal icon margins and all native pixels. Only remove the
            // excessive empty canvas that makes legacy application icons tiny.
            if (span > 0 && span < canvas * .76)
            {
                int padding = Math.Max(1, (int)Math.Ceiling(span * .035));
                int left = Math.Max(0, bounds.X - padding);
                int top = Math.Max(0, bounds.Y - padding);
                int right = Math.Min(source.PixelWidth, bounds.X + bounds.Width + padding);
                int bottom = Math.Min(source.PixelHeight, bounds.Y + bounds.Height + padding);
                result = new CroppedBitmap(source, new Int32Rect(left, top, right - left, bottom - top));
            }
            result.Freeze();
            return result;
        }

        private static ImageSource FallbackIcon()
        {
            DrawingGroup group = new DrawingGroup();
            group.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(81, 151, 233)), null,
                new RectangleGeometry(new Rect(3, 3, 42, 42), 10, 10)));
            SolidColorBrush light = new SolidColorBrush(Color.FromRgb(232, 245, 255));
            for (int row = 0; row < 2; row++)
                for (int col = 0; col < 2; col++)
                    group.Children.Add(new GeometryDrawing(light, null,
                        new RectangleGeometry(new Rect(13 + col * 13, 13 + row * 13, 9, 9), 2, 2)));
            DrawingImage image = new DrawingImage(group);
            image.Freeze();
            return image;
        }

        public static Rect WorkingArea(Window window)
        {
            try
            {
                IntPtr handle = new WindowInteropHelper(window).Handle;
                System.Drawing.Rectangle area = System.Windows.Forms.Screen.FromHandle(handle).WorkingArea;
                PresentationSource source = PresentationSource.FromVisual(window);
                Matrix transform = source != null && source.CompositionTarget != null
                    ? source.CompositionTarget.TransformFromDevice : Matrix.Identity;
                Point origin = transform.Transform(new Point(area.X, area.Y));
                Point end = transform.Transform(new Point(area.Right, area.Bottom));
                return new Rect(origin, end);
            }
            catch { return SystemParameters.WorkArea; }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
        }

        [DllImport("shell32.dll", EntryPoint = "SHGetFileInfoW", CharSet = CharSet.Unicode)]
        private static extern IntPtr SHGetFileInfoPath(string path, uint attributes, out SHFILEINFO info, uint size, uint flags);
        [DllImport("shell32.dll", EntryPoint = "SHGetFileInfoW", CharSet = CharSet.Unicode)]
        private static extern IntPtr SHGetFileInfoPidl(IntPtr itemId, uint attributes, out SHFILEINFO info, uint size, uint flags);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHParseDisplayName(string name, IntPtr binding, out IntPtr itemId, uint attributesIn, out uint attributesOut);
        [DllImport("shell32.dll")]
        private static extern int SHGetImageList(int imageList, ref Guid interfaceId, [MarshalAs(UnmanagedType.Interface)] out IImageList images);

        // The first eight IImageList methods retain their native vtable order.
        // Only GetIcon is called; the shared shell image lists are never changed.
        [ComImport, Guid("46EB5926-582E-4017-9FDF-E8998DAA0950"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IImageList
        {
            [PreserveSig] int Add(IntPtr image, IntPtr mask, out int index);
            [PreserveSig] int ReplaceIcon(int index, IntPtr icon, out int newIndex);
            [PreserveSig] int SetOverlayImage(int image, int overlay);
            [PreserveSig] int Replace(int index, IntPtr image, IntPtr mask);
            [PreserveSig] int AddMasked(IntPtr image, uint mask, out int index);
            [PreserveSig] int Draw(IntPtr drawParameters);
            [PreserveSig] int Remove(int index);
            [PreserveSig] int GetIcon(int index, uint flags, out IntPtr icon);
        }
        [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
        [DllImport("ole32.dll")] private static extern int CoInitializeEx(IntPtr reserved, uint flags);
        [DllImport("ole32.dll")] private static extern void CoUninitialize();
    }

}
