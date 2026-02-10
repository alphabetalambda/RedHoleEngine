using System;
using System.Numerics;
using System.Numerics;
using RedHoleEngine.Engine;
using RedHoleEngine.Physics;
using Silk.NET.OpenGL;

namespace RedHoleEngine.Rendering;

public class Renderer : IDisposable
{
    public RaytracerSettings RaytracerSettings { get; } = new();

    private readonly GL _gl;
    private int _width;
    private int _height;

    // Compute shader for raytracing
    private uint _computeProgram;
    private uint _outputTexture;
    private uint _accumTexture;

    private uint _bvhBuffer;
    private uint _triBuffer;

    private uint _frameIndex;
    private Vector3 _lastCameraPos;
    private Vector3 _lastCameraForward;
    private float _lastCameraFov;
    private int _lastSettingsHash;

    // Display shader (renders texture to screen)
    private uint _displayProgram;
    private uint _quadVao;
    private uint _quadVbo;

    public Renderer(GL gl, int width, int height)
    {
        _gl = gl;
        _width = width;
        _height = height;

        InitializeComputeShader();
        InitializeDisplayShader();
        InitializeOutputTexture();
        InitializeQuad();
        InitializeRaytracerBuffers();
    }

    public uint OutputTextureId => _outputTexture;
    public int Width => _width;
    public int Height => _height;

    public void Resize(int width, int height)
    {
        if (width <= 0 || height <= 0)
            return;

        if (_width == width && _height == height)
            return;

        _width = width;
        _height = height;

        _gl.DeleteTexture(_outputTexture);
        _gl.DeleteTexture(_accumTexture);

        InitializeOutputTexture();

        _frameIndex = 0;
        RaytracerSettings.ResetAccumulation = true;
    }

    private void InitializeComputeShader()
    {
        string shaderSource = LoadShaderSource("Rendering/Shaders/raytracer.comp");
        uint shader = CompileShader(ShaderType.ComputeShader, shaderSource);

        _computeProgram = _gl.CreateProgram();
        _gl.AttachShader(_computeProgram, shader);
        _gl.LinkProgram(_computeProgram);

        // Check for linking errors
        _gl.GetProgram(_computeProgram, ProgramPropertyARB.LinkStatus, out int status);
        if (status == 0)
        {
            string infoLog = _gl.GetProgramInfoLog(_computeProgram);
            throw new Exception($"Compute program linking failed: {infoLog}");
        }

        _gl.DeleteShader(shader);
    }

    private void InitializeDisplayShader()
    {
        string vertSource = LoadShaderSource("Rendering/Shaders/display.vert");
        string fragSource = LoadShaderSource("Rendering/Shaders/display.frag");

        uint vertShader = CompileShader(ShaderType.VertexShader, vertSource);
        uint fragShader = CompileShader(ShaderType.FragmentShader, fragSource);

        _displayProgram = _gl.CreateProgram();
        _gl.AttachShader(_displayProgram, vertShader);
        _gl.AttachShader(_displayProgram, fragShader);
        _gl.LinkProgram(_displayProgram);

        _gl.GetProgram(_displayProgram, ProgramPropertyARB.LinkStatus, out int status);
        if (status == 0)
        {
            string infoLog = _gl.GetProgramInfoLog(_displayProgram);
            throw new Exception($"Display program linking failed: {infoLog}");
        }

        _gl.DeleteShader(vertShader);
        _gl.DeleteShader(fragShader);
    }

    private void InitializeOutputTexture()
    {
        _outputTexture = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, _outputTexture);
        
