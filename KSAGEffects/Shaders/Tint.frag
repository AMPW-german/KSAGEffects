#version 450 core

layout(location = 0) out vec4 outColor;

/* Post-process source texture
   Must match:
   set = 1, binding = 0
*/
layout(set = 1, binding = 0) uniform sampler2D u_Source;

/* From ScreenspaceVert */
layout(location = 0) in vec2 v_Uv;

void main()
{
    vec4 c = texture(u_Source, v_Uv);

    float amount = 0.25;
    vec3 tintColor = vec3(1, 0, 0.75);

    outColor = vec4(mix(c.rgb, vec3(tintColor), clamp(amount, 0.0, 1.0)), 1);
}
