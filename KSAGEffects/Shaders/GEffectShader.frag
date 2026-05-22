#version 450 core

layout(location = 0) out vec4 outColor;

/* Post-process source texture
   Must match:
   set = 1, binding = 0
*/
layout(set = 1, binding = 0, input_attachment_index = 0) uniform subpassInput Source;
layout(set = 1, binding = 1) uniform GEffectBuffer {
  float grayScaleLevel;
  float tunnelVisionLevel;
  float screensizeAdjustment;
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

float cinematicVignette(vec2 uv, float amount, float aspectAdjust)
{
    amount = clamp(amount, 0.0, 1.0);

    // Early out avoids tiny residuals at amount=0
    if (amount <= 0.0)
        return 0.0;

    // Centered coordinates
    vec2 p = uv - 0.5;

    // Aspect correction for oval shape
    p.y *= aspectAdjust;

    // Distance from center
    float d = length(p);

    // --------------------------------------------
    // Vignette shape control
    // --------------------------------------------

    // Keep a minimum visible center area.
    // Even at amount=1 the vignette itself
    // never fully closes.
    const float minOuterRadius = 0.15;

    // Outer radius shrinks with amount:
    // starts near corners, then closes inward
    float outerRadius = mix(0.72, minOuterRadius, pow(amount, 1.15));

    // Fade width:
    // very thin initially, broader later
    float fadeWidth = mix(0.015, 0.35, pow(amount, 1.6));

    // Inner radius derived from width
    float innerRadius = max(outerRadius - fadeWidth, 0.0);

    // Smooth vignette
    float vignette = smoothstep(innerRadius, outerRadius, d);

    // --------------------------------------------
    // Additional fullscreen darkening at high amount
    // --------------------------------------------

    // Starts appearing around 0.8
    float globalDarkening =
        smoothstep(0.80, 1.00, amount);

    // Strength curve for softer transition
    globalDarkening *= 0.75;

    // Combine:
    // global darkening lifts the minimum darkness
    float darkness = max(vignette, globalDarkening);

    // Ensure exact full black at amount=1
    darkness = mix(darkness, 1.0, smoothstep(0.98, 1.0, amount));

    return clamp(darkness, 0.0, 1.0);
}

void main()
{
    vec4 c = subpassLoad(Source);

    vec3 color = desaturate(c.rgb, grayScaleLevel); // full grayscale
    vec3 vignetteColor = vec3(0.0); // black vignette
    color = mix(color, vignetteColor, cinematicVignette(v_Uv, tunnelVisionLevel, screensizeAdjustment));

    outColor = vec4(color, 1);
}
