#version 450 core

layout(location = 0) out vec4 outColor;

/* Post-process source texture
   Must match:
   set = 1, binding = 0
*/
layout(set = 1, binding = 0) uniform sampler2D u_Source;
layout(set = 1, binding = 1) uniform TestBuffer {
  float v1;
  float v2;
};

/* From ScreenspaceVert */
layout(location = 0) in vec2 v_Uv;

float luminance(vec3 c)
{
    return dot(c, vec3(0.2126, 0.7152, 0.0722));
}

vec3 desaturate(vec3 color, float amount)
{
    float l = luminance(color);
    return mix(color, vec3(l), clamp(amount, 0.0, 1.0));
}

float vignette(vec2 uv, float amount)
{
    // amount: 0 = no vignette, 1 = vignette reaches center
    amount = clamp(amount, 0.0, 1.0);

    vec2 centered = uv - 0.5;
    float dist = length(centered);

    // Max distance from center to corner (≈ 0.707)
    float maxDist = 0.7071;

    // Where vignette starts
    float innerRadius = mix(maxDist, 0.0, amount);

    // Softness of the edge
    float softness = 0.2 * maxDist;

    return smoothstep(innerRadius, innerRadius - softness, dist);
}

void main()
{
    vec4 c = texture(u_Source, v_Uv);

    vec3 color = desaturate(c.rgb, TestBuffer.v1); // full grayscale

    float vig = vignette(v_Uv, TestBuffer.v2);
    vec3 finalColor = color * vig;

    outColor = vec4(finalColor, 1);
}
