namespace RedHoleEngine.Resources;

/// <summary>
/// Shader stage types
/// </summary>
public enum ShaderStage
{
    Vertex,
    Fragment,
    Compute,
    Geometry,
    TessellationControl,
    TessellationEvaluation
}

/// <summary>
/// Represents a compiled shader
/// </summary>
public class ShaderResource : IDisposable
{
    /// <summary>
    /// Shader stage
    /// </summary>
    public ShaderStage Stage { get; }
    
    /// <summary>
    /// SPIR-V bytecode
    /// </summary>
    public byte[] SpirVCode { get; }
    
    /// <summary>
    /// Original source path
    /// </summary>
    public string SourcePath { get; }
    
    /// <summary>
    /// Entry point name
    /// </summary>
    public string EntryPoint { get; }

    public ShaderResource(ShaderStage stage, byte[] spirvCode, string sourcePath, string entryPoint = "main")
    {
        Stage = stage;
        SpirVCode = spirvCode;
        SourcePath = sourcePath;
        EntryPoint = entryPoint;
    }

    public void Dispose()
    {
        // SPIR-V code is managed, no disposal needed
    }
}

/// <summary>
/// Loads shader files (SPIR-V or GLSL source)
/// </summary>
public class ShaderLoader : ResourceLoader<ShaderResource>
{
    /// <summary>
    /// Whether to compile GLSL to SPIR-V (requires shaderc)
    /// </summary>
    public bool CompileGlsl { get; set; } = false;

    public override ShaderResource? Load(string path)
    {
        if (!File.Exists(path))
        {
            Console.WriteLine($"Shader file not found: {path}");
            return null;
        }

        var extension = Path.GetExtension(path).ToLower();
        var stage = InferStage(path);
        
        if (extension == ".spv")
        {
            // Already compiled SPIR-V
            var code = File.ReadAllBytes(path);
            return new ShaderResource(stage, code, path);
        }
        else if (CompileGlsl)
        {
            // TODO: Compile with shaderc
            throw new NotImplementedException("GLSL compilation not yet implemented");
        }
        else
        {
            // Try to find pre-compiled .spv
            var spvPath = path + ".spv";
            if (File.Exists(spvPath))
            {
                var code = File.ReadAllBytes(spvPath);
                return new ShaderResource(stage, code, path);
            }
            
            Console.WriteLine($"No SPIR-V found for shader: {path}");
            return null;
        }
    }

    private static ShaderStage InferStage(string path)
    {
        var name = Path.GetFileName(path).ToLower();
        
        if (name.Contains(".vert") || name.Contains("_vert"))
            return ShaderStage.Vertex;
        if (name.Contains(".frag") || name.Contains("_frag"))
            return ShaderStage.Fragment;
        if (name.Contains(".comp") || name.Contains("_comp"))
            return ShaderStage.Compute;
        if (name.Contains(".geom") || name.Contains("_geom"))
            return ShaderStage.Geometry;
        if (name.Contains(".tesc"))
            return ShaderStage.TessellationControl;
        if (name.Contains(".tese"))
            return ShaderStage.TessellationEvaluation;
        
        // Default to compute for this engine's focus
        return ShaderStage.Compute;
    }
}
