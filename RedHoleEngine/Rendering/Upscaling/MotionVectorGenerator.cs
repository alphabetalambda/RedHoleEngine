using System.Numerics;
using System.Runtime.InteropServices;

namespace RedHoleEngine.Rendering.Upscaling;

/// <summary>
/// Generates per-pixel motion vectors for temporal upscaling.
/// Motion vectors represent the screen-space movement from previous to current frame.
/// 
/// Two modes:
/// 1. Camera-only: Fast, only accounts for camera movement (good for static scenes)
/// 2. Full: Includes object motion via depth reprojection (requires depth buffer)
/// </summary>
public class MotionVectorGenerator
{
    // Previous frame camera state
    private Matrix4x4 _prevViewMatrix;
    private Matrix4x4 _prevProjectionMatrix;
    private Matrix4x4 _prevViewProjMatrix;
    private Vector3 _prevCameraPosition;
    private Vector2 _prevJitter;
    
    // Current frame state
    private Matrix4x4 _currentViewMatrix;
    private Matrix4x4 _currentProjectionMatrix;
    private Matrix4x4 _currentViewProjMatrix;
    private Vector3 _currentCameraPosition;
    private Vector2 _currentJitter;
    
    // Resolution
    private int _width;
    private int _height;
    
    // Tracking
    private bool _isFirstFrame = true;
    private uint _frameIndex;
    
    /// <summary>
    /// Uniform buffer data for motion vector compute shader
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct MotionVectorUniforms
    {
        // Current frame (64 + 64 + 16 = 144 bytes)
        public Matrix4x4 CurrentViewProj;      // 64 bytes
        public Matrix4x4 CurrentInvViewProj;   // 64 bytes
        public Vector3 CurrentCameraPos;       // 12 bytes
        private float _pad1;                   // 4 bytes
        
        // Previous frame (64 + 64 + 16 = 144 bytes)
        public Matrix4x4 PrevViewProj;         // 64 bytes
        public Matrix4x4 PrevInvViewProj;      // 64 bytes
        public Vector3 PrevCameraPos;          // 12 bytes
        private float _pad2;                   // 4 bytes
        
        // Resolution (16 bytes)
        public Vector2 Resolution;             // 8 bytes
        public Vector2 InvResolution;          // 8 bytes
        
        // Jitter (16 bytes)
        public Vector2 CurrentJitter;          // 8 bytes
        public Vector2 PrevJitter;             // 8 bytes
        
        // Planes (16 bytes)
        public float NearPlane;                // 4 bytes
        public float FarPlane;                 // 4 bytes
        private float _pad3;                   // 4 bytes
        private float _pad4;                   // 4 bytes
        
        // Total: 336 bytes
    }
    
    public MotionVectorGenerator()
    {
        _prevViewMatrix = Matrix4x4.Identity;
        _prevProjectionMatrix = Matrix4x4.Identity;
        _prevViewProjMatrix = Matrix4x4.Identity;
        _currentViewMatrix = Matrix4x4.Identity;
        _currentProjectionMatrix = Matrix4x4.Identity;
        _currentViewProjMatrix = Matrix4x4.Identity;
    }
    
    /// <summary>
    /// Initialize with resolution
    /// </summary>
    public void Initialize(int width, int height)
    {
        _width = width;
        _height = height;
        _isFirstFrame = true;
        _frameIndex = 0;
    }
    
    /// <summary>
    /// Resize the motion vector buffers
    /// </summary>
    public void Resize(int width, int height)
    {
        _width = width;
        _height = height;
        // Reset on resize to avoid artifacts
        _isFirstFrame = true;
    }
    
    /// <summary>
    /// Update camera state for the current frame.
    /// Call this before rendering each frame.
    /// </summary>
    public void UpdateCamera(
        Vector3 cameraPosition,
        Matrix4x4 viewMatrix,
        Matrix4x4 projectionMatrix,
        Vector2 jitterOffset)
    {
        // Store previous frame
        _prevViewMatrix = _currentViewMatrix;
        _prevProjectionMatrix = _currentProjectionMatrix;
        _prevViewProjMatrix = _currentViewProjMatrix;
        _prevCameraPosition = _currentCameraPosition;
        _prevJitter = _currentJitter;
        
        // Update current frame
        _currentViewMatrix = viewMatrix;
        _currentProjectionMatrix = projectionMatrix;
        _currentViewProjMatrix = viewMatrix * projectionMatrix;
        _currentCameraPosition = cameraPosition;
        _currentJitter = jitterOffset;
        
        _frameIndex++;
        
        if (_isFirstFrame)
        {
            // Copy current to previous on first frame
            _prevViewMatrix = _currentViewMatrix;
            _prevProjectionMatrix = _currentProjectionMatrix;
            _prevViewProjMatrix = _currentViewProjMatrix;
            _prevCameraPosition = _currentCameraPosition;
            _prevJitter = _currentJitter;
            _isFirstFrame = false;
        }
    }
    
