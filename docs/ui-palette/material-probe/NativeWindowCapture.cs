using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

// Evidence-only capture: a guarded crop inside the selected synthetic window.
// No full-desktop image, window titles, keyboard input or unrelated screenshots.
internal static class NativeWindowCapture
{
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct BitmapHeader
    {
        public uint Size; public int Width, Height; public ushort Planes, BitCount;
        public uint Compression, SizeImage; public int XPels, YPels; public uint ColorsUsed, ColorsImportant;
    }
    private delegate bool WindowVisitor(IntPtr hwnd, IntPtr parameter);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint process);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint source, uint target, bool attach);
    [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] private static extern bool EnumWindows(WindowVisitor visitor, IntPtr parameter);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out Rect value, int size);
    [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")] private static extern int GetCloaked(IntPtr hwnd, int attribute, out int value, int size);
    [DllImport("dwmapi.dll")] private static extern int DwmFlush();
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int width, int height);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr value);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr value);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr target, int x, int y, int width, int height, IntPtr source, int sourceX, int sourceY, uint operation);
    [DllImport("gdi32.dll")] private static extern int GetDIBits(IntPtr dc, IntPtr bitmap, uint start, uint count, [Out] byte[] pixels, ref BitmapHeader info, uint usage);

    internal static bool TryActivate(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        uint source = GetCurrentThreadId(), target = GetWindowThreadProcessId(GetForegroundWindow(), out _);
        bool attached = target != 0 && source != target && AttachThreadInput(source, target, true);
        try { window.Activate(); BringWindowToTop(handle); SetForegroundWindow(handle); return GetForegroundWindow() == handle; }
        finally { if (attached) AttachThreadInput(source, target, false); }
    }

    public static bool TryCapture(Window window, string file, out string detail)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (!window.Topmost || !IsWindowVisible(handle) || IsIconic(handle))
        { detail = "Blocked: selected preview must be visible, restored and evidence-only topmost; no screen pixels read."; return false; }
        if (DwmGetWindowAttribute(handle, 9, out var bounds, Marshal.SizeOf<Rect>()) < 0)
        { detail = "Blocked: no reliable DWM window bounds; no screen pixels read."; return false; }
        // Exclude rounded native corners/borders and all desktop outside the window.
        bounds.Left += 14; bounds.Right -= 14; bounds.Top += 2; bounds.Bottom -= 14;
        int left = GetSystemMetrics(76), top = GetSystemMetrics(77);
        if (bounds.Left < left || bounds.Top < top || bounds.Right > left + GetSystemMetrics(78) || bounds.Bottom > top + GetSystemMetrics(79))
        { detail = "Blocked: selected-window crop extends beyond the virtual screen; no screen pixels read."; return false; }
        bool IsUnobstructed()
        {
            var found = false;
            var obstructed = false;
            EnumWindows((candidate, _) =>
            {
                if (candidate == handle) { found = true; return false; }
                if (!IsWindowVisible(candidate)) return true;
                if (GetCloaked(candidate, 14, out var cloaked, sizeof(int)) >= 0 && cloaked != 0) return true;
                if (!GetWindowRect(candidate, out var other)) { obstructed = true; return false; }
                if (other.Left < bounds.Right && other.Right > bounds.Left && other.Top < bounds.Bottom && other.Bottom > bounds.Top)
                { obstructed = true; return false; }
                return true;
            }, IntPtr.Zero);
            return found && !obstructed;
        }
        if (!IsUnobstructed()) { detail = "Blocked: crop could contain an overlapping window; no screen pixels read."; return false; }
        int width = bounds.Right - bounds.Left, height = bounds.Bottom - bounds.Top;
        if (width <= 0 || height <= 0 || width > 5000 || height > 5000) throw new InvalidOperationException("Unexpected selected-window bounds.");
        DwmFlush();
        if (!IsUnobstructed()) { detail = "Blocked: occlusion changed before capture; no screen pixels read."; return false; }
        bool active = GetForegroundWindow() == handle;
        var screen = GetDC(IntPtr.Zero);
        var memory = CreateCompatibleDC(screen);
        var bitmap = CreateCompatibleBitmap(screen, width, height);
        var old = SelectObject(memory, bitmap);
        try
        {
            if (!BitBlt(memory, 0, 0, width, height, screen, bounds.Left, bounds.Top, 0x00CC0020)) throw new InvalidOperationException("Selected-window crop failed.");
            SelectObject(memory, old);
            var info = new BitmapHeader { Size = 40, Width = width, Height = -height, Planes = 1, BitCount = 32 };
            var pixels = new byte[width * height * 4];
            if (GetDIBits(memory, bitmap, 0, (uint)height, pixels, ref info, 0) != height) throw new InvalidOperationException("Incomplete selected-window pixels.");
            if (!IsUnobstructed() || DwmGetWindowAttribute(handle, 9, out var current, Marshal.SizeOf<Rect>()) < 0 ||
                current.Left + 14 != bounds.Left || current.Top + 2 != bounds.Top || current.Right - 14 != bounds.Right || current.Bottom - 14 != bounds.Bottom)
            { detail = "Blocked: bounds/occlusion changed during capture; pixels discarded, no file written."; return false; }
            var image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr32, null, pixels, width * 4);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
            using var stream = File.Create(file); encoder.Save(stream);
            detail = $"Composited unobstructed selected-window crop, {width}x{height} physical pixels; foreground={active}; native caption included, outer corners/borders excluded.";
            return true;
        }
        finally { DeleteObject(bitmap); DeleteDC(memory); ReleaseDC(IntPtr.Zero, screen); }
    }
}
