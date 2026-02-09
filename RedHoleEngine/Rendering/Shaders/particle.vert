#version 450

// Per-vertex attributes (from quad vertices)
layout(location = 0) in vec2 inQuadVertex; // -0.5 to 0.5 quad corners

// Per-instance attributes (particle data)
layout(location = 1) in vec3 inPosition;   // World position
layout(location = 2) in float inSize;      // Particle size
layout(location = 3) in vec4 inColor;      // RGBA color
layout(location = 4) in float inRotation;  // Rotation in radians

// Output to fragment shader
layout(location = 0) out vec4 fragColor;
layout(location = 1) out vec2 fragTexCoord;

// Uniform buffer with camera matrices
layout(set = 0, binding = 0) uniform UniformBufferObject {
    mat4 view;
    mat4 projection;
    mat4 viewProjection;
    vec3 cameraPosition;
    float time;
} ubo;

void main() {
    // Calculate billboard vectors (particle always faces camera)
    // Extract right and up vectors from the inverse view matrix
    vec3 cameraRight = vec3(ubo.view[0][0], ubo.view[1][0], ubo.view[2][0]);
    vec3 cameraUp = vec3(ubo.view[0][1], ubo.view[1][1], ubo.view[2][1]);
    
    // Apply rotation to the quad vertex
    float cosR = cos(inRotation);
    float sinR = sin(inRotation);
    vec2 rotatedVertex = vec2(
        inQuadVertex.x * cosR - inQuadVertex.y * sinR,
        inQuadVertex.x * sinR + inQuadVertex.y * cosR
    );
    
    // Calculate world position of this vertex
    vec3 worldPosition = inPosition 
        + cameraRight * rotatedVertex.x * inSize
        + cameraUp * rotatedVertex.y * inSize;
    
    // Transform to clip space
    gl_Position = ubo.viewProjection * vec4(worldPosition, 1.0);
    
    // Pass data to fragment shader
    fragColor = inColor;
    fragTexCoord = inQuadVertex + 0.5; // Convert from -0.5..0.5 to 0..1
}
