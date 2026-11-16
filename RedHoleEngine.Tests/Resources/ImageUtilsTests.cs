using System.Text;
using RedHoleEngine.Resources;
using RedHoleEngine.Tests.Rendering;

namespace RedHoleEngine.Tests.Resources;

public class ImageUtilsTests
{
    // 2x1 animated GIF (generated with Pillow): frame 0 = red, blue; frame 1 = green, white.
    private static readonly byte[] TwoFrameGif =
    {
        0x47, 0x49, 0x46, 0x38, 0x39, 0x61, 0x02, 0x00, 0x01, 0x00, 0x81, 0x00, 0x00, 0xFF, 0x00, 0x00,
        0x00, 0x00, 0xFF, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x21, 0xFF, 0x0B, 0x4E, 0x45, 0x54, 0x53,
        0x43, 0x41, 0x50, 0x45, 0x32, 0x2E, 0x30, 0x03, 0x01, 0x00, 0x00, 0x00, 0x21, 0xF9, 0x04, 0x04,
        0x0A, 0x00, 0x00, 0x00, 0x2C, 0x00, 0x00, 0x00, 0x00, 0x02, 0x00, 0x01, 0x00, 0x00, 0x08, 0x05,
        0x00, 0x01, 0x04, 0x08, 0x08, 0x00, 0x21, 0xF9, 0x04, 0x04, 0x0A, 0x00, 0x00, 0x00, 0x2C, 0x00,
        0x00, 0x00, 0x00, 0x02, 0x00, 0x01, 0x00, 0x81, 0xFF, 0xFF, 0xFF, 0x00, 0xFF, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x08, 0x05, 0x00, 0x03, 0x00, 0x08, 0x08, 0x00, 0x3B,
    };

    [Fact]
    public void LoadRgba8_DecodesInMemoryPng_TopRowFirst()
    {
        // 2x2: top row red, green; bottom row blue, half-transparent white.
        var rgba = new byte[]
        {
            255, 0, 0, 255,   0, 255, 0, 255,
            0, 0, 255, 255,   255, 255, 255, 128,
        };
        var png = CoveragePng.Encode(2, 2, rgba);

        var image = ImageUtils.LoadRgba8(png);

        Assert.Equal(2, image.Width);
        Assert.Equal(2, image.Height);
        Assert.Equal(rgba, image.Rgba);
    }

    [Fact]
    public void LoadRgba8_RejectsGarbage()
    {
        Assert.Throws<InvalidDataException>(() => ImageUtils.LoadRgba8(Encoding.ASCII.GetBytes("not an image")));
    }

