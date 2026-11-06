using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace TemplateWithPreview;

internal static class ShellThumbnail
{
    public static BitmapSource? TryGet(string path, int width, int height)
    {
        if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path)) return null;
        IntPtr bitmap = IntPtr.Zero;
        try
        {
            int hr = SHCreateItemFromParsingName(path, IntPtr.Zero, typeof(IShellItemImageFactory).GUID, out var factory);
            if (hr != 0 || factory == null) return null;
            var size = new SIZE { cx = Math.Max(32, width), cy = Math.Max(32, height) };
            try
            {
                factory.GetImage(size, SIIGBF.BiggerSizeOk | SIIGBF.ThumbnailOnly, out bitmap);
            }
            catch (COMException)
            {
                bitmap = IntPtr.Zero;
            }
            if (bitmap == IntPtr.Zero)
            {
                try { factory.GetImage(size, SIIGBF.BiggerSizeOk, out bitmap); }
                catch (COMException) { return null; }
            }
            if (bitmap == IntPtr.Zero) return null;
            var source = Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHCreateItemFromParsingName(string pszPath, IntPtr pbc, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, out IShellItemImageFactory ppv);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr ho);

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE
    {
        public int cx;
        public int cy;
    }

    [Flags]
    private enum SIIGBF
    {
        BiggerSizeOk = 0x1,
        ThumbnailOnly = 0x8
    }

    [ComImport]
    [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        void GetImage(SIZE size, SIIGBF flags, out IntPtr phbm);
    }
}
