using Brutal.VulkanApi.Abstractions;
using KSA;
using KSAGEffects;
using System;
using System.Numerics;
using System.Runtime.InteropServices;
using ShaderExtensions;

namespace ShaderExtensions
{
    [AttributeUsage(AttributeTargets.Struct)]
    internal class SxUniformBufferAttribute(string xmlElement) : Attribute;


    [AttributeUsage(AttributeTargets.Field)]
    internal class SxUniformBufferLookupAttribute() : Attribute;

    public delegate BufferEx SxBufferLookup(KeyHash hash);
    public delegate MappedMemory SxMemoryLookup(KeyHash hash);
    public delegate Span<T> SxSpanLookup<T>(KeyHash hash) where T : unmanaged;
    public unsafe delegate T* SxPtrLookup<T>(KeyHash hash) where T : unmanaged;
}


namespace KSAGEffects
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    [SxUniformBuffer("GEffectBuffer")]
    public struct GEffectBuffer
    {
        public float V1;
        public float V2;

        // lookup delegate fields must be static fields on the buffer element type
        // the names and specific types of these are not relevant, as long as the delegate signature matches
        // these are not all required, but you will need at least one to be able to set the uniform data
        [SxUniformBufferLookup] public static SxBufferLookup LookupBuffer;
        [SxUniformBufferLookup] public static SxMemoryLookup LookupMemory;
        [SxUniformBufferLookup] public static SxSpanLookup<GEffectBuffer> LookupSpan; // gives a Span<T> of length Size
        [SxUniformBufferLookup] public static SxPtrLookup<GEffectBuffer> LookupPtr; // gives T* to first element
    }
}

