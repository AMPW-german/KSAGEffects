using Brutal.Numerics;
using ShaderExtensions;
using System.Runtime.InteropServices;

namespace KSAGEffects.Shaders
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    [SxPushConstant("GEffectBuffer")]
    public struct GEffectBuffer
    {
        public float GrayScaleLevel;
        public float TunnelVisionLevel;
        public float ScreenSizeAdjustment;
        public float FilmGrainLevel;
        public float4 TunnelVisionColor;
        public float4 FilmGrainData;

        // lookup delegate fields must be static fields on the buffer element type
        [SxPushConstantLookup] public static SxSpanLookup<GEffectBuffer> LookupSpan; // gives a Span<T> of length Size
    }
}

