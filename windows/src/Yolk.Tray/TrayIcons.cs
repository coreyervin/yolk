using System.Drawing.Imaging;

namespace Yolk.Tray;

/// <summary>
/// The three tray icons, drawn once from the app icon: full colour when
/// active, greyscale when idle, faded when paused.
/// </summary>
/// <remarks>
/// The Mac's menu bar glyphs are black template images, which would vanish on
/// a dark taskbar; the coloured egg reads on both.
/// </remarks>
internal sealed class TrayIcons : IDisposable
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
        return ToIcon(bitmap);
    }

    /// <summary>
    /// Wraps the bitmap's PNG encoding in a one-image .ico, which Icon loads
    /// directly. Unlike Bitmap.GetHicon, this leaves no native icon handle to
    /// destroy by hand, so the tray app needs no P/Invoke of its own.
    /// </summary>
    private static Icon ToIcon(Bitmap bitmap)
    {
        using var png = new MemoryStream();
        bitmap.Save(png, ImageFormat.Png);

        using var ico = new MemoryStream();
        using (var writer = new BinaryWriter(ico, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            // ICONDIR: reserved, type 1 (icon), one image.
            writer.Write((short)0);
            writer.Write((short)1);
            writer.Write((short)1);
            // ICONDIRENTRY: a dimension of 0 means 256.
            writer.Write((byte)(bitmap.Width >= 256 ? 0 : bitmap.Width));
            writer.Write((byte)(bitmap.Height >= 256 ? 0 : bitmap.Height));
            writer.Write((byte)0);  // no palette
            writer.Write((byte)0);  // reserved
            writer.Write((short)1); // colour planes
            writer.Write((short)32); // bits per pixel
            writer.Write((int)png.Length);
            writer.Write(6 + 16);   // the image follows the one-entry directory
            png.WriteTo(ico);
        }
        ico.Position = 0;
        return new Icon(ico);
    }

    private static ColorMatrix Greyscale() => new(
    [
        [0.30f, 0.30f, 0.30f, 0, 0],
        [0.59f, 0.59f, 0.59f, 0, 0],
        [0.11f, 0.11f, 0.11f, 0, 0],
        [0, 0, 0, 1, 0],
        [0, 0, 0, 0, 1],
    ]);

    public void Dispose()
    {
        Active.Dispose();
        Idle.Dispose();
        Paused.Dispose();
    }
}
