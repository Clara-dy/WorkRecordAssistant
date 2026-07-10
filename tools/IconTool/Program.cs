using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

if (args.Length < 2)
{
    Console.WriteLine("Usage: IconTool <input.png> <output.ico>");
    return 1;
}

var pngPath = args[0];
var icoPath = args[1];

using var source = new Bitmap(pngPath);
var sizes = new[] { 16, 32, 48, 64, 128, 256 };
using var stream = File.Create(icoPath);
using var writer = new BinaryWriter(stream);

writer.Write((ushort)0);
writer.Write((ushort)1);
writer.Write((ushort)sizes.Length);

var imageDataList = new List<byte[]>();
foreach (var size in sizes)
{
    using var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
    using (var g = Graphics.FromImage(bmp))
    {
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.SmoothingMode = SmoothingMode.HighQuality;
        g.Clear(Color.Transparent);
        g.DrawImage(source, 0, 0, size, size);
    }

    using var ms = new MemoryStream();
    bmp.Save(ms, ImageFormat.Png);
    imageDataList.Add(ms.ToArray());
}

var offset = 6 + sizes.Length * 16;
foreach (var (size, index) in sizes.Select((s, i) => (s, i)))
{
    writer.Write((byte)(size == 256 ? 0 : size));
    writer.Write((byte)(size == 256 ? 0 : size));
    writer.Write((byte)0);
    writer.Write((byte)0);
    writer.Write((ushort)1);
    writer.Write((ushort)32);
    writer.Write((uint)imageDataList[index].Length);
    writer.Write((uint)offset);
    offset += imageDataList[index].Length;
}

foreach (var data in imageDataList)
    writer.Write(data);

Console.WriteLine($"Created {icoPath} ({sizes.Length} sizes)");
return 0;
