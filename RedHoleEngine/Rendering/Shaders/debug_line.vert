#version 450

// Vertex attributes
layout(location = 0) in vec3 inPosition;
layout(location = 1) in vec4 inColor;

// Output to fragment shader
layout(location = 0) out vec4 fragColor;

// Uniform buffer with camera matrices
layout(set = 0, binding = 0) uniform UniformBufferObject {
    mat4 view;
    mat4 projection;
    mat4 viewProjection;
    vec3 cameraPosition;
    float time;
} ubo;

void main() {
    gl_Position = ubo.viewProjection * vec4(inPosition, 1.0);
    fragColor = inColor;
}