    /// <summary>
    /// Check if we should reset temporal accumulation (camera cut, teleport, etc.)
    /// </summary>
    public bool ShouldResetAccumulation()
    {
        if (_isFirstFrame) return true;
        
        // Detect large camera movement (likely a cut or teleport)
        float positionDelta = Vector3.Distance(_currentCameraPosition, _prevCameraPosition);
        
        // If camera moved more than 10 units in one frame, likely a teleport
        const float TeleportThreshold = 10.0f;
        if (positionDelta > TeleportThreshold)
        {
            return true;
        }
        
        // Check for large rotation change
        // Extract forward vectors and compare
        Vector3 currentForward = new Vector3(
            _currentViewMatrix.M31,
            _currentViewMatrix.M32,
            _currentViewMatrix.M33
        );
        Vector3 prevForward = new Vector3(
            _prevViewMatrix.M31,
            _prevViewMatrix.M32,
            _prevViewMatrix.M33
        );
        
        float dotProduct = Vector3.Dot(currentForward, prevForward);
        // If rotation more than ~45 degrees, reset
        if (dotProduct < 0.7f)
        {
            return true;
        }
        
        return false;
    }
    
    /// <summary>
    /// Get uniform buffer data for the motion vector compute shader
    /// </summary>
    public MotionVectorUniforms GetUniforms(float nearPlane, float farPlane)
    {
        Matrix4x4.Invert(_currentViewProjMatrix, out var currentInvViewProj);
        Matrix4x4.Invert(_prevViewProjMatrix, out var prevInvViewProj);
        
        return new MotionVectorUniforms
        {
            CurrentViewProj = _currentViewProjMatrix,
            CurrentInvViewProj = currentInvViewProj,
            CurrentCameraPos = _currentCameraPosition,
            PrevViewProj = _prevViewProjMatrix,
            PrevInvViewProj = prevInvViewProj,
            PrevCameraPos = _prevCameraPosition,
            Resolution = new Vector2(_width, _height),
            InvResolution = new Vector2(1.0f / _width, 1.0f / _height),
            CurrentJitter = _currentJitter,
            PrevJitter = _prevJitter,
            NearPlane = nearPlane,
            FarPlane = farPlane
        };
    }
    
    /// <summary>
    /// Calculate motion vector for a single world position (CPU fallback)
    /// </summary>
    public Vector2 CalculateMotionVector(Vector3 worldPosition)
    {
        // Project to current frame
        Vector4 currentClip = Vector4.Transform(new Vector4(worldPosition, 1), _currentViewProjMatrix);
        Vector2 currentNDC = new Vector2(currentClip.X, currentClip.Y) / currentClip.W;
        Vector2 currentUV = currentNDC * 0.5f + new Vector2(0.5f);
        
        // Project to previous frame
        Vector4 prevClip = Vector4.Transform(new Vector4(worldPosition, 1), _prevViewProjMatrix);
        Vector2 prevNDC = new Vector2(prevClip.X, prevClip.Y) / prevClip.W;
        Vector2 prevUV = prevNDC * 0.5f + new Vector2(0.5f);
        
        // Motion in pixels (current - previous)
        Vector2 motion = (currentUV - prevUV) * new Vector2(_width, _height);
        
        // Add jitter difference
        motion += _currentJitter - _prevJitter;
        
        return motion;
    }
    
    /// <summary>
    /// Generate Halton jitter sequence for temporal anti-aliasing
    /// </summary>
    public static Vector2 GetHaltonJitter(uint frameIndex, int phaseCount = 8)
    {
        int phase = (int)(frameIndex % phaseCount);
        float x = HaltonSequence(phase + 1, 2) - 0.5f;
        float y = HaltonSequence(phase + 1, 3) - 0.5f;
        return new Vector2(x, y);
    }
    
    private static float HaltonSequence(int index, int baseValue)
    {
        float result = 0;
        float f = 1.0f / baseValue;
        int i = index;
        
        while (i > 0)
        {
            result += f * (i % baseValue);
            i /= baseValue;
            f /= baseValue;
        }
        
        return result;
    }
    
    /// <summary>
    /// Apply jitter to projection matrix for temporal anti-aliasing
    /// </summary>
    public static Matrix4x4 ApplyJitterToProjection(Matrix4x4 projection, Vector2 jitterPixels, int width, int height)
    {
        // Convert pixel jitter to NDC jitter
        float jitterX = jitterPixels.X * 2.0f / width;
        float jitterY = jitterPixels.Y * 2.0f / height;
        
        // Create jitter translation matrix
        var jitterMatrix = Matrix4x4.CreateTranslation(jitterX, jitterY, 0);
        
        // Apply jitter to projection
        return projection * jitterMatrix;
    }
}
