using System;
using System.IO;
using System.Numerics;
using ImGuiNET;
using RedHoleEngine.Rendering.PBR;

namespace RedHoleEngine.Editor.UI.Panels;

/// <summary>
/// Panel for environment map and lighting settings
/// </summary>
public class EnvironmentPanel : EditorPanel
{
    private EnvironmentMap? _environmentMap;
    private string _envMapPath = string.Empty;
    private float _intensity = 1.0f;
    private float _rotation;
    private int _selectedPreset = -1;
    private bool _isDirty;
    
    // File dialog for HDR loading
    private readonly FileDialog _envFileDialog = new();
    
    // Callbacks
    private readonly Action<EnvironmentMap?>? _onEnvironmentChanged;
    
    private static readonly string[] PresetNames = {
        "Procedural Sky",
        "Studio Light",
        "Sunset",
        "Night Sky",
        "Overcast"
    };
    
    public override string Title => "Environment";
    
    /// <summary>
    /// The current environment map
    /// </summary>
    public EnvironmentMap? EnvironmentMap => _environmentMap;
    
    public EnvironmentPanel(Action<EnvironmentMap?>? onEnvironmentChanged = null)
    {
        _onEnvironmentChanged = onEnvironmentChanged;
        
        // Create default procedural sky
        _environmentMap = EnvironmentMap.CreateProceduralSky();
        _selectedPreset = 0;
    }
    
