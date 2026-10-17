using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Yolk.Tray;

/// <summary>
/// The three tray icons, drawn once from the app icon: full colour when
/// active, greyscale when idle, faded when paused.
/// </summary>
/// <remarks>
/// The Mac's menu bar glyphs are black template images, which would vanish on
/// a dark taskbar; the coloured egg reads on both.
/// </remarks>
internal sealed partial class TrayIcons : IDisposable
{
    public Icon Active { get; }
    public Icon Idle { get; }
    public Icon Paused { get; }

    public TrayIcons()
    {
        using var stream = typeof(TrayIcons).Assembly.GetManifestResourceStream("Yolk.png")
            ?? throw new InvalidOperationException("the Yolk.png resource is missing");
        using var source = new Bitmap(stream);
        var size = SystemInformation.SmallIconSize;

        Active = Render(source, size, new ColorMatrix());
        Idle = Render(source, size, Greyscale());
        Paused = Render(source, size, new ColorMatrix { Matrix33 = 0.45f });
    }

    private static Icon Render(Bitmap source, Size size, ColorMatrix matrix)
    {
        using var bitmap = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        using (var attributes = new ImageAttributes())
        {
            attributes.SetColorMatrix(matrix);
            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            graphics.DrawImage(source, new Rectangle(Point.Empty, size),
                0, 0, source.Width, source.Height, GraphicsUnit.Pixel, attributes);
        }
        // Icon.FromHandle doesn't own the HICON, so clone it into an Icon that
        // does and destroy the original.
        var handle = bitmap.GetHicon();
        try
        {
            using var borrowed = Icon.FromHandle(handle);
            return (Icon)borrowed.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    private static ColorMatrix Greyscale() => new(
    [
        [0.30f, 0.30f, 0.30f, 0, 0],
        [0.59f, 0.59f, 0.59f, 0, 0],
        [0.11f, 0.11f, 0.11f, 0, 0],
        [0, 0, 0, 1, 0],
        [0, 0, 0, 0, 1],
    ]);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyIcon(nint handle);

    public void Dispose()
    {
        Active.Dispose();
        Idle.Dispose();
        Paused.Dispose();
    }
}
