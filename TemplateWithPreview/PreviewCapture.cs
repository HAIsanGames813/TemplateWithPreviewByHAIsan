using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using YukkuriMovieMaker.Plugin.Effects;
using YukkuriMovieMaker.Project.Items;

namespace TemplateWithPreview;

/// <summary>
/// Temporarily enables only the chosen effects on the selected item and captures
/// the visible YMM4 preview surface. Every effect state is restored in a finally block.
/// </summary>
internal static class PreviewCapture
{
    private const uint Srccopy = 0x00CC0020;
    private const uint CaptureBlt = 0x40000000;
    private const uint DibRgbColors = 0;
    private const uint BiRgb = 0;

    public static async Task<BitmapSource?> CaptureSelectedItemAsync(IItem sourceItem, IReadOnlyCollection<IVideoEffect> selectedEffects)
    {
        var allEffects = PresetApplier.GetVideoEffects(sourceItem);
        if (allEffects.Count == 0 || selectedEffects.Count == 0) return null;

        var originalStates = allEffects.Select(effect => (Effect: effect, Enabled: effect.IsEnabled)).ToList();
        var voiceItem = sourceItem as VoiceItem;
        var originalJimakuVisibility = voiceItem?.JimakuVisibility;
        try
        {
            if (voiceItem != null) voiceItem.JimakuVisibility = JimakuVisibility.Custom;
            foreach (var effect in allEffects)
            {
                effect.IsEnabled = selectedEffects.Any(selected => ReferenceEquals(selected, effect));
            }

            await Application.Current.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Render);
            await Task.Delay(200);

            return await Application.Current.Dispatcher.InvokeAsync(CapturePreviewHost, System.Windows.Threading.DispatcherPriority.Render);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PreviewCapture] Preview capture failed: {ex}");
            return null;
        }
        finally
        {
            foreach (var state in originalStates)
            {
                try { state.Effect.IsEnabled = state.Enabled; }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[PreviewCapture] Failed to restore effect state: {ex}"); }
            }
            if (voiceItem != null && originalJimakuVisibility.HasValue)
            {
                try { voiceItem.JimakuVisibility = originalJimakuVisibility.Value; }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[PreviewCapture] Failed to restore subtitle setting: {ex}"); }
            }
            await Application.Current.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Render);
        }
    }

    private static BitmapSource? CapturePreviewHost()
    {
        var windows = Application.Current.Windows.Cast<Window>()
            .OrderByDescending(window => window.GetType().Name == "MainView")
            .ToList();
        var candidates = new List<FrameworkElement>();
        foreach (var window in windows)
        {
            FindPreviewHosts(window, candidates);
        }

        var host = candidates
            .Where(element => element.IsVisible && element.ActualWidth >= 64 && element.ActualHeight >= 64)
            .OrderByDescending(element => element.ActualWidth * element.ActualHeight)
            .FirstOrDefault();
        if (host == null) return null;

        var origin = host.PointToScreen(new Point(0, 0));
        var transform = PresentationSource.FromVisual(host)?.CompositionTarget?.TransformToDevice ?? Matrix.Identity;
        int width = Math.Max(1, (int)Math.Round(host.ActualWidth * transform.M11));
        int height = Math.Max(1, (int)Math.Round(host.ActualHeight * transform.M22));
        int x = (int)Math.Round(origin.X);
        int y = (int)Math.Round(origin.Y);

        IntPtr screenDc = GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero) return null;
        IntPtr memoryDc = IntPtr.Zero;
        IntPtr bitmap = IntPtr.Zero;
        IntPtr oldBitmap = IntPtr.Zero;
        try
        {
            memoryDc = CreateCompatibleDC(screenDc);
            var info = new BitmapInfo
            {
                Header = new BitmapInfoHeader
                {
                    Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
                    Width = width,
                    Height = -height,
                    Planes = 1,
                    BitCount = 32,
                    Compression = BiRgb,
                    SizeImage = (uint)(width * height * 4)
                }
            };
            bitmap = CreateDIBSection(screenDc, ref info, DibRgbColors, out var bits, IntPtr.Zero, 0);
            if (bitmap == IntPtr.Zero || bits == IntPtr.Zero) return null;
            oldBitmap = SelectObject(memoryDc, bitmap);
            if (!BitBlt(memoryDc, 0, 0, width, height, screenDc, x, y, Srccopy | CaptureBlt)) return null;

            var pixels = new byte[width * height * 4];
            Marshal.Copy(bits, pixels, 0, pixels.Length);
            var image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr32, null, pixels, width * 4);
            image.Freeze();
            return image;
        }
        finally
        {
            if (oldBitmap != IntPtr.Zero) SelectObject(memoryDc, oldBitmap);
            if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
            if (memoryDc != IntPtr.Zero) DeleteDC(memoryDc);
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    private static void FindPreviewHosts(DependencyObject parent, List<FrameworkElement> hosts)
    {
        if (parent.GetType().FullName == "System.Windows.Forms.Integration.WindowsFormsHost" && parent is FrameworkElement host)
        {
            hosts.Add(host);
        }

        int childCount;
        try { childCount = VisualTreeHelper.GetChildrenCount(parent); }
        catch { return; }
        for (int index = 0; index < childCount; index++)
        {
            try { FindPreviewHosts(VisualTreeHelper.GetChild(parent, index), hosts); }
            catch { }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ColorsUsed;
        public uint ColorsImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public BitmapInfoHeader Header;
        public uint Color;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfo info, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(IntPtr destination, int x, int y, int width, int height, IntPtr source, int sourceX, int sourceY, uint operation);
}