    protected override void OnDraw()
    {
        // Current environment info
        if (_environmentMap != null && _environmentMap.IsLoaded)
        {
            ImGui.TextColored(new Vector4(0.5f, 0.8f, 0.5f, 1f), "Active");
            ImGui.SameLine();
            ImGui.Text($"({_environmentMap.Width}x{_environmentMap.Height})");
            
            if (!string.IsNullOrEmpty(_environmentMap.FilePath))
            {
                ImGui.TextDisabled(Path.GetFileName(_environmentMap.FilePath));
            }
        }
        else
        {
            ImGui.TextDisabled("No environment loaded");
        }
        
        ImGui.Separator();
        
        // Presets
        if (ImGui.CollapsingHeader("Presets", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.PushItemWidth(-1);
            if (ImGui.Combo("##Preset", ref _selectedPreset, PresetNames, PresetNames.Length))
            {
                ApplyPreset(_selectedPreset);
            }
            ImGui.PopItemWidth();
            
            ImGui.Spacing();
            
            // Quick preset buttons
            if (ImGui.Button("Day", new Vector2(60, 0)))
            {
                ApplyPreset(0);
                _selectedPreset = 0;
            }
            ImGui.SameLine();
            if (ImGui.Button("Studio", new Vector2(60, 0)))
            {
                ApplyPreset(1);
                _selectedPreset = 1;
            }
            ImGui.SameLine();
            if (ImGui.Button("Sunset", new Vector2(60, 0)))
            {
                ApplyPreset(2);
                _selectedPreset = 2;
            }
            ImGui.SameLine();
            if (ImGui.Button("Night", new Vector2(60, 0)))
            {
                ApplyPreset(3);
                _selectedPreset = 3;
            }
        }
        
        // Load from file
        if (ImGui.CollapsingHeader("Load HDR File"))
        {
            ImGui.Text("Path:");
            ImGui.SetNextItemWidth(-140);
            if (ImGui.InputText("##EnvPath", ref _envMapPath, 512))
            {
                // Path changed
            }
            ImGui.SameLine();
            if (ImGui.Button("Browse", new Vector2(65, 0)))
            {
                var startDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                _envFileDialog.Open(
                    FileDialogMode.Open,
                    "Environment Map",
                    startDir,
                    "HDR Files (*.hdr)|*.hdr|All Images|*.png|*.jpg|*.hdr",
                    "");
            }
            ImGui.SameLine();
            if (ImGui.Button("Load", new Vector2(60, 0)))
            {
                LoadEnvironmentMap(_envMapPath);
            }
            
            ImGui.TextDisabled("Supported: .hdr, .png, .jpg");
            
            // Recent files (placeholder)
            ImGui.Spacing();
            ImGui.TextDisabled("Drop HDR file here or enter path above");
        }
        
        // Draw environment file dialog
        DrawEnvFileDialog();
        
        // Settings
        if (ImGui.CollapsingHeader("Settings", ImGuiTreeNodeFlags.DefaultOpen))
        {
            // Intensity
            if (ImGui.SliderFloat("Intensity", ref _intensity, 0.0f, 5.0f, "%.2f"))
            {
                if (_environmentMap != null)
                {
                    _environmentMap.Intensity = _intensity;
                    _isDirty = true;
                    NotifyChanged();
                }
            }
            
            // Rotation
            float rotationDeg = _rotation * 180f / MathF.PI;
            if (ImGui.SliderFloat("Rotation", ref rotationDeg, -180f, 180f, "%.0f deg"))
            {
                _rotation = rotationDeg * MathF.PI / 180f;
                if (_environmentMap != null)
                {
                    _environmentMap.Rotation = _rotation;
                    _isDirty = true;
                    NotifyChanged();
                }
            }
            
            // Reset button
            if (ImGui.Button("Reset Settings"))
            {
                _intensity = 1.0f;
                _rotation = 0f;
                if (_environmentMap != null)
                {
                    _environmentMap.Intensity = _intensity;
                    _environmentMap.Rotation = _rotation;
                    _isDirty = true;
                    NotifyChanged();
                }
            }
        }
        
        // Info
        if (ImGui.CollapsingHeader("Info"))
        {
            ImGui.TextDisabled("Environment maps provide:");
            ImGui.BulletText("Sky background");
            ImGui.BulletText("Ambient diffuse lighting");
            ImGui.BulletText("Specular reflections (IBL)");
            
            ImGui.Spacing();
            ImGui.TextDisabled("For best results, use HDR");
            ImGui.TextDisabled("equirectangular images.");
        }
    }
    
    private void ApplyPreset(int presetIndex)
    {
        _environmentMap?.Dispose();
        
        switch (presetIndex)
        {
            case 0: // Procedural Sky (Day)
                _environmentMap = CreateDaySky();
                break;
            case 1: // Studio Light
                _environmentMap = CreateStudioEnvironment();
                break;
            case 2: // Sunset
                _environmentMap = CreateSunsetSky();
                break;
            case 3: // Night Sky
                _environmentMap = CreateNightSky();
                break;
            case 4: // Overcast
                _environmentMap = CreateOvercastSky();
                break;
            default:
                _environmentMap = EnvironmentMap.CreateProceduralSky();
                break;
        }
        
        if (_environmentMap != null)
        {
            _environmentMap.Intensity = _intensity;
            _environmentMap.Rotation = _rotation;
        }
        
        _isDirty = true;
        NotifyChanged();
    }
    
    private void LoadEnvironmentMap(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            Console.WriteLine("No path specified");
            return;
        }
        
        if (!File.Exists(path))
        {
            Console.WriteLine($"File not found: {path}");
            return;
        }
        
        var newEnvMap = new EnvironmentMap();
        if (newEnvMap.Load(path))
        {
            _environmentMap?.Dispose();
            _environmentMap = newEnvMap;
            _environmentMap.Intensity = _intensity;
            _environmentMap.Rotation = _rotation;
            _selectedPreset = -1;
            _isDirty = true;
            NotifyChanged();
            Console.WriteLine($"Loaded environment map: {path}");
        }
        else
        {
            newEnvMap.Dispose();
            Console.WriteLine($"Failed to load environment map: {path}");
        }
    }
    
    private void NotifyChanged()
    {
        _onEnvironmentChanged?.Invoke(_environmentMap);
    }
    
    private void DrawEnvFileDialog()
    {
        var result = _envFileDialog.Draw();
        
        if (result == FileDialogResult.Ok)
        {
            _envMapPath = _envFileDialog.SelectedPath;
            LoadEnvironmentMap(_envMapPath);
        }
    }
    
    // Procedural sky generators
    
    private static EnvironmentMap CreateDaySky()
    {
        return EnvironmentMap.CreateProceduralSky(512, 256);
    }
    
