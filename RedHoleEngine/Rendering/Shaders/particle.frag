#version 450

// Input from vertex shader
layout(location = 0) in vec4 fragColor;
layout(location = 1) in vec2 fragTexCoord;

// Output color
layout(location = 0) out vec4 outColor;

// Optional texture sampler (binding 1)
// layout(set = 0, binding = 1) uniform sampler2D particleTexture;

void main() {
    // Calculate distance from center for soft circle
    vec2 center = fragTexCoord - 0.5;
    float dist = length(center) * 2.0; // 0 at center, 1 at edge
    
    // Soft circular falloff
    float alpha = 1.0 - smoothstep(0.0, 1.0, dist);
    
    // Apply particle color with soft edges
    outColor = vec4(fragColor.rgb, fragColor.a * alpha);
    
    // Discard fully transparent pixels
    if (outColor.a < 0.01) {
        discard;
    }
}
