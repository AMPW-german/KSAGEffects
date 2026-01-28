// <MyBuffer Id="MyBuf" Size="1" />, where Size is the number of sequential MyBufferUbo elements in the buffer
using Brutal.VulkanApi.Abstractions;
using KSA;
using KSAGEffects;
using System;
using System.Numerics;
using System.Runtime.InteropServices;

namespace KittenExtensions
{
    [AttributeUsage(AttributeTargets.Struct)]
#pragma warning disable CS9113 // Parameter is unread.
    internal class KxUniformBufferAttribute(string xmlElement) : Attribute;
#pragma warning restore CS9113 // Parameter is unread.

    [AttributeUsage(AttributeTargets.Field)]
    internal class KxUniformBufferLookupAttribute() : Attribute;

    // You can use your own delegate types as long as the signature matches one of these
    public delegate BufferEx KxBufferLookup(KeyHash hash);
    public delegate MappedMemory KxMemoryLookup(KeyHash hash);
    public delegate Span<T> KxSpanLookup<T>(KeyHash hash) where T : unmanaged;
    public unsafe delegate T* KxPtrLookup<T>(KeyHash hash) where T : unmanaged;

    [KxUniformBuffer("TestBuffer")]
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct MyBufferUbo
    {
        public float V1;
        public float V2;

        // lookup delegate fields must be static fields on the buffer element type
        // the names and specific types of these are not relevant, as long as the delegate signature matches
        // these are not all required, but you will need at least one to be able to set the uniform data
        [KxUniformBufferLookup] public static KxBufferLookup LookupBuffer;
        [KxUniformBufferLookup] public static KxMemoryLookup LookupMemory;
        [KxUniformBufferLookup] public static KxSpanLookup<MyBufferUbo> LookupSpan; // gives a Span<T> of length Size
        [KxUniformBufferLookup] public static KxPtrLookup<MyBufferUbo> LookupPtr; // gives T* to first element
    }

    [KxUniformBuffer("TexelBuffer")]
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct TexelBufferUbo
    {
        public Vector2 UV1;

        // lookup delegate fields must be static fields on the buffer element type
        // the names and specific types of these are not relevant, as long as the delegate signature matches
        // these are not all required, but you will need at least one to be able to set the uniform data
        [KxUniformBufferLookup] public static KxBufferLookup LookupBuffer;
        [KxUniformBufferLookup] public static KxMemoryLookup LookupMemory;
        [KxUniformBufferLookup] public static KxSpanLookup<TexelBufferUbo> LookupSpan; // gives a Span<T> of length Size
        [KxUniformBufferLookup] public static KxPtrLookup<TexelBufferUbo> LookupPtr; // gives T* to first element
    }
}

