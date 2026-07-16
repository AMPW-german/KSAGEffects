using ShaderExtensions;
using System.Runtime.InteropServices;

namespace KSAGEffects.Shaders
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    [SxPushConstant("GEffectsBlurPushConstantsBufferAsset")]
    public unsafe struct GEffectsBlurPushConstantsBuffer
    {
        public int Radius;
        public fixed float Weights[21];

        // lookup delegate fields must be static fields on the buffer element type
        [SxPushConstantLookup] public static SxSpanLookup<GEffectsBlurPushConstantsBuffer> LookupSpan;
    }
}
