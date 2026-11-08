using System.Text;
using Silk.NET.Core.Native;
using Silk.NET.Shaderc;

namespace RedHoleEngine.Tests.Rendering;

/// <summary>
/// Compiles the temporal denoise shaders with shaderc (the same compiler the Vulkan backend uses).
/// Returns early if the native shaderc library cannot be loaded on this machine.
/// </summary>
public unsafe class TemporalShaderCompileTests
{
    private readonly Xunit.Abstractions.ITestOutputHelper output;

    public TemporalShaderCompileTests(Xunit.Abstractions.ITestOutputHelper output) => this.output = output;

    private static string? FindShader(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "RedHoleEngine", "Rendering", "Shaders", name);
            if (File.Exists(candidate)) return candidate;
            candidate = Path.Combine(dir.FullName, "Rendering", "Shaders", name);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }

    [Theory]
    [InlineData("denoise_svgf_temporal.comp")]
    [InlineData("motion_vectors.comp")]
    public void Shader_CompilesToSpirv(string name)
    {
        var path = FindShader(name);
        Assert.NotNull(path);

        Shaderc shaderc;
        try { shaderc = Shaderc.GetApi(); }
        catch (Exception ex) { output.WriteLine("SHADERC_UNAVAILABLE " + ex.Message); return; }   // native shaderc unavailable

        var compiler = shaderc.CompilerInitialize();
        var options = shaderc.CompileOptionsInitialize();
        var src = Encoding.UTF8.GetBytes(File.ReadAllText(path!));
        var fn = SilkMarshal.StringToPtr(name, NativeStringEncoding.UTF8);
        var ep = SilkMarshal.StringToPtr("main", NativeStringEncoding.UTF8);
        try
        {
            CompilationResult* result;
            fixed (byte* p = src)
                result = shaderc.CompileIntoSpv(compiler, p, (nuint)src.Length, ShaderKind.ComputeShader, (byte*)fn, (byte*)ep, options);
            var status = shaderc.ResultGetCompilationStatus(result);
            var msg = status == CompilationStatus.Success ? "" : shaderc.ResultGetErrorMessageS(result);
            var len = shaderc.ResultGetLength(result);
            shaderc.ResultRelease(result);
            Assert.True(status == CompilationStatus.Success, msg);
            Assert.True(len > 0);
            output.WriteLine("SHADERC_COMPILED " + name + " bytes=" + len);
        }
        finally
        {
            shaderc.CompileOptionsRelease(options);
            shaderc.CompilerRelease(compiler);
            SilkMarshal.Free(fn);
            SilkMarshal.Free(ep);
        }
    }
}
