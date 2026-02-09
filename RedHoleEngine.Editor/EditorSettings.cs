using System.Numerics;

namespace RedHoleEngine.Editor;

public class EditorSettings
{
    public string ViewportBackend { get; set; } = "Vulkan";
    public bool LoadShowcaseOnStart { get; set; } = true;
    public string GameProjectPath { get; set; } = "";
    public bool HasCamera { get; set; }
    public Vector3 CameraPosition { get; set; } = new(0f, 10f, 40f);
    public float CameraYaw { get; set; } = -90f;
    public float CameraPitch { get; set; } = -14f;
}
