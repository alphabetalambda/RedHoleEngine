using RedHoleEngine.Audio;

namespace RedHoleEngine.Tests.Audio;

public class EfxFilterControllerTests
{
    private sealed class FakeEfx : IEfxApi
    {
        public bool IsSupported { get; set; } = true;
        public uint NextFilter = 100;
        public readonly Dictionary<uint, EfxFilterType> Types = new();
        public readonly Dictionary<(uint, int), float> Floats = new();
        public readonly Dictionary<uint, uint> Attached = new();
        public readonly List<uint> Deleted = new();
        public int Created;

        public uint CreateFilter() { Created++; return NextFilter++; }
        public void DeleteFilter(uint filter) => Deleted.Add(filter);
        public void SetFilterType(uint filter, EfxFilterType type) => Types[filter] = type;
        public void SetFilterFloat(uint filter, int param, float value) => Floats[(filter, param)] = value;
        public void AttachDirectFilter(uint source, uint filter) => Attached[source] = filter;
    }

    [Fact]
    public void Unsupported_IsNoOp()
    {
        var api = new FakeEfx { IsSupported = false };
        var c = new EfxFilterController(api);
        c.SetLowPass(1, 10, 500f);
        c.SetHighPass(1, 10, 500f);
        Assert.False(c.IsSupported);
        Assert.Equal(0, api.Created);
        Assert.Empty(api.Attached);
    }

    [Fact]
    public void OpenFilters_DoNotAllocate()
    {
        var api = new FakeEfx();
        var c = new EfxFilterController(api);
        c.SetLowPass(1, 10, 22000f);
        c.SetHighPass(1, 10, 0f);
        Assert.Equal(0, api.Created);
        Assert.Equal(0, c.ActiveFilterCount);
    }

    [Fact]
    public void LowPass_AttachesLowPassFilterWithGainHf()
    {
        var api = new FakeEfx();
        var c = new EfxFilterController(api);
        c.SetLowPass(1, 10, 5000f);
        Assert.Equal(EfxFilterType.LowPass, api.Types[100]);
        Assert.Equal(0.25f, api.Floats[(100, EfxFilterController.LowPassGainHf)], 3);
        Assert.Equal(100u, api.Attached[10]);
    }

    [Fact]
    public void HighPass_AttachesHighPassFilterWithGainLf()
    {
        var api = new FakeEfx();
        var c = new EfxFilterController(api);
        c.SetHighPass(1, 10, 2500f);
        Assert.Equal(EfxFilterType.HighPass, api.Types[100]);
        Assert.Equal(0.5f, api.Floats[(100, EfxFilterController.HighPassGainLf)], 3);
    }

    [Fact]
    public void BothFilters_UseBandPass()
    {
        var api = new FakeEfx();
        var c = new EfxFilterController(api);
        c.SetLowPass(1, 10, 10000f);
        c.SetHighPass(1, 10, 1000f);
        Assert.Equal(1, api.Created);
        Assert.Equal(EfxFilterType.BandPass, api.Types[100]);
        Assert.Equal(0.5f, api.Floats[(100, EfxFilterController.BandPassGainHf)], 3);
        Assert.Equal(0.8f, api.Floats[(100, EfxFilterController.BandPassGainLf)], 3);
    }

    [Fact]
    public void ReopeningFilter_DetachesSource()
    {
        var api = new FakeEfx();
        var c = new EfxFilterController(api);
        c.SetLowPass(1, 10, 1000f);
        c.SetLowPass(1, 10, 20000f);
        Assert.Equal(0u, api.Attached[10]);
    }

    [Fact]
    public void Release_DeletesFilterAndDetaches()
    {
        var api = new FakeEfx();
        var c = new EfxFilterController(api);
        c.SetLowPass(1, 10, 1000f);
        c.SetLowPass(2, 11, 1000f);
        c.Release(1);
        Assert.Contains(100u, api.Deleted);
        Assert.Equal(0u, api.Attached[10]);
        Assert.Equal(1, c.ActiveFilterCount);
        c.ReleaseAll();
        Assert.Equal(0, c.ActiveFilterCount);
        Assert.Contains(101u, api.Deleted);
    }

    [Fact]
    public void FailedAllocation_DegradesGracefully()
    {
        var api = new FakeEfx { NextFilter = 0 };
        var c = new EfxFilterController(api);
        c.SetLowPass(1, 10, 1000f);
        Assert.Empty(api.Attached);
    }
}
