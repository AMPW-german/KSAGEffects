using Brutal.VulkanApi.Abstractions;
using KSA;

#pragma warning disable IDE0130 // Namespace does not match folder structure
namespace ShaderExtensions
#pragma warning restore IDE0130 // Namespace does not match folder structure
{
    [AttributeUsage(AttributeTargets.Struct)]
    internal class SxUniformBufferAttribute(string xmlElement) : Attribute;

    [AttributeUsage(AttributeTargets.Field)]
    internal class SxUniformBufferLookupAttribute() : Attribute;

    [AttributeUsage(AttributeTargets.Struct)]
    internal class SxPushConstantAttribute(string xmlElement) : Attribute;

    [AttributeUsage(AttributeTargets.Field)]
    internal class SxPushConstantLookupAttribute() : Attribute;

    public delegate BufferEx SxBufferLookup(KeyHash hash);
    public delegate MappedMemory SxMemoryLookup(KeyHash hash);
    public delegate Span<T> SxSpanLookup<T>(KeyHash hash) where T : unmanaged;
    public unsafe delegate T* SxPtrLookup<T>(KeyHash hash) where T : unmanaged;
}