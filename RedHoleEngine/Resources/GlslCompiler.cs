using System.Runtime.InteropServices;
using System.Text;
using Silk.NET.Core.Native;
using Silk.NET.Shaderc;

namespace RedHoleEngine.Resources;

/// <summary>
/// Result of a GLSL to SPIR-V compilation attempt.
/// </summary>
public readonly record struct GlslCompileResult(bool Success, byte[] SpirV, string? Error, bool CompilerUnavailable);

/// <summary>
/// Runtime GLSL to SPIR-V compiler backed by shaderc (the same library the Vulkan backend uses).
/// Never throws: failures (including a missing native library) are reported via the result.
/// </summary>
public static unsafe class GlslCompiler
{
    public static GlslCompileResult Compile(string source, ShaderStage stage, string fileName, string entryPoint = "main")
    {
        Shaderc? api = null;
        Compiler* compiler = null;
        CompileOptions* options = null;
        CompilationResult* result = null;
        nint namePtr = 0, entryPtr = 0;
        try
        {
            api = Shaderc.GetApi();
            compiler = api.CompilerInitialize();
            if (compiler == null)
                return new GlslCompileResult(false, Array.Empty<byte>(), "shaderc is unavailable: compiler initialization failed", true);
            options = api.CompileOptionsInitialize();

            var bytes = Encoding.UTF8.GetBytes(source);
            namePtr = SilkMarshal.StringToPtr(fileName, NativeStringEncoding.UTF8);
            entryPtr = SilkMarshal.StringToPtr(entryPoint, NativeStringEncoding.UTF8);
            fixed (byte* src = bytes)
            {
                result = api.CompileIntoSpv(compiler, src, (nuint)bytes.Length, ToKind(stage),
                    (byte*)namePtr, (byte*)entryPtr, options);
            }

            var status = api.ResultGetCompilationStatus(result);
            if (status != CompilationStatus.Success)
            {
                var msg = Marshal.PtrToStringUTF8((nint)api.ResultGetErrorMessage(result)) ?? status.ToString();
                return new GlslCompileResult(false, Array.Empty<byte>(), msg, false);
            }

            var len = (int)api.ResultGetLength(result);
            var spirv = new byte[len];
            Marshal.Copy((nint)api.ResultGetBytes(result), spirv, 0, len);
            return new GlslCompileResult(true, spirv, null, false);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException
                                   or TypeInitializationException or NotSupportedException)
        {
            return new GlslCompileResult(false, Array.Empty<byte>(), $"shaderc is unavailable: {ex.Message}", true);
        }
        finally
        {
            try
            {
                if (api != null)
                {
                    if (result != null) api.ResultRelease(result);
                    if (options != null) api.CompileOptionsRelease(options);
                    if (compiler != null) api.CompilerRelease(compiler);
                }
            }
            catch { /* best effort cleanup */ }
            if (namePtr != 0) SilkMarshal.Free(namePtr);
            if (entryPtr != 0) SilkMarshal.Free(entryPtr);
        }
    }

    private static ShaderKind ToKind(ShaderStage stage) => stage switch
    {
        ShaderStage.Vertex => ShaderKind.VertexShader,
        ShaderStage.Fragment => ShaderKind.FragmentShader,
        ShaderStage.Compute => ShaderKind.ComputeShader,
        ShaderStage.Geometry => ShaderKind.GeometryShader,
        ShaderStage.TessellationControl => ShaderKind.TessControlShader,
        ShaderStage.TessellationEvaluation => ShaderKind.TessEvaluationShader,
        _ => ShaderKind.GlslInferFromSource
    };
}
