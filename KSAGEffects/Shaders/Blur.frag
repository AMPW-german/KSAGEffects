#version 450 core

layout(location = 0) out vec4 outColor;

layout(set = 1, binding = 0) uniform sampler2D u_Source;

// Add this uniform to pass the size of a single texel (pixel)
layout(set = 1, binding = 1) uniform TexelInfo { // You can choose a different binding if needed
    vec2 texelSize; // e.g., vec2(1.0 / textureWidth, 1.0 / textureHeight)
};

layout(location = 0) in vec2 v_Uv;

void main()
{
    vec4 sumColor = vec4(0.0);

    // Define offsets for a 3x3 kernel
    // Each offset is multiplied by texelSize.texelSize to get the actual UV step
    vec2 offsets[9] = vec2[](
        vec2(-1.0, -1.0), vec2(0.0, -1.0), vec2(1.0, -1.0),
        vec2(-1.0,  0.0), vec2(0.0,  0.0), vec2(1.0,  0.0),
        vec2(-1.0,  1.0), vec2(0.0,  1.0), vec2(1.0,  1.0)
    );

    // Sample and sum the colors of the current pixel and its 8 neighbors
    for (int i = 0; i < 9; i++) {
        sumColor += texture(u_Source, v_Uv + offsets[i] * texelSize);
    }

    // Average the colors to get a simple box blur
    outColor = sumColor / 9.0;

    // If you wanted a weighted average (e.g., for a Gaussian blur),
    // you would define weights and apply them here.
    // For example, a simple center-weighted average:
    /*
    vec4 centerColor = texture(u_Source, v_Uv);
    vec4 neighborSum = vec4(0.0);
    neighborSum += texture(u_Source, v_Uv + vec2(-1.0, -1.0) * TexelInfo.texelSize);
    neighborSum += texture(u_Source, v_Uv + vec2( 0.0, -1.0) * TexelInfo.texelSize);
    neighborSum += texture(u_Source, v_Uv + vec2( 1.0, -1.0) * TexelInfo.texelSize);
    neighborSum += texture(u_Source, v_Uv + vec2(-1.0,  0.0) * TexelInfo.texelSize);
    neighborSum += texture(u_Source, v_Uv + vec2( 1.0,  0.0) * TexelInfo.texelSize);
    neighborSum += texture(u_Source, v_Uv + vec2(-1.0,  1.0) * TexelInfo.texelSize);
    neighborSum += texture(u_Source, v_Uv + vec2( 0.0,  1.0) * TexelInfo.texelSize);
    neighborSum += texture(u_Source, v_Uv + vec2( 1.0,  1.0) * TexelInfo.texelSize);

    // Example weights (center pixel has higher weight)
    outColor = (centerColor * 2.0 + neighborSum) / 10.0; // Sum of weights: 2 + 8*1 = 10
    */
}