using System.Numerics;
using RedHoleEngine.Components;
using RedHoleEngine.Core.ECS;
using RedHoleEngine.Rendering.UI;

namespace RedHoleEngine.Tests.Rendering;

public class UiDrawDataCoverageTests
{
    [Fact]
    public void AddVertices_MergesSameTexture_AndSplitsOnChange()
    {
        var data = new UiDrawData();
        var quad = Quad(Vector4.One);
        data.AddVertices(1, quad[0], quad[1], quad[2], quad[3], quad[4], quad[5]);
        data.AddVertices(1, quad[0], quad[1], quad[2], quad[3], quad[4], quad[5]);
        data.AddVertices(2, quad[0], quad[1], quad[2], quad[3], quad[4], quad[5]);

        Assert.Equal(18, data.VertexCount);
        Assert.Equal(2, data.Commands.Count);
        Assert.Equal(1, data.Commands[0].TextureId);
        Assert.Equal(0, data.Commands[0].VertexOffset);
        Assert.Equal(12, data.Commands[0].VertexCount);
        Assert.Equal(2, data.Commands[1].TextureId);
        Assert.Equal(12, data.Commands[1].VertexOffset);
        Assert.Equal(6, data.Commands[1].VertexCount);

        data.Textures[1] = new UiTextureFrame(2, 2, new byte[16], 3);
        data.Clear();
        Assert.Equal(0, data.VertexCount);
        Assert.Empty(data.Commands);
        Assert.Empty(data.Textures);
    }

    private static UiVertex[] Quad(Vector4 color)
    {
        return new[]
        {
            new UiVertex { Position = Vector2.Zero, UV = Vector2.Zero, Color = color },
            new UiVertex { Position = Vector2.UnitX, UV = Vector2.UnitX, Color = color },
            new UiVertex { Position = Vector2.One, UV = Vector2.One, Color = color },
            new UiVertex { Position = Vector2.Zero, UV = Vector2.Zero, Color = color },
            new UiVertex { Position = Vector2.One, UV = Vector2.One, Color = color },
            new UiVertex { Position = Vector2.UnitY, UV = Vector2.UnitY, Color = color }
        };
    }
}

public class UiFontAtlasCoverageTests
{
    [Fact]
    public void GlyphUv_ClampsAndPlacesTheWhiteGlyph()
    {
        Assert.Equal(128, UiFontAtlas.AtlasSize);
        Assert.Equal(8, UiFontAtlas.GlyphSize);
        Assert.Equal(16, UiFontAtlas.Grid);
        Assert.Equal(255, UiFontAtlas.WhiteGlyphIndex);

        var first = UiFontAtlas.GetGlyphUv(0);
        Assert.Equal(new Vector4(0f, 0f, 8f / 128f, 8f / 128f), first);
        Assert.Equal(first, UiFontAtlas.GetGlyphUv(-10));

        var last = UiFontAtlas.GetGlyphUv(255);
        Assert.Equal(last, UiFontAtlas.GetGlyphUv(400));
        Assert.Equal(15f * 8f / 128f, last.X, precision: 5);
        Assert.Equal(15f * 8f / 128f, last.Y, precision: 5);
        Assert.Equal(1f, last.Z, precision: 5);
        Assert.Equal(1f, last.W, precision: 5);

        var atlas = UiFontAtlas.BuildAtlas();
        Assert.Equal(128 * 128, atlas.Length);
        int white = 15 * 8 * 128 + 15 * 8;
        Assert.Equal(255, atlas[white]);
        Assert.Equal(255, atlas[white + 7 + 7 * 128]);
        Assert.Equal(0, atlas[0]);
    }
}