    private static EnvironmentMap CreateStudioEnvironment()
    {
        int width = 512, height = 256;
        var env = new EnvironmentMap();
        
        // Access private fields via reflection or create a factory method
        // For now, create a simple gradient studio environment
        var data = new float[width * height * 3];
        
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float u = (float)x / (width - 1);
                float v = (float)y / (height - 1);
                
                // Convert to direction
                float theta = u * 2f * MathF.PI;
                float phi = v * MathF.PI;
                
                Vector3 dir = new Vector3(
                    MathF.Sin(phi) * MathF.Cos(theta),
                    MathF.Cos(phi),
                    MathF.Sin(phi) * MathF.Sin(theta)
                );
                
                // Studio: soft gradient from top to bottom
                float t = dir.Y * 0.5f + 0.5f;
                Vector3 color = Vector3.Lerp(
                    new Vector3(0.2f, 0.2f, 0.22f),  // Floor
                    new Vector3(0.8f, 0.82f, 0.85f), // Ceiling
                    t * t
                );
                
                // Add soft area lights
                // Top light
                float topLight = MathF.Max(0, dir.Y);
                color += new Vector3(0.5f, 0.5f, 0.5f) * topLight * topLight * 0.5f;
                
                // Side rim lights
                float sideLight = MathF.Abs(dir.X) * (1f - MathF.Abs(dir.Y));
                color += new Vector3(0.3f, 0.35f, 0.4f) * sideLight * 0.3f;
                
                int idx = (y * width + x) * 3;
                data[idx] = color.X;
                data[idx + 1] = color.Y;
                data[idx + 2] = color.Z;
            }
        }
        
        // Use reflection to set the data (or add a constructor)
        SetEnvironmentData(env, width, height, data, "[Studio]");
        return env;
    }
    
    private static EnvironmentMap CreateSunsetSky()
    {
        int width = 512, height = 256;
        var data = new float[width * height * 3];
        
        Vector3 sunDir = Vector3.Normalize(new Vector3(0.3f, 0.1f, -0.8f));
        
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float u = (float)x / (width - 1);
                float v = (float)y / (height - 1);
                
                float theta = u * 2f * MathF.PI;
                float phi = v * MathF.PI;
                
                Vector3 dir = new Vector3(
                    MathF.Sin(phi) * MathF.Cos(theta),
                    MathF.Cos(phi),
                    MathF.Sin(phi) * MathF.Sin(theta)
                );
                
                // Sunset colors
                Vector3 horizonColor = new Vector3(1.0f, 0.4f, 0.1f);
                Vector3 skyColor = new Vector3(0.1f, 0.15f, 0.4f);
                Vector3 groundColor = new Vector3(0.05f, 0.03f, 0.02f);
                
                Vector3 color;
                if (dir.Y < 0)
                {
                    color = Vector3.Lerp(horizonColor * 0.3f, groundColor, -dir.Y);
                }
                else if (dir.Y < 0.3f)
                {
                    float t = dir.Y / 0.3f;
                    color = Vector3.Lerp(horizonColor, skyColor, t);
                }
                else
                {
                    color = skyColor;
                }
                
                // Sun
                float sunDot = Vector3.Dot(dir, sunDir);
                if (sunDot > 0.99f)
                {
                    color += new Vector3(30f, 20f, 5f);
                }
                else if (sunDot > 0.9f)
                {
                    float glow = (sunDot - 0.9f) / 0.09f;
                    color += new Vector3(5f, 2f, 0.5f) * glow * glow;
                }
                else if (sunDot > 0.5f)
                {
                    float scatter = (sunDot - 0.5f) / 0.4f;
                    color += new Vector3(0.5f, 0.2f, 0.05f) * scatter;
                }
                
                int idx = (y * width + x) * 3;
                data[idx] = color.X;
                data[idx + 1] = color.Y;
                data[idx + 2] = color.Z;
            }
        }
        
        var env = new EnvironmentMap();
        SetEnvironmentData(env, width, height, data, "[Sunset]");
        return env;
    }
    
    private static EnvironmentMap CreateNightSky()
    {
        int width = 512, height = 256;
        var data = new float[width * height * 3];
        var random = new Random(42);
        
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float u = (float)x / (width - 1);
                float v = (float)y / (height - 1);
                
                float theta = u * 2f * MathF.PI;
                float phi = v * MathF.PI;
                
                Vector3 dir = new Vector3(
                    MathF.Sin(phi) * MathF.Cos(theta),
                    MathF.Cos(phi),
                    MathF.Sin(phi) * MathF.Sin(theta)
                );
                
                // Dark blue night sky
                Vector3 skyColor = new Vector3(0.01f, 0.015f, 0.03f);
                Vector3 horizonColor = new Vector3(0.02f, 0.025f, 0.04f);
                Vector3 groundColor = new Vector3(0.005f, 0.005f, 0.008f);
                
                Vector3 color;
                if (dir.Y < 0)
                {
                    color = Vector3.Lerp(horizonColor, groundColor, -dir.Y);
                }
                else
                {
                    color = Vector3.Lerp(horizonColor, skyColor, dir.Y);
                }
                
                // Stars (based on pixel position hash)
                float hash = Hash(x + y * width);
                if (hash > 0.997f && dir.Y > 0)
                {
                    float brightness = (hash - 0.997f) * 300f;
                    color += new Vector3(brightness);
                }
                
                // Moon
                Vector3 moonDir = Vector3.Normalize(new Vector3(-0.5f, 0.6f, 0.3f));
                float moonDot = Vector3.Dot(dir, moonDir);
                if (moonDot > 0.998f)
                {
                    color += new Vector3(2f, 2f, 1.8f);
                }
                else if (moonDot > 0.99f)
                {
                    float glow = (moonDot - 0.99f) / 0.008f;
                    color += new Vector3(0.3f, 0.3f, 0.25f) * glow;
                }
                
                int idx = (y * width + x) * 3;
                data[idx] = color.X;
                data[idx + 1] = color.Y;
                data[idx + 2] = color.Z;
            }
        }
        
        var env = new EnvironmentMap();
        SetEnvironmentData(env, width, height, data, "[Night]");
        return env;
    }
    
    private static EnvironmentMap CreateOvercastSky()
    {
        int width = 512, height = 256;
        var data = new float[width * height * 3];
        
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float u = (float)x / (width - 1);
                float v = (float)y / (height - 1);
                
                float theta = u * 2f * MathF.PI;
                float phi = v * MathF.PI;
                
                Vector3 dir = new Vector3(
                    MathF.Sin(phi) * MathF.Cos(theta),
                    MathF.Cos(phi),
                    MathF.Sin(phi) * MathF.Sin(theta)
                );
                
                // Overcast: uniform grey with slight variation
                Vector3 skyColor = new Vector3(0.5f, 0.52f, 0.55f);
                Vector3 horizonColor = new Vector3(0.6f, 0.62f, 0.65f);
                Vector3 groundColor = new Vector3(0.15f, 0.14f, 0.13f);
                
                Vector3 color;
                if (dir.Y < 0)
                {
                    color = Vector3.Lerp(horizonColor * 0.7f, groundColor, -dir.Y);
                }
                else
                {
                    float t = MathF.Pow(dir.Y, 0.5f);
                    color = Vector3.Lerp(horizonColor, skyColor, t);
                }
                
                // Add subtle noise for cloud variation
                float noise = Hash(x + y * width + 12345) * 0.1f - 0.05f;
                color += new Vector3(noise);
                
                int idx = (y * width + x) * 3;
                data[idx] = Math.Max(0, color.X);
                data[idx + 1] = Math.Max(0, color.Y);
                data[idx + 2] = Math.Max(0, color.Z);
            }
        }
        
        var env = new EnvironmentMap();
        SetEnvironmentData(env, width, height, data, "[Overcast]");
        return env;
    }
    
    private static float Hash(int n)
    {
        n = (n << 13) ^ n;
        return 1.0f - ((n * (n * n * 15731 + 789221) + 1376312589) & 0x7fffffff) / 1073741824.0f;
    }
    
    // Helper to set environment data using reflection (since fields are private)
    private static void SetEnvironmentData(EnvironmentMap env, int width, int height, float[] data, string name)
    {
        var type = typeof(EnvironmentMap);
        
        var widthField = type.GetField("_width", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var heightField = type.GetField("_height", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var dataField = type.GetField("_hdrData", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var pathProp = type.GetProperty("FilePath");
        
        widthField?.SetValue(env, width);
        heightField?.SetValue(env, height);
        dataField?.SetValue(env, data);
        pathProp?.SetValue(env, name);
    }
}
