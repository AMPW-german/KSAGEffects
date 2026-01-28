using Brutal;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Brutal.VulkanApi.Abstractions;
using KSA;
using System;

namespace KSAGEffects
{
    internal static class KxImGui
    {
        internal static readonly KeyHash MarkerKey = KeyHash.Make("KxImGuiShader");
        internal static unsafe void CustomShader(KeyHash key)
        {
            var data = new uint2(MarkerKey.Code, key.Code);
            ImGui.GetWindowDrawList().AddCallback(DummyCallback, (nint)(&data), ByteSize.Of<uint2>().Bytes);
        }
        private static unsafe void DummyCallback(ImDrawList* parent_list, ImDrawCmd* cmd) { }
    }
}
