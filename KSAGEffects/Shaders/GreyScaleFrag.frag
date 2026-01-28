#version 450 core

layout(location = 0) out vec4 outColor;

/* Post-process source texture
   Must match:
   set = 1, binding = 0
*/
layout(set = 1, binding = 0, input_attachment_index = 0) uniform subpassInput Source;

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

void main()
{
    vec4 c = subpassLoad(Source);

    vec3 color = desaturate(c.rgb, 1); // full grayscale

    outColor = vec4(color, 1);
}