    [Theory]
    [InlineData(new byte[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F', 0, 0, 0, 0, (byte)'W', (byte)'E', (byte)'B', (byte)'P' }, "WebP")]
    [InlineData(new byte[] { (byte)'I', (byte)'I', (byte)'*', 0 }, "TIFF")]
    [InlineData(new byte[] { (byte)'M', (byte)'M', 0, (byte)'*' }, "TIFF")]
    [InlineData(new byte[] { (byte)'q', (byte)'o', (byte)'i', (byte)'f' }, "QOI")]
    [InlineData(new byte[] { (byte)'P', (byte)'1', (byte)'\n' }, "PBM/PNM (P1)")]
    public void UnsupportedFormats_AreDetectedAndRejected(byte[] header, string expected)
    {
        Assert.Equal(expected, ImageUtils.DetectUnsupportedFormat(header));
        var ex = Assert.Throws<NotSupportedException>(() => ImageUtils.LoadRgba8(header, "asset.bin"));
        Assert.Contains(expected, ex.Message);
        Assert.Contains("asset.bin", ex.Message);
    }

    [Fact]
    public void DetectUnsupportedFormat_IgnoresSupportedFormats()
    {
        Assert.Null(ImageUtils.DetectUnsupportedFormat(CoveragePng.Encode(1, 1, new byte[] { 1, 2, 3, 4 })));
        Assert.Null(ImageUtils.DetectUnsupportedFormat(TwoFrameGif));
        Assert.Null(ImageUtils.DetectUnsupportedFormat(Encoding.ASCII.GetBytes("P6\n1 1\n255\n")));
        Assert.Null(ImageUtils.DetectUnsupportedFormat(ReadOnlySpan<byte>.Empty));
    }

    [Fact]
    public void LoadFramesRgba8_DecodesEveryGifFrame()
    {
        var frames = ImageUtils.LoadFramesRgba8(TwoFrameGif);

        Assert.Equal(2, frames.Count);
        Assert.All(frames, f => Assert.Equal((2, 1), (f.Width, f.Height)));
        Assert.Equal(new byte[] { 255, 0, 0, 255, 0, 0, 255, 255 }, frames[0].Rgba);
        Assert.Equal(new byte[] { 0, 255, 0, 255, 255, 255, 255, 255 }, frames[1].Rgba);
        Assert.NotSame(frames[0].Rgba, frames[1].Rgba);
    }

    [Fact]
    public void LoadFramesRgba8_TreatsNonGifAsSingleFrame()
    {
        var rgba = new byte[] { 9, 8, 7, 255 };
        var frames = ImageUtils.LoadFramesRgba8(CoveragePng.Encode(1, 1, rgba));
        Assert.Single(frames);
        Assert.Equal(rgba, frames[0].Rgba);
    }

    [Fact]
    public void LoadRgbFloat_DecodesRadianceHdr()
    {
        // 2x1 uncompressed RGBE: (128,64,32 | e=129) -> (1, 0.5, 0.25); (128,128,128 | e=128) -> 0.5.
        var header = Encoding.ASCII.GetBytes("#?RADIANCE\nFORMAT=32-bit_rle_rgbe\n\n-Y 1 +X 2\n");
        var hdr = header.Concat(new byte[] { 128, 64, 32, 129, 128, 128, 128, 128 }).ToArray();

        var image = ImageUtils.LoadRgbFloat(hdr);

        Assert.Equal(2, image.Width);
        Assert.Equal(1, image.Height);
        Assert.Equal(6, image.Rgb.Length);
        var expected = new[] { 1f, 0.5f, 0.25f, 0.5f, 0.5f, 0.5f };
        for (int i = 0; i < expected.Length; i++)
            Assert.Equal(expected[i], image.Rgb[i], precision: 4);
    }

    [Fact]
    public void ResizeRgba8_SameSize_ReturnsCopy()
    {
        var rgba = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        var result = ImageUtils.ResizeRgba8(rgba, 2, 1, 2, 1);
        Assert.Equal(rgba, result);
        Assert.NotSame(rgba, result);
    }

    [Fact]
    public void ResizeRgba8_Downscale_IsBoxAverage()
    {
        // 4x2 -> 2x1: each output pixel averages a 2x2 block.
        var rgba = new byte[]
        {
            0, 0, 0, 255,     100, 0, 0, 255,   10, 20, 30, 255,  10, 20, 30, 255,
            100, 0, 0, 255,   200, 0, 0, 255,   10, 20, 30, 255,  50, 60, 70, 255,
        };

        var result = ImageUtils.ResizeRgba8(rgba, 4, 2, 2, 1);

        Assert.Equal(new byte[] { 100, 0, 0, 255, 20, 30, 40, 255 }, result);
    }

    [Fact]
    public void ResizeRgba8_NonIntegerDownscale_UsesAreaWeights()
    {
        // 3x1 -> 2x1: out0 = (p0 + 0.5 p1) / 1.5, out1 = (0.5 p1 + p2) / 1.5.
        var rgba = new byte[] { 0, 0, 0, 255, 150, 0, 0, 255, 255, 0, 0, 255 };
        var result = ImageUtils.ResizeRgba8(rgba, 3, 1, 2, 1);
        Assert.Equal(50, result[0]);
        Assert.Equal(220, result[4]);
        Assert.Equal(255, result[3]);
        Assert.Equal(255, result[7]);
    }

    [Fact]
    public void ResizeRgba8_Upscale_IsBilinearWithEdgeClamp()
    {
        var rgba = new byte[] { 0, 0, 0, 255, 255, 255, 255, 255 };
        var result = ImageUtils.ResizeRgba8(rgba, 2, 1, 4, 1);
        Assert.Equal(new byte[] { 0, 64, 191, 255 }, new[] { result[0], result[4], result[8], result[12] });
        Assert.All(new[] { result[3], result[7], result[11], result[15] }, a => Assert.Equal(255, a));
    }

    [Fact]
    public void ResizeRgba8_UsesPremultipliedAlpha()
    {
        // Opaque red next to fully transparent green: the green must not bleed into the result.
        var rgba = new byte[] { 255, 0, 0, 255, 0, 255, 0, 0 };
        var result = ImageUtils.ResizeRgba8(rgba, 2, 1, 1, 1);
        Assert.Equal(new byte[] { 255, 0, 0, 128 }, result);

        var transparent = ImageUtils.ResizeRgba8(new byte[] { 10, 20, 30, 0, 40, 50, 60, 0 }, 2, 1, 1, 1);
        Assert.Equal(new byte[] { 0, 0, 0, 0 }, transparent);
    }

    [Fact]
    public void ResizeRgba8_SolidColour_StaysSolid()
    {
        var rgba = new byte[7 * 5 * 4];
        for (int i = 0; i < 7 * 5; i++)
        {
            rgba[i * 4] = 12;
            rgba[i * 4 + 1] = 200;
            rgba[i * 4 + 2] = 77;
            rgba[i * 4 + 3] = 255;
        }

        foreach (var (w, h) in new[] { (3, 2), (1, 1), (11, 9), (7, 2) })
        {
            var result = ImageUtils.ResizeRgba8(rgba, 7, 5, w, h);
            Assert.Equal(w * h * 4, result.Length);
            for (int i = 0; i < w * h; i++)
                Assert.Equal(new byte[] { 12, 200, 77, 255 }, result[(i * 4)..(i * 4 + 4)]);
        }
    }

    [Fact]
    public void ResizeRgba8_ValidatesArguments()
    {
        var rgba = new byte[4];
        Assert.Throws<ArgumentOutOfRangeException>(() => ImageUtils.ResizeRgba8(rgba, 0, 1, 1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => ImageUtils.ResizeRgba8(rgba, 1, 1, 0, 1));
        Assert.Throws<ArgumentException>(() => ImageUtils.ResizeRgba8(rgba, 2, 1, 1, 1));
    }

    [Theory]
    [InlineData(4, 2)]
    [InlineData(3, 2)]
    [InlineData(2, 5)]
    [InlineData(7, 7)]
    public void ComputeWeights_SumToOne(int src, int dst)
    {
        foreach (var sample in ImageUtils.ComputeWeights(src, dst))
        {
            Assert.Equal(1f, sample.Sum(w => w.Weight), precision: 5);
            Assert.All(sample, w => Assert.InRange(w.Index, 0, src - 1));
        }
    }
}
