using Brutal.Numerics;
using ShaderExtensions;
using System.Runtime.InteropServices;

namespace KSAGEffects.Shaders
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    [SxUniformBuffer("GEffectBuffer")]
    public struct GEffectBuffer
    {
        public float GrayScaleLevel;
        public float TunnelVisionLevel;
        public float ScreenSizeAdjustment;
        public float filmGrainLevel;
        public float4 filmGrainData;

        // lookup delegate fields must be static fields on the buffer element type
        // the names and specific types of these are not relevant, as long as the delegate signature matches
        // these are not all required, but you will need at least one to be able to set the uniform data
        [SxUniformBufferLookup] public static SxBufferLookup LookupBuffer;
        [SxUniformBufferLookup] public static SxMemoryLookup LookupMemory;
        [SxUniformBufferLookup] public static SxSpanLookup<GEffectBuffer> LookupSpan; // gives a Span<T> of length Size
        [SxUniformBufferLookup] public static SxPtrLookup<GEffectBuffer> LookupPtr; // gives T* to first element
    }
}

