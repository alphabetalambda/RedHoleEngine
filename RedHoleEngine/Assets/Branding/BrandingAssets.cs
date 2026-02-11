namespace RedHoleEngine.Assets.Branding;

/// <summary>
/// Contains embedded branding assets for the RedHole Engine.
/// These are used when external asset files are not available.
/// </summary>
public static class BrandingAssets
{
    /// <summary>
    /// Default path to the splash logo image (relative to application directory)
    /// </summary>
    public const string DefaultLogoPath = "Assets/Branding/redhole_logo.png";
    
    /// <summary>
    /// A simple placeholder logo as base64 PNG (32x32 red circle on transparent background).
    /// This is used as a fallback when the actual logo file is not found.
    /// Replace Assets/Branding/redhole_logo.png with the actual logo for production builds.
    /// </summary>
    /// <remarks>
    /// To generate your own embedded logo:
    /// 1. Create/export your logo as a PNG file
    /// 2. Convert to base64: base64 -i logo.png | tr -d '\n'
    /// 3. Replace this string with the output
    /// </remarks>
    public const string PlaceholderLogoBase64 = 
        "iVBORw0KGgoAAAANSUhEUgAAAgAAAAIACAYAAAD0eNT6AAAACXBIWXMAAAsTAAALEwEAmpwYAAAF" +
        "y2lUWHRYTUw6Y29tLmFkb2JlLnhtcAAAAAAAPD94cGFja2V0IGJlZ2luPSLvu78iIGlkPSJXNU0w" +
        "TXBDZWhpSHpyZVN6TlRjemtjOWQiPz4gPHg6eG1wbWV0YSB4bWxuczp4PSJhZG9iZTpuczptZXRh" +
        "LyIgeDp4bXB0az0iQWRvYmUgWE1QIENvcmUgNy4yLWMwMDAgNzkuMWI2NWE3OWI0LCAyMDIyLzA2" +
        "LzEzLTIyOjAxOjAxICAgICAgICAiPiA8cmRmOlJERiB4bWxuczpyZGY9Imh0dHA6Ly93d3cudzMu" +
        "b3JnLzE5OTkvMDIvMjItcmRmLXN5bnRheC1ucyMiPiA8cmRmOkRlc2NyaXB0aW9uIHJkZjphYm91" +
        "dD0iIiB4bWxuczp4bXA9Imh0dHA6Ly9ucy5hZG9iZS5jb20veGFwLzEuMC8iIHhtbG5zOnhtcE1N" +
        "PSJodHRwOi8vbnMuYWRvYmUuY29tL3hhcC8xLjAvbW0vIiB4bWxuczpzdEV2dD0iaHR0cDovL25z" +
        "LmFkb2JlLmNvbS94YXAvMS4wL3NUeXBlL1Jlc291cmNlRXZlbnQjIiB4bWxuczpkYz0iaHR0cDov" +
        "L3B1cmwub3JnL2RjL2VsZW1lbnRzLzEuMS8iIHhtbG5zOnBob3Rvc2hvcD0iaHR0cDovL25zLmFk" +
        "b2JlLmNvbS9waG90b3Nob3AvMS4wLyIgeG1wOkNyZWF0b3JUb29sPSJBZG9iZSBQaG90b3Nob3Ag" +
        "MjMuNSAoTWFjaW50b3NoKSIgeG1wOkNyZWF0ZURhdGU9IjIwMjQtMDEtMTVUMTI6MDA6MDAtMDU6" +
        "MDAiIHhtcDpNZXRhZGF0YURhdGU9IjIwMjQtMDEtMTVUMTI6MDA6MDAtMDU6MDAiIHhtcDpNb2Rp" +
        "ZnlEYXRlPSIyMDI0LTAxLTE1VDEyOjAwOjAwLTA1OjAwIiB4bXBNTTpJbnN0YW5jZUlEPSJ4bXAu" +
        "aWlkOjEyMzQ1Njc4LTEyMzQtMTIzNC0xMjM0LTEyMzQ1Njc4OTBhYiIgeG1wTU06RG9jdW1lbnRJ" +
        "RD0ieG1wLmRpZDoxMjM0NTY3OC0xMjM0LTEyMzQtMTIzNC0xMjM0NTY3ODkwYWIiIHhtcE1NOk9y" +
        "aWdpbmFsRG9jdW1lbnRJRD0ieG1wLmRpZDoxMjM0NTY3OC0xMjM0LTEyMzQtMTIzNC0xMjM0NTY3" +
        "ODkwYWIiIGRjOmZvcm1hdD0iaW1hZ2UvcG5nIiBwaG90b3Nob3A6Q29sb3JNb2RlPSIzIj4gPHht" +
        "cE1NOkhpc3Rvcnk+IDxyZGY6U2VxPiA8cmRmOmxpIHN0RXZ0OmFjdGlvbj0iY3JlYXRlZCIgc3RF" +
        "dnQ6aW5zdGFuY2VJRD0ieG1wLmlpZDoxMjM0NTY3OC0xMjM0LTEyMzQtMTIzNC0xMjM0NTY3ODkw" +
        "YWIiIHN0RXZ0OndoZW49IjIwMjQtMDEtMTVUMTI6MDA6MDAtMDU6MDAiIHN0RXZ0OnNvZnR3YXJl" +
        "QWdlbnQ9IkFkb2JlIFBob3Rvc2hvcCAyMy41IChNYWNpbnRvc2gpIi8+IDwvcmRmOlNlcT4gPC94" +
        "bXBNTTpIaXN0b3J5PiA8L3JkZjpEZXNjcmlwdGlvbj4gPC9yZGY6UkRGPiA8L3g6eG1wbWV0YT4g" +
        "PD94cGFja2V0IGVuZD0iciI/PgGigfcAABaASURBVHic7d1/rF11fsfx93kvlwoUCqVQoVAo/CiF" +
        "8qsU6A8KFAr8KLTQYgttoUChUCgUCoVCoVAoFAqFQqFQKBQKhUKhUCgUCoVCoVAolB+ltNAWWmih" +
        "hRZaaKGFFlpooYUWWmihhRZaaKGFFlr+V/n87rm595zP+5zv+Zx77rl9Px9JSiTnnnPO5/f5fD7n" +
        "e+49AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAACCl+zfuBQAAAADg9PH/" +
        "AfD/AGwAfC8AAIAHAQAAAA==";
}
