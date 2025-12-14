using Brutal.ImGuiApi;
using Brutal.VulkanApi;
using KSA;
using StarMap.API;

namespace KSAGEffects
{
    [StarMapMod]
    public class KSAGEffects
    {
        //private ShaderReference gEffectsShader;
        //private ShaderReference ScreenSpaceVert;
        private VkShaderModule gEffectsShaderModule;

        [StarMapImmediateLoad]
        public void Init(Mod definingMod)
        {
            Console.WriteLine("Hello World from G Effects!");
        }

        public static double StandardGravity => KSA.Constants.STANDARD_GRAVITY;

        private bool showDebugWindow = true;
        [StarMapAfterGui]
        public void AfterGui(double dt)
        {
            //if (gEffectsShader == null)
            //{
            //    try
            //    {
            //        // Shader needs to be placed inside Content/Core/Shaders/ and added to DefaultAssets.xml
            //        ScreenSpaceVert = ModLibrary.Get<ShaderReference>("ScreenspaceVert").Get();
            //        gEffectsShader = ModLibrary.Get<ShaderReference>("GEffectFrag").Get();

            //        //Program.GetMainCamera().
            //    }
            //    catch { }
            //}
            if (!showDebugWindow) return;

            Vehicle? rocket = Program.ControlledVehicle;
            if (rocket == null) return;

            // Create a debug window for showing g forces and calculated effect parameters
            if (ImGui.Begin("G Effects debug window", ref showDebugWindow))
            {
                ImGui.Text($"Hello World");
                ImGui.Text($"G Force x axis: {(rocket.AccelerationBody.X / StandardGravity):f4} ({rocket.AccelerationBody.X:f4})");
                ImGui.Text($"G Force y axis: {(rocket.AccelerationBody.Y / StandardGravity):f4} ({rocket.AccelerationBody.Y:f4})");
                ImGui.Text($"G Force z axis: {(rocket.AccelerationBody.Z / StandardGravity):f4} ({rocket.AccelerationBody.Z:f4})");
                ImGui.Text($"Overall G Force: {(rocket.AccelerationBody.Length() / StandardGravity):f4} ({rocket.AccelerationBody.Length():f4})");
            }
            ImGui.End();
        }


    }
}