public class UiMediaCoverageTests
{
    [Fact]
    public void Registry_AssignsDistinctIds_AndStoresVideoSettings()
    {
        int image = UiMediaRegistry.RegisterImage("ui/icon.png");
        int video = UiMediaRegistry.RegisterVideo("ui/clip.mp4", 24f, false);
        Assert.NotEqual(image, video);

        var imageEntry = UiMediaRegistry.Get(image);
        Assert.NotNull(imageEntry);
        Assert.Equal(UiMediaRegistry.MediaType.Image, imageEntry!.Type);
        Assert.Equal("ui/icon.png", imageEntry.Path);

        var videoEntry = UiMediaRegistry.Get(video);
        Assert.Equal(UiMediaRegistry.MediaType.Video, videoEntry!.Type);
        Assert.Equal(24f, videoEntry.FramesPerSecond);
        Assert.False(videoEntry.Loop);
        Assert.Null(UiMediaRegistry.Get(-50));
    }

    [Fact]
    public void Loader_ReadsPngBase64SequenceAndFallback()
    {
        var dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "uimedia-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var red = new byte[] { 255, 0, 0, 255 };
            var blue = new byte[] { 0, 0, 255, 128 };
            var redPath = CoveragePng.Write(Path.Combine(dir, "b.png"), 1, 1, red);
            var bluePath = CoveragePng.Write(Path.Combine(dir, "a.png"), 1, 1, blue);
            File.WriteAllText(Path.Combine(dir, "notes.txt"), "skip");

            var image = UiMediaLoader.LoadImage(redPath, 4);
            Assert.Equal(1, image.Width);
            Assert.Equal(1, image.Height);
            Assert.Equal(4, image.Version);
            Assert.Equal(red, image.Rgba);

            var encoded = Convert.ToBase64String(File.ReadAllBytes(bluePath));
            var fromBase64 = UiMediaLoader.LoadImageFromBase64(encoded, 9);
            Assert.Equal(blue, fromBase64.Rgba);
            Assert.Equal(9, fromBase64.Version);

            var fallback = UiMediaLoader.LoadImageWithFallback(Path.Combine(dir, "missing.png"), encoded, 2);
            Assert.Equal(blue, fallback.Rgba);
            var magenta = UiMediaLoader.LoadImageWithFallback(Path.Combine(dir, "missing.png"), "", 2);
            Assert.Equal(new byte[] { 255, 0, 255, 255 }, magenta.Rgba);

            var sequence = UiMediaLoader.LoadImageSequence(dir, 10);
            Assert.Equal(2, sequence.Count);
            Assert.Equal(10, sequence[0].Version);
            Assert.Equal(11, sequence[1].Version);
            Assert.Equal(blue, sequence[0].Rgba);
            Assert.Equal(red, sequence[1].Rgba);

            var frames = UiMediaLoader.LoadGifFrames(redPath, 20);
            Assert.Single(frames);
            Assert.Equal(20, frames[0].Version);
            Assert.Equal(red, frames[0].Rgba);

            var empty = Directory.CreateDirectory(Path.Combine(dir, "empty")).FullName;
            Assert.Empty(UiMediaLoader.LoadImageSequence(empty, 0));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}

public class UiSystemCoverageTests
{
    [Fact]
    public void Update_OrdersLayers_AndBuildsTextAndRectQuads()
    {
        var world = new World();
        var ui = world.AddSystem(new UiSystem());
        ui.SetViewportSize(800, 600);
        Assert.Equal(0, ui.DrawData.VertexCount);

        world.AddComponent(world.CreateEntity(), new UiRectComponent(
            new Vector2(10f, 20f), new Vector2(30f, 40f), new Vector4(1f, 0f, 0f, 1f), layer: 2));
        world.AddComponent(world.CreateEntity(), new UiTextComponent(
            new Vector2(1f, 2f), "A\nB", new Vector4(0f, 1f, 0f, 1f), scale: 2f, layer: 0));
        world.AddComponent(world.CreateEntity(), new UiTextComponent(
            Vector2.Zero, "", Vector4.One, layer: 5));

        ui.Update(0.5f);
        Assert.Equal(18, ui.DrawData.VertexCount);
        Assert.Single(ui.DrawData.Commands);
        Assert.Equal(0, ui.DrawData.Commands[0].TextureId);
        Assert.Equal(18, ui.DrawData.Commands[0].VertexCount);

        Assert.Equal(new Vector2(1f, 2f), ui.DrawData.Vertices[0].Position);
        Assert.Equal(new Vector2(17f, 2f), ui.DrawData.Vertices[1].Position);
        Assert.Equal(new Vector2(1f, 18f), ui.DrawData.Vertices[6].Position);
        var glyph = UiFontAtlas.GetGlyphUv('A');
        Assert.Equal(new Vector2(glyph.X, glyph.Y), ui.DrawData.Vertices[0].UV);
        Assert.Equal(new Vector2(10f, 20f), ui.DrawData.Vertices[12].Position);
        Assert.Equal(new Vector2(40f, 60f), ui.DrawData.Vertices[14].Position);
        Assert.Equal(new Vector4(1f, 0f, 0f, 1f), ui.DrawData.Vertices[12].Color);

        ui.Update(0.1f);
        Assert.Equal(18, ui.DrawData.VertexCount);
    }