        // Create empty RGBA32F texture
        _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba32f, 
            (uint)_width, (uint)_height, 0, PixelFormat.Rgba, PixelType.Float, ReadOnlySpan<float>.Empty);
        
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);

        _accumTexture = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, _accumTexture);
        _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba32f,
            (uint)_width, (uint)_height, 0, PixelFormat.Rgba, PixelType.Float, ReadOnlySpan<float>.Empty);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);
    }

    private void InitializeRaytracerBuffers()
    {
        _bvhBuffer = _gl.GenBuffer();
        _gl.BindBuffer(BufferTargetARB.ShaderStorageBuffer, _bvhBuffer);
        _gl.BufferData(BufferTargetARB.ShaderStorageBuffer, (nuint)64, IntPtr.Zero, BufferUsageARB.DynamicDraw);

        _triBuffer = _gl.GenBuffer();
        _gl.BindBuffer(BufferTargetARB.ShaderStorageBuffer, _triBuffer);
        _gl.BufferData(BufferTargetARB.ShaderStorageBuffer, (nuint)64, IntPtr.Zero, BufferUsageARB.DynamicDraw);

        _gl.BindBuffer(BufferTargetARB.ShaderStorageBuffer, 0);
    }

    private void InitializeQuad()
    {
        // Full-screen quad vertices (position + texcoords)
        float[] vertices = {
            // Position     // TexCoords
            -1.0f,  1.0f,   0.0f, 1.0f,
            -1.0f, -1.0f,   0.0f, 0.0f,
             1.0f, -1.0f,   1.0f, 0.0f,
            
            -1.0f,  1.0f,   0.0f, 1.0f,
             1.0f, -1.0f,   1.0f, 0.0f,
             1.0f,  1.0f,   1.0f, 1.0f
        };

        _quadVao = _gl.GenVertexArray();
        _quadVbo = _gl.GenBuffer();

        _gl.BindVertexArray(_quadVao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _quadVbo);

        unsafe
        {
            fixed (float* v = vertices)
            {
                _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(vertices.Length * sizeof(float)), v, BufferUsageARB.StaticDraw);
            }
        }

        // Position attribute
        unsafe
        {
            _gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), (void*)0);
        }
        _gl.EnableVertexAttribArray(0);

        // TexCoord attribute
        unsafe
        {
            _gl.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), (void*)(2 * sizeof(float)));
        }
        _gl.EnableVertexAttribArray(1);
    }

    private uint CompileShader(ShaderType type, string source)
    {
        uint shader = _gl.CreateShader(type);
        _gl.ShaderSource(shader, source);
        _gl.CompileShader(shader);

        _gl.GetShader(shader, ShaderParameterName.CompileStatus, out int status);
        if (status == 0)
        {
            string infoLog = _gl.GetShaderInfoLog(shader);
            throw new Exception($"Shader compilation failed ({type}): {infoLog}");
        }

        return shader;
    }

    private string LoadShaderSource(string relativePath)
    {
        // Get the directory where the executable is located
        string basePath = AppContext.BaseDirectory;
        
        // Try multiple possible locations
        string[] possiblePaths = {
            Path.Combine(basePath, relativePath),
            Path.Combine(basePath, "..", "..", "..", relativePath),
            relativePath
        };

        foreach (string path in possiblePaths)
        {
            if (File.Exists(path))
            {
                return File.ReadAllText(path);
            }
        }

        throw new FileNotFoundException($"Shader file not found: {relativePath}");
    }

    /// <summary>
    /// Render a frame using the raytracer
    /// </summary>
    public void Render(Camera camera, BlackHole? blackHole, float time)
    {
        RenderToTexture(camera, blackHole, time);
        DrawFullscreenQuad();
    }

    public void RenderToTexture(Camera camera, BlackHole? blackHole, float time)
    {
        // Step 1: Run compute shader to raytrace
        _gl.UseProgram(_computeProgram);

        // Bind output texture as image
        _gl.BindImageTexture(0, _outputTexture, 0, false, 0, BufferAccessARB.WriteOnly, InternalFormat.Rgba32f);

        // Bind accumulation texture
        _gl.BindImageTexture(3, _accumTexture, 0, false, 0, BufferAccessARB.ReadWrite, InternalFormat.Rgba32f);

        // Bind dummy BVH buffers (raytracer uses zero counts in this backend)
        _gl.BindBufferBase(BufferTargetARB.ShaderStorageBuffer, 1, _bvhBuffer);
        _gl.BindBufferBase(BufferTargetARB.ShaderStorageBuffer, 2, _triBuffer);

        // Set uniforms
        SetUniform(_computeProgram, "u_Resolution", new Vector2(_width, _height));
        SetUniform(_computeProgram, "u_Time", time);
        SetUniform(_computeProgram, "u_CameraPos", camera.Position);
        SetUniform(_computeProgram, "u_CameraForward", camera.Forward);
        SetUniform(_computeProgram, "u_CameraRight", camera.Right);
        SetUniform(_computeProgram, "u_CameraUp", camera.Up);
        SetUniform(_computeProgram, "u_Fov", camera.FieldOfView);

        RaytracerSettings.Clamp();
        UpdateAccumulationState(camera);
        SetUniform(_computeProgram, "u_RaySettings", new Vector4(
            RaytracerSettings.RaysPerPixel,
            RaytracerSettings.MaxBounces,
            0f,
            0f));
        SetUniform(_computeProgram, "u_FrameSettings", new Vector4(
            RaytracerSettings.SamplesPerFrame,
            _frameIndex,
            RaytracerSettings.Accumulate ? 1f : 0f,
            RaytracerSettings.Denoise ? 1f : 0f));
        
        // Black hole parameters - all zero when no black hole (disables lensing)
        SetUniform(_computeProgram, "u_BlackHolePos", blackHole?.Position ?? Vector3.Zero);
        SetUniform(_computeProgram, "u_BlackHoleMass", blackHole?.Mass ?? 0f);
        SetUniform(_computeProgram, "u_SchwarzschildRadius", blackHole?.SchwarzschildRadius ?? 0f);
        SetUniform(_computeProgram, "u_DiskInnerRadius", blackHole?.DiskInnerRadius ?? 0f);
        SetUniform(_computeProgram, "u_DiskOuterRadius", blackHole?.DiskOuterRadius ?? 0f);

        // Dispatch compute shader (16x16 workgroups)
        uint groupsX = (uint)Math.Ceiling(_width / 16.0);
        uint groupsY = (uint)Math.Ceiling(_height / 16.0);
        _gl.DispatchCompute(groupsX, groupsY, 1);

        // Wait for compute shader to finish
        _gl.MemoryBarrier(MemoryBarrierMask.ShaderImageAccessBarrierBit);

        if (RaytracerSettings.Accumulate)
        {
            _frameIndex++;
        }
        else
        {
            _frameIndex = 0;
        }
    }

    private void DrawFullscreenQuad()
    {
        _gl.Clear(ClearBufferMask.ColorBufferBit);
        _gl.UseProgram(_displayProgram);

        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindTexture(TextureTarget.Texture2D, _outputTexture);
        SetUniform(_displayProgram, "u_Texture", 0);

        _gl.BindVertexArray(_quadVao);
        _gl.DrawArrays(PrimitiveType.Triangles, 0, 6);
    }

    private void SetUniform(uint program, string name, float value)
    {
        int location = _gl.GetUniformLocation(program, name);
        if (location >= 0) _gl.Uniform1(location, value);
    }

    private void SetUniform(uint program, string name, int value)
    {
        int location = _gl.GetUniformLocation(program, name);
        if (location >= 0) _gl.Uniform1(location, value);
    }

    private void SetUniform(uint program, string name, Vector2 value)
    {
        int location = _gl.GetUniformLocation(program, name);
        if (location >= 0) _gl.Uniform2(location, value.X, value.Y);
    }

    private void SetUniform(uint program, string name, Vector3 value)
    {
        int location = _gl.GetUniformLocation(program, name);
        if (location >= 0) _gl.Uniform3(location, value.X, value.Y, value.Z);
    }

    private void SetUniform(uint program, string name, Vector4 value)
    {
        int location = _gl.GetUniformLocation(program, name);
        if (location >= 0) _gl.Uniform4(location, value.X, value.Y, value.Z, value.W);
    }

    public void Dispose()
    {
        _gl.DeleteProgram(_computeProgram);
        _gl.DeleteProgram(_displayProgram);
        _gl.DeleteTexture(_outputTexture);
        _gl.DeleteTexture(_accumTexture);
        _gl.DeleteVertexArray(_quadVao);
        _gl.DeleteBuffer(_quadVbo);
        _gl.DeleteBuffer(_bvhBuffer);
        _gl.DeleteBuffer(_triBuffer);
    }

    private void UpdateAccumulationState(Camera camera)
    {
        int settingsHash = HashCode.Combine(
            RaytracerSettings.RaysPerPixel,
            RaytracerSettings.MaxBounces,
            RaytracerSettings.SamplesPerFrame,
            RaytracerSettings.Accumulate,
            RaytracerSettings.Denoise);

        bool cameraChanged = camera.Position != _lastCameraPos ||
                             camera.Forward != _lastCameraForward ||
                             Math.Abs(camera.FieldOfView - _lastCameraFov) > 0.001f;

        if (RaytracerSettings.ResetAccumulation || cameraChanged || settingsHash != _lastSettingsHash || !RaytracerSettings.Accumulate)
        {
            _frameIndex = 0;
            RaytracerSettings.ResetAccumulation = false;
        }

        _lastCameraPos = camera.Position;
        _lastCameraForward = camera.Forward;
        _lastCameraFov = camera.FieldOfView;
        _lastSettingsHash = settingsHash;
    }
}
