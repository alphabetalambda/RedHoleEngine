using System.Numerics;
using RedHoleEngine.Components;
using RedHoleEngine.Core;
using RedHoleEngine.Core.ECS;

namespace RedHoleEngine.Tests.Core;

public class SplashScreenTests
{
    [Fact]
    public void BeforeShow_IsIdleAndIgnoresUpdateAndSkip()
    {
        using var world = new World();
        var splash = new SplashScreen(world, 200, 100);

        Assert.False(splash.IsActive);
        Assert.False(splash.IsComplete);
        Assert.Equal("Assets/Branding/redhole_logo.png", splash.LogoPath);

        splash.Update(1f);
        splash.Skip();

        Assert.False(splash.IsActive);
        Assert.False(splash.IsComplete);
        Assert.Equal(0, world.EntityCount);
    }

    [Fact]
    public void Show_CreatesLayeredBackgroundLogoAndFade()
    {
        using var world = new World();
        var splash = CreateSplash(world);
        splash.Show();
        splash.Show();

        Assert.True(splash.IsActive);
        Assert.False(splash.IsComplete);
        Assert.Equal(3, world.EntityCount);

        var background = Rect(world, 1000);
        var fade = Rect(world, 1002);
        ref var logo = ref Logo(world);

        Assert.Equal(Vector2.Zero, background.Position);
        Assert.Equal(new Vector2(200f, 100f), background.Size);
        Assert.Equal(new Vector4(0.1f, 0.2f, 0.3f, 1f), background.Color);
        Assert.Equal(new Vector2(75f, 25f), logo.Position);
        Assert.Equal(new Vector2(50f, 50f), logo.Size);
        Assert.Equal("logo.png", logo.SourcePath);
        Assert.Equal(new Vector4(1f, 1f, 1f, 0f), logo.Tint);
        Assert.Equal(1001, logo.Layer);
        Assert.Equal(new Vector4(0f, 0f, 0f, 0f), fade.Color);
        Assert.Equal(1002, fade.Layer);
    }

    [Fact]
    public void Update_FadesInHoldsThenFadesOutAndDestroysEntities()
    {
        using var world = new World();
        var splash = CreateSplash(world);
        splash.Show();

        splash.Update(0.25f);
        Assert.Equal(new Vector4(1f, 1f, 1f, 0.25f), Logo(world).Tint);
        Assert.True(splash.IsActive);
        Assert.Equal(new Vector4(0f, 0f, 0f, 0f), Rect(world, 1002).Color);

        splash.Update(0.75f);
        Assert.Equal(new Vector4(1f, 1f, 1f, 1f), Logo(world).Tint);
        Assert.False(splash.IsComplete);

        splash.Update(0.4f);
        Assert.Equal(new Vector4(1f, 1f, 1f, 1f), Logo(world).Tint);
        Assert.Equal(new Vector4(0f, 0f, 0f, 0f), Rect(world, 1002).Color);

        splash.Update(0.6f);
        splash.Update(0.5f);
        Assert.Equal(new Vector4(0f, 0f, 0f, 0.5f), Rect(world, 1002).Color);
        Assert.True(splash.IsActive);

        splash.Update(0.5f);

        Assert.True(splash.IsComplete);
        Assert.False(splash.IsActive);
        Assert.Equal(0, world.EntityCount);
        Assert.Empty(world.Query<UiImageComponent>());
        Assert.Empty(world.Query<UiRectComponent>());

        splash.Update(1f);
        splash.Skip();
        splash.Show();
        Assert.True(splash.IsComplete);
        Assert.Equal(0, world.EntityCount);
    }

    [Fact]
    public void Update_OnePhasePerCall_DoesNotSkipAhead()
    {
        using var world = new World();
        var splash = CreateSplash(world);
        splash.Show();

        splash.Update(10f);

        Assert.True(splash.IsActive);
        Assert.False(splash.IsComplete);
        Assert.Equal(new Vector4(1f, 1f, 1f, 1f), Logo(world).Tint);
        Assert.Equal(new Vector4(0f, 0f, 0f, 0f), Rect(world, 1002).Color);
    }

    [Fact]
    public void Skip_DuringDisplay_CompletesAndCleansUp()
    {
        using var world = new World();
        var splash = CreateSplash(world);
        splash.Show();
        splash.Update(1f);

        splash.Skip();

        Assert.True(splash.IsComplete);
        Assert.False(splash.IsActive);
        Assert.Equal(0, world.EntityCount);

        splash.Skip();
        splash.Update(1f);
        Assert.Equal(0, world.EntityCount);
    }

    [Fact]
    public void Update_ZeroDeltaDuringFadeIn_HoldsTransparentLogo()
    {
        using var world = new World();
        var splash = CreateSplash(world);
        splash.Show();

        splash.Update(0f);

        Assert.True(splash.IsActive);
        Assert.Equal(new Vector4(1f, 1f, 1f, 0f), Logo(world).Tint);
    }

    private static SplashScreen CreateSplash(World world)
    {
        return new SplashScreen(world, 200, 100)
        {
            LogoScale = 0.5f,
            DisplayDuration = 1f,
            FadeInDuration = 1f,
            FadeOutDuration = 1f,
            BackgroundColor = new Vector4(0.1f, 0.2f, 0.3f, 1f),
            LogoPath = "logo.png"
        };
    }

    private static UiRectComponent Rect(World world, int layer)
    {
        foreach (var entity in world.Query<UiRectComponent>())
        {
            var rect = world.GetComponent<UiRectComponent>(entity);
            if (rect.Layer == layer)
                return rect;
        }

        throw new InvalidOperationException($"No rect on layer {layer}");
    }

    private static ref UiImageComponent Logo(World world)
    {
        var entity = Assert.Single(world.Query<UiImageComponent>());
        return ref world.GetComponent<UiImageComponent>(entity);
    }
}