    [Fact]
    public void Update_LoadsImagesFromFilesAndRegistry()
    {
        var dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "uisys-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var path = CoveragePng.Write(Path.Combine(dir, "icon.png"), 1, 1, new byte[] { 10, 20, 30, 255 });
            int media = UiMediaRegistry.RegisterImage(path);

            var world = new World();
            var ui = world.AddSystem(new UiSystem());
            ui.ResourceBasePath = dir;
            world.AddComponent(world.CreateEntity(), new UiImageComponent(
                new Vector2(4f, 5f), new Vector2(8f, 9f), "icon.png", new Vector4(1f, 1f, 1f, 0.5f)));
            world.AddComponent(world.CreateEntity(), new UiImageComponent(
                Vector2.Zero, Vector2.One, media, Vector4.One, layer: 1));
            world.AddComponent(world.CreateEntity(), new UiImageComponent(
                Vector2.Zero, Vector2.One, "   ", Vector4.One));

            ui.Update(0.016f);

            Assert.Equal(12, ui.DrawData.VertexCount);
            Assert.Single(ui.DrawData.Textures);
            Assert.Equal(new Vector2(4f, 5f), ui.DrawData.Vertices[0].Position);
            Assert.Equal(new Vector2(12f, 14f), ui.DrawData.Vertices[2].Position);
            Assert.Equal(new Vector2(0f, 0f), ui.DrawData.Vertices[0].UV);
            Assert.Equal(new Vector2(1f, 1f), ui.DrawData.Vertices[2].UV);
            var frame = ui.DrawData.Textures.Values.First();
            Assert.Equal(new byte[] { 10, 20, 30, 255 }, frame.Rgba);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Update_SkipsBlankVideoSources()
    {
        var world = new World();
        var ui = world.AddSystem(new UiSystem());
        world.AddComponent(world.CreateEntity(), new UiVideoComponent(Vector2.Zero, Vector2.One, "  "));
        ui.Update(0.2f);
        Assert.Equal(0, ui.DrawData.VertexCount);
    }

    [Fact]
    public void Update_AdvancesImageSequenceFrames()
    {
        var dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "uivid-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            CoveragePng.Write(Path.Combine(dir, "000.png"), 1, 1, new byte[] { 1, 0, 0, 255 });
            CoveragePng.Write(Path.Combine(dir, "001.png"), 1, 1, new byte[] { 0, 2, 0, 255 });

            var world = new World();
            var ui = world.AddSystem(new UiSystem());
            world.AddComponent(world.CreateEntity(), new UiVideoComponent(
                new Vector2(3f, 4f), new Vector2(5f, 6f), dir, framesPerSecond: 10f, loop: true, playing: true));

            ui.Update(0f);
            Assert.Equal(6, ui.DrawData.VertexCount);
            Assert.Equal(new byte[] { 1, 0, 0, 255 }, ui.DrawData.Textures.Values.Single().Rgba);
            Assert.Equal(new Vector2(8f, 10f), ui.DrawData.Vertices[2].Position);

            ui.Update(0.15f);
            Assert.Equal(new byte[] { 0, 2, 0, 255 }, ui.DrawData.Textures.Values.Single().Rgba);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
