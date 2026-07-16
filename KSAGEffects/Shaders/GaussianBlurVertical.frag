#version 450 core

layout(location = 0) out vec4 outColor;

layout(set = 1, binding = 0) uniform sampler2D Source;

layout(location = 0) in vec2 Uv;

layout(push_constant, std430) uniform BlurParams {
  int radius;
  float weights[21];
} blur;

void main()
{
  vec4 color = texture(Source, Uv) * blur.weights[0];
  vec2 texelSize = 1.0 / vec2(textureSize(Source, 0));

  vec2 minUv = 0.5 * texelSize;
  vec2 maxUv = 1.0 - minUv;

  for (int i = 1; i <= blur.radius; i++) {
    vec2 offset = vec2(0.0, float(i) * texelSize.y);

    color += texture(Source, clamp(Uv + offset, minUv, maxUv)) * blur.weights[i];
    color += texture(Source, clamp(Uv - offset, minUv, maxUv)) * blur.weights[i];
  }

  outColor = color;
}
