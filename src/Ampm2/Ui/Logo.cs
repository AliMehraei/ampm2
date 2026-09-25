using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Ampm2.Ui;

/// <summary>The ampm2 mark, drawn in code: a gradient tile with three "worker" rows.</summary>
public static class Logo
{
    public enum Badge { None, Ok, Warn, Error }

    public static DrawingGroup Drawing(Badge badge = Badge.None)
    {
        var g = new DrawingGroup();
        using (var dc = g.Open())
        {
            var grad = new LinearGradientBrush(Color.FromRgb(0x63, 0x66, 0xF1), Color.FromRgb(0xA8, 0x55, 0xF7), new Point(0, 0), new Point(1, 1));
            dc.DrawRoundedRectangle(grad, null, new Rect(2, 2, 60, 60), 15, 15);
            var white = new SolidColorBrush(Color.FromArgb(0xF2, 0xFF, 0xFF, 0xFF));
            var mint = new SolidColorBrush(Color.FromRgb(0x5E, 0xF0, 0xAE));
            double[] widths = { 26, 18, 22 };
            for (int i = 0; i < 3; i++)
            {
                double y = 17 + i * 12;
                dc.DrawEllipse(i == 1 ? white : mint, null, new Point(19, y + 3), 4, 4);
                dc.DrawRoundedRectangle(white, null, new Rect(27, y, widths[i], 6), 3, 3);
            }
            if (badge != Badge.None)
            {
                var c = badge switch
                {
                    Badge.Error => Color.FromRgb(0xFF, 0x4D, 0x4F),
                    Badge.Warn => Color.FromRgb(0xF5, 0xB8, 0x3D),
                    _ => Color.FromRgb(0x3D, 0xD6, 0x8C),
                };
                dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(0x10, 0x12, 0x18)), null, new Point(50, 50), 14, 14);
                dc.DrawEllipse(new SolidColorBrush(c), null, new Point(50, 50), 10.5, 10.5);
            }
        }
        g.Freeze();
        return g;
    }

    public static ImageSource Image(Badge badge = Badge.None)
    {
        var img = new DrawingImage(Drawing(badge));
        img.Freeze();
        return img;
    }

    public static byte[] Png(int size, Badge badge = Badge.None)
    {
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.PushTransform(new ScaleTransform(size / 64.0, size / 64.0));
            dc.DrawDrawing(Drawing(badge));
            dc.Pop();
        }
        var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }

    [DllImport("user32.dll")]
    private static extern IntPtr CreateIconFromResourceEx(byte[] data, int size, bool icon, int ver, int cx, int cy, uint flags);

    /// <summary>HICON for the tray (caller owns it; destroy with DestroyIcon).</summary>
    public static IntPtr HIcon(int size, Badge badge)
    {
        var png = Png(size, badge);
        return CreateIconFromResourceEx(png, png.Length, true, 0x00030000, size, size, 0);
    }

    /// <summary>Writes a multi-size PNG-compressed .ico (used to generate Assets\ampm2.ico).</summary>
    public static void WriteIco(string path)
    {
        int[] sizes = { 16, 20, 24, 32, 40, 48, 64, 128, 256 };
        var images = new List<byte[]>();
        foreach (var s in sizes) images.Add(Png(s));
        using var fs = File.Create(path);
        using var w = new BinaryWriter(fs);
        w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
        int offset = 6 + 16 * sizes.Length;
        for (int i = 0; i < sizes.Length; i++)
        {
            w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
            w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
            w.Write((byte)0); w.Write((byte)0);
            w.Write((short)1); w.Write((short)32);
            w.Write(images[i].Length);
            w.Write(offset);
            offset += images[i].Length;
        }
        foreach (var img in images) w.Write(img);
    }
}
