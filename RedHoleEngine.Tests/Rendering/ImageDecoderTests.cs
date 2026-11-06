using System.IO.Compression;
using System.Text;
using RedHoleEngine.Rendering;
using RedHoleEngine.Rendering.PBR;
using RedHoleEngine.Rendering.UI;

namespace RedHoleEngine.Tests.Rendering;

public class ImageDecoderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "redhole_img_" + Guid.NewGuid().ToString("N"));

    public ImageDecoderTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    // 2x2 RGBA: row0 = red, green; row1 = blue, half-alpha white
    private static readonly byte[] Pixels2x2 =
    {
        255, 0, 0, 255,   0, 255, 0, 255,
        0, 0, 255, 255,   255, 255, 255, 128,
    };

    private static uint Crc32(byte[] data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (var b in data)
        {
            crc ^= b;
            for (int k = 0; k < 8; k++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
        }
        return ~crc;
    }

    private static void WriteBE(Stream s, uint v)
    {
        s.WriteByte((byte)(v >> 24)); s.WriteByte((byte)(v >> 16)); s.WriteByte((byte)(v >> 8)); s.WriteByte((byte)v);
    }

    private static void Chunk(Stream s, string type, byte[] body)
    {
        WriteBE(s, (uint)body.Length);
        var td = Encoding.ASCII.GetBytes(type).Concat(body).ToArray();
        s.Write(td);
        WriteBE(s, Crc32(td));
    }

    internal static byte[] MakePng(int w, int h, byte[] rgba)
    {
        using var ms = new MemoryStream();
        ms.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        using (var ihdr = new MemoryStream())
        {
            WriteBE(ihdr, (uint)w); WriteBE(ihdr, (uint)h);
            ihdr.Write(new byte[] { 8, 6, 0, 0, 0 }); // 8-bit RGBA
            Chunk(ms, "IHDR", ihdr.ToArray());
        }
        using (var raw = new MemoryStream())
        {
            for (int y = 0; y < h; y++)
            {
                raw.WriteByte(0); // filter none
                raw.Write(rgba, y * w * 4, w * 4);
            }
            using var z = new MemoryStream();
            using (var zs = new ZLibStream(z, CompressionLevel.Optimal, true))
                zs.Write(raw.ToArray());
            Chunk(ms, "IDAT", z.ToArray());
        }
        Chunk(ms, "IEND", Array.Empty<byte>());
        return ms.ToArray();
    }

    private static byte[] MakeGif()
    {
        var b = new List<byte>();
        b.AddRange(Encoding.ASCII.GetBytes("GIF89a"));
        b.AddRange(new byte[] { 2, 0, 1, 0, 0x80, 0, 0 });          // 2x1, 2-entry global palette
        b.AddRange(new byte[] { 255, 0, 0, 0, 0, 255 });             // red, blue
        foreach (var lzw in new[] { new byte[] { 0x44, 0x0A }, new byte[] { 0x0C, 0x0A } })
        {
            b.AddRange(new byte[] { 0x21, 0xF9, 4, 0x00, 10, 0, 0, 0 }); // GCE, 100ms delay
            b.AddRange(new byte[] { 0x2C, 0, 0, 0, 0, 2, 0, 1, 0, 0 });  // image descriptor
            b.Add(2); b.Add((byte)lzw.Length); b.AddRange(lzw); b.Add(0);
        }
        b.Add(0x3B);
        return b.ToArray();
    }

    // 2x1 Radiance RGBE (flat, uncompressed): (1.0,0.5,0.25) and black
    private static byte[] MakeHdr()
    {
        var header = Encoding.ASCII.GetBytes("#?RADIANCE\nFORMAT=32-bit_rle_rgbe\n\n-Y 1 +X 2\n");
        return header.Concat(new byte[] { 128, 64, 32, 129, 0, 0, 0, 0 }).ToArray();
    }

    [Fact]
    public void LoadRgba_Png_PreservesLayoutAndRowOrder()
    {
        var img = ImageDecoder.LoadRgba(MakePng(2, 2, Pixels2x2));
        Assert.Equal(2, img.Width);
        Assert.Equal(2, img.Height);
        Assert.Equal(Pixels2x2, img.Pixels);
    }

    [Fact]
    public void UiMediaLoader_LoadImage_FileAndBase64()
    {
        var png = MakePng(2, 2, Pixels2x2);
        var path = Path.Combine(_dir, "t.png");
        File.WriteAllBytes(path, png);

        var fromFile = UiMediaLoader.LoadImage(path, 3);
        var fromB64 = UiMediaLoader.LoadImageFromBase64(Convert.ToBase64String(png), 4);
        Assert.Equal(2, fromFile.Width);
        Assert.Equal(Pixels2x2, fromFile.Rgba);
        Assert.Equal(Pixels2x2, fromB64.Rgba);
    }

    [Fact]
    public void UiMediaLoader_LoadGifFrames_ReturnsTwoFrames()
    {
        var path = Path.Combine(_dir, "t.gif");
        File.WriteAllBytes(path, MakeGif());

        var frames = UiMediaLoader.LoadGifFrames(path, 10);
        Assert.Equal(2, frames.Count);
        Assert.Equal(2, frames[0].Width);
        Assert.Equal(1, frames[0].Height);
        Assert.Equal(new byte[] { 255, 0, 0, 255, 0, 0, 255, 255 }, frames[0].Rgba);
        Assert.Equal(new byte[] { 0, 0, 255, 255, 255, 0, 0, 255 }, frames[1].Rgba);
    }

    [Fact]
    public void EnvironmentMap_LoadsRadianceHdr()
    {
        var path = Path.Combine(_dir, "t.hdr");
        File.WriteAllBytes(path, MakeHdr());

        var env = new EnvironmentMap();
        Assert.True(env.Load(path));
        Assert.Equal(2, env.Width);
        Assert.Equal(1, env.Height);
        Assert.Equal(1.0f, env.HdrData[0], 3);
        Assert.Equal(0.5f, env.HdrData[1], 3);
        Assert.Equal(0.25f, env.HdrData[2], 3);
        Assert.Equal(0f, env.HdrData[3], 3);
    }

    [Fact]
    public void EnvironmentMap_StandardImage_ConvertsSrgbToLinear()
    {
        var path = Path.Combine(_dir, "e.png");
        File.WriteAllBytes(path, MakePng(2, 2, Pixels2x2));

        var env = new EnvironmentMap();
        Assert.True(env.Load(path));
        Assert.Equal(12, env.HdrData.Length);
        Assert.Equal(1f, env.HdrData[0], 3); // red channel of first pixel
        Assert.Equal(0f, env.HdrData[1], 3);
    }

    [Fact]
    public void TextureLibrary_DownscalesOversizedTexture()
    {
        var rgba = new byte[8 * 4 * 4];
        for (int i = 0; i < rgba.Length; i += 4) { rgba[i] = 200; rgba[i + 1] = 100; rgba[i + 2] = 50; rgba[i + 3] = 255; }
        var path = Path.Combine(_dir, "big.png");
        File.WriteAllBytes(path, MakePng(8, 4, rgba));

        using var lib = new TextureLibrary { MaxTextureSize = 4 };
        var tex = lib.LoadTexture(path, PbrTextureType.BaseColor);
        Assert.NotNull(tex);
        Assert.Equal(4, tex!.Width);
        Assert.Equal(2, tex.Height);
        Assert.Equal(4 * 2 * 4, tex.PixelData.Length);
        Assert.Equal(new byte[] { 200, 100, 50, 255 }, tex.PixelData.Take(4).ToArray());
    }

    [Fact]
    public void Resize_BoxAveragesWhenDownscaling_AndKeepsSizeWhenUnchanged()
    {
        var src = new ImageDecoder.Rgba8(2, 1, new byte[] { 0, 0, 0, 255, 100, 200, 50, 255 });
        var half = ImageDecoder.Resize(src, 1, 1);
        Assert.Equal(new byte[] { 50, 100, 25, 255 }, half.Pixels);
        Assert.Same(src.Pixels, ImageDecoder.Resize(src, 2, 1).Pixels);

        var up = ImageDecoder.Resize(src, 4, 2);
        Assert.Equal(4 * 2 * 4, up.Pixels.Length);
        Assert.Equal(0, up.Pixels[0]);
        Assert.Equal(100, up.Pixels[3 * 4]);
    }
}
