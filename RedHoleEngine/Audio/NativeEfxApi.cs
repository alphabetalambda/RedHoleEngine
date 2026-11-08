using Silk.NET.OpenAL;

namespace RedHoleEngine.Audio;

/// <summary>
/// ALC_EXT_EFX filter entry points resolved at runtime via alGetProcAddress.
/// If the extension or any entry point is missing, <see cref="IsSupported"/> is false.
/// </summary>
internal sealed unsafe class NativeEfxApi : IEfxApi
{
    private const int AlFilterType = 0x8001;
    private const int AlDirectFilter = 0x20005;

    private readonly AL _al;
    private readonly delegate* unmanaged[Cdecl]<int, uint*, void> _genFilters;
    private readonly delegate* unmanaged[Cdecl]<int, uint*, void> _deleteFilters;
    private readonly delegate* unmanaged[Cdecl]<uint, int, int, void> _filteri;
    private readonly delegate* unmanaged[Cdecl]<uint, int, float, void> _filterf;

    public bool IsSupported { get; }

    public NativeEfxApi(AL al, ALContext alc, Device* device)
    {
        _al = al;
        try
        {
            if (!alc.IsExtensionPresent(device, "ALC_EXT_EFX")) return;

            _genFilters = (delegate* unmanaged[Cdecl]<int, uint*, void>)al.GetProcAddress("alGenFilters");
            _deleteFilters = (delegate* unmanaged[Cdecl]<int, uint*, void>)al.GetProcAddress("alDeleteFilters");
            _filteri = (delegate* unmanaged[Cdecl]<uint, int, int, void>)al.GetProcAddress("alFilteri");
            _filterf = (delegate* unmanaged[Cdecl]<uint, int, float, void>)al.GetProcAddress("alFilterf");

            IsSupported = _genFilters != null && _deleteFilters != null && _filteri != null && _filterf != null;
        }
        catch (Exception)
        {
            IsSupported = false;
        }
    }

    public uint CreateFilter()
    {
        uint f = 0;
        _genFilters(1, &f);
        return _al.GetError() == AudioError.NoError ? f : 0;
    }

    public void DeleteFilter(uint filter)
    {
        _deleteFilters(1, &filter);
    }

    public void SetFilterType(uint filter, EfxFilterType type) => _filteri(filter, AlFilterType, (int)type);

    public void SetFilterFloat(uint filter, int param, float value) => _filterf(filter, param, value);

    public void AttachDirectFilter(uint source, uint filter) =>
        _al.SetSourceProperty(source, (SourceInteger)AlDirectFilter, (int)filter);
}
