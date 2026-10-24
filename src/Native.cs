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

namespace Polka
{
    internal static class Native
    {
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
            try { return new StartupRegistration(ExePath).Enabled; }
            catch(Exception ex) { throw new IOException(StartupError(ex),ex); }
        }

        public static void SetStartup(bool enabled)
        {
            try { new StartupRegistration(ExePath).SetEnabled(enabled); }
            catch(Exception ex) { throw new IOException(StartupError(ex),ex); }
        }

        internal static string StartupError(Exception error)
        {
            var startup=error as StartupRegistrationException;
            return startup==null ? error.Message : L10n.T(startup.MessageKey);
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
            else throw new FileNotFoundException(L10n.T("ShortcutMissing"), item.Path);
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

        // Automatic import must not move a shortcut while an installer is
        // still writing it. This reads only the local shortcut, never launches
        // it or calls IShellLink.Resolve (which could search a network target).
        internal static bool IsDesktopShortcutReady(string path)
        {
            object shellLink = null;
            try
            {
                string extension = Path.GetExtension(path);
                bool link = String.Equals(extension, ".lnk", StringComparison.OrdinalIgnoreCase);
                if (!link && !String.Equals(extension, ".url", StringComparison.OrdinalIgnoreCase)) return false;
                if ((File.GetAttributes(path) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0) return false;
                // Deny writers and renames while parsing and while COM loads
                // this exact file. No target path is opened or resolved.
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    if (stream.Length <= 0 || stream.Length > 4 * 1024 * 1024) return false;
                    if (!link) return InternetShortcutReady(stream);
                    byte[] data = new byte[(int)stream.Length];
                    int read = 0;
                    while (read < data.Length)
                    {
                        int count = stream.Read(data, read, data.Length - read);
                        if (count == 0) return false;
                        read += count;
                    }
                    if (!CompleteShellLink(data)) return false;
                    shellLink = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("00021401-0000-0000-C000-000000000046"), true));
                    ((System.Runtime.InteropServices.ComTypes.IPersistFile)shellLink).Load(path, 0x20); // STGM_READ | STGM_SHARE_DENY_WRITE
                    return true;
                }
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
            catch (ArgumentException) { return false; }
            catch (NotSupportedException) { return false; }
            catch (COMException) { return false; }
            catch (System.Security.SecurityException) { return false; }
            finally { if (shellLink != null && Marshal.IsComObject(shellLink)) Marshal.FinalReleaseComObject(shellLink); }
        }

        static bool InternetShortcutReady(Stream stream)
        {
            bool section = false;
            string target = null;
            using (var reader = new StreamReader(stream, Encoding.Default, true, 4096, true))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    line = line.Trim();
                    if (line.Length == 0 || line[0] == ';' || line[0] == '#') continue;
                    if (line[0] == '[')
                    {
                        section = String.Equals(line, "[InternetShortcut]", StringComparison.OrdinalIgnoreCase);
                        continue;
                    }
                    int equal = line.IndexOf('=');
                    if (!section || equal < 0 || !String.Equals(line.Substring(0, equal).Trim(), "URL", StringComparison.OrdinalIgnoreCase)) continue;
                    if (target != null) return false;
                    target = line.Substring(equal + 1).Trim();
                }
            }
            if (String.IsNullOrWhiteSpace(target) || target.Length > 65536) return false;
            foreach (char character in target) if (Char.IsControl(character)) return false;
            Uri address;
            return Uri.TryCreate(target, UriKind.Absolute, out address);
        }

        static bool CompleteShellLink(byte[] data)
        {
            // MS-SHLLINK: 76-byte header, optional IDList/LinkInfo/StringData,
            // then size-prefixed ExtraData ending in a four-byte TerminalBlock.
            // Bounds checks catch truncation that IPersistFile.Load can tolerate.
            if (data.Length < 80 || BitConverter.ToUInt32(data, 0) != 76) return false;
            byte[] clsid = new Guid("00021401-0000-0000-C000-000000000046").ToByteArray();
            for (int index = 0; index < clsid.Length; index++) if (data[index + 4] != clsid[index]) return false;
            uint flags = BitConverter.ToUInt32(data, 20);
            int cursor = 76;
            bool hasTarget = false;
            if ((flags & 1) != 0)
            {
                if (cursor + 2 > data.Length) return false;
                int bytes = BitConverter.ToUInt16(data, cursor); cursor += 2;
                if (bytes < 2 || bytes > data.Length - cursor) return false;
                int end = cursor + bytes;
                while (true)
                {
                    if (cursor + 2 > end) return false;
                    int item = BitConverter.ToUInt16(data, cursor);
                    if (item == 0) { cursor += 2; if (cursor != end) return false; break; }
                    if (item < 2 || item > end - cursor) return false;
                    hasTarget = true; cursor += item;
                }
            }
            if ((flags & 2) != 0)
            {
                if (cursor + 28 > data.Length) return false;
                uint bytes = BitConverter.ToUInt32(data, cursor), header = BitConverter.ToUInt32(data, cursor + 4);
                if (bytes < 28 || bytes > data.Length - cursor || header < 28 || header > bytes || (header > 28 && header < 36)) return false;
                for (int offset = 12; offset <= 24; offset += 4)
                {
                    uint value = BitConverter.ToUInt32(data, cursor + offset);
                    if (value != 0 && (value < header || value >= bytes)) return false;
                }
                if (header >= 36)
                    for (int offset = 28; offset <= 32; offset += 4)
                    {
                        uint value = BitConverter.ToUInt32(data, cursor + offset);
                        if (value != 0 && (value < header || value >= bytes)) return false;
                    }
                hasTarget |= (BitConverter.ToUInt32(data, cursor + 8) & 3) != 0;
                cursor += (int)bytes;
            }
            for (uint mask = 4; mask <= 64; mask <<= 1)
            {
                if ((flags & mask) == 0) continue;
                if (cursor + 2 > data.Length) return false;
                int characters = BitConverter.ToUInt16(data, cursor); cursor += 2;
                int bytes = characters * ((flags & 128) != 0 ? 2 : 1);
                if (bytes > data.Length - cursor) return false;
                if (mask == 8 && characters > 0) hasTarget = true;
                cursor += bytes;
            }
            while (cursor + 4 <= data.Length)
            {
                uint bytes = BitConverter.ToUInt32(data, cursor);
                if (bytes < 4) return cursor + 4 == data.Length && hasTarget;
                if (bytes < 8 || bytes > data.Length - cursor) return false;
                uint signature = BitConverter.ToUInt32(data, cursor + 4);
                // Environment, MSI, property-store/UWP and Vista IDList targets
                // can be valid without a filesystem path or existing target.
                if (bytes > 8 && (signature == 0xA0000001 || signature == 0xA0000006 || signature == 0xA0000009 || signature == 0xA000000C)) hasTarget = true;
                cursor += (int)bytes;
            }
            return false;
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
