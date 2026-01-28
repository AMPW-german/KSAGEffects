using Brutal.ImGuiApi;
using Brutal.VulkanApi;
using KittenExtensions;
using KSA;
using StarMap.API;
using System;
using System.Linq;
using System.Reflection;

namespace KSAGEffects
{
    [StarMapMod]
    public class KSAGEffects
    {
        static Assembly KittenExtensionsAsm;

        [StarMapImmediateLoad]
        public void Init(Mod definingMod)
        {
            Console.WriteLine("Hello World from G Effects!");

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Console.WriteLine($"Loaded assembly: {asm.GetName().Name}");
            }

            KittenExtensionsAsm = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "KittenExtensions");
        }

        public static double StandardGravity => KSA.Constants.STANDARD_GRAVITY;

        private bool showDebugWindow = true;
        [StarMapAfterGui]
        public void AfterGui(double dt)
        {
            if (!showDebugWindow) return;

            Vehicle? rocket = Program.ControlledVehicle;
            if (rocket != null)
            {
                float t = rocket.GetManualThrottle();

                if (MyBufferUbo.LookupSpan != null)
                {
                    Span<MyBufferUbo> data = MyBufferUbo.LookupSpan(KeyHash.Make("TestBuffer2"));
                    data[0].V1 = 0.75f;
                    data[0].V2 = t / 1.5f;
                }

                if (TexelBufferUbo.LookupSpan != null)
                {
                    Span<TexelBufferUbo> data = TexelBufferUbo.LookupSpan(KeyHash.Make("TestTexelBuffer2"));
                    data[0].UV1 = new System.Numerics.Vector2(1.0f / 3440 * 2, 1.0f / 1360 * 2);
                }
            }

            //KeyHash GEffectShader = KeyHash.Make("GEffectFrag");

            // Create a debug window for showing g forces and calculated effect parameters
            if (ImGui.Begin("G Effects debug window", ref showDebugWindow))
            {
                //KxImGui.CustomShader(GEffectShader);

                ImGui.Text($"Hello World!!!");
                ImGui.TextColored(new Brutal.Numerics.float4(1, 0, 0, 1), $"G Force x axis: {(rocket.AccelerationBody.X / StandardGravity):f4} ({rocket.AccelerationBody.X:f4})");
                ImGui.TextColored(new Brutal.Numerics.float4(0, 1, 0, 1), $"G Force y axis: {(rocket.AccelerationBody.Y / StandardGravity):f4} ({rocket.AccelerationBody.Y:f4})");
                ImGui.TextColored(new Brutal.Numerics.float4(0, 0, 1, 1), $"G Force z axis: {(rocket.AccelerationBody.Z / StandardGravity):f4} ({rocket.AccelerationBody.Z:f4})");
                ImGui.TextColored(new Brutal.Numerics.float4(1, 0, 1, 1), $"Overall G Force: {(rocket.AccelerationBody.Length() / StandardGravity):f4} ({rocket.AccelerationBody.Length():f4})");
            }
            ImGui.End();
        }
    }
}
