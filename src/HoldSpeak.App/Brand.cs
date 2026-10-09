using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Controls.Shapes;

namespace HoldSpeak.App;

internal static class Brand
{
    public const string Name = "左键说";
    public const string Tagline = "按住左键，让想法成文。";
    public static Control Mark(double size = 48)
    {
        var drawing = new DrawingGroup();
        drawing.Children.Add(new GeometryDrawing { Brush = Brush.Parse("#1677FF"), Geometry = new RectangleGeometry(new Rect(0, 0, 64, 64), 16, 16) });
        void Shape(string data, string? fill, string? stroke = null, double thickness = 2.5) => drawing.Children.Add(new GeometryDrawing
        {
            Geometry = Geometry.Parse(data), Brush = fill == null ? null : Brush.Parse(fill),
            Pen = stroke == null ? null : new Pen(Brush.Parse(stroke), thickness)
        });
        Shape("M 29 12 C 20 12 15 18 15 27 L 15 38 C 15 48 21 53 29 53 C 37 53 43 48 43 38 L 43 27 C 43 18 38 12 29 12 Z", null, "#FFFFFF");
        var left = new StreamGeometry();
        using (var context = left.Open())
        {
            context.BeginFigure(new Point(28, 15), true);
            context.CubicBezierTo(new Point(21, 15), new Point(18, 20), new Point(18, 27));
            context.LineTo(new Point(18, 30)); context.LineTo(new Point(28, 30)); context.EndFigure(true);
        }
        drawing.Children.Add(new GeometryDrawing { Geometry = left, Brush = Brushes.White });
        Shape("M 29 13 L 29 30 M 16 32 L 42 32", null, "#FFFFFF", 2);
        Shape("M 49 28 L 49 37 M 54 23 L 54 42", null, "#FFFFFF", 3);
        return new Image { Width = size, Height = size, Source = new DrawingImage(drawing) };
    }
    public static WindowIcon Icon()
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize(64, 64));
        var mark = Mark(64); mark.Measure(new Size(64, 64)); mark.Arrange(new Rect(0, 0, 64, 64)); bitmap.Render(mark);
        using var stream = new MemoryStream(); bitmap.Save(stream); stream.Position = 0;
        return new WindowIcon(stream);
    }
    internal static void Export(string directory)
    {
        Directory.CreateDirectory(directory);
        using var bitmap = new RenderTargetBitmap(new PixelSize(256, 256));
        var mark = Mark(256); mark.Measure(new Size(256, 256)); mark.Arrange(new Rect(0, 0, 256, 256)); bitmap.Render(mark);
        bitmap.Save(System.IO.Path.Combine(directory, "leftspeak.png"));
        using var png = new MemoryStream(); bitmap.Save(png);
        using var writer = new BinaryWriter(File.Create(System.IO.Path.Combine(directory, "leftspeak.ico")));
        writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)1);
        writer.Write((byte)0); writer.Write((byte)0); writer.Write((byte)0); writer.Write((byte)0);
        writer.Write((ushort)1); writer.Write((ushort)32); writer.Write((uint)png.Length); writer.Write((uint)22); writer.Write(png.ToArray());
    }
}
