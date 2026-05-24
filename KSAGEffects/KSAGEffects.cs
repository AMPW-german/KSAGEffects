using Brutal.ImGuiApi;
using Brutal.Numerics;
using HarmonyLib;
using KSA;
using KSAGEffects.Logging;
using StarMap.API;
using System.Reflection;

namespace KSAGEffects
{
    [StarMapMod]
    [HarmonyPatch]
    public class KSAGEffects
    {
        // Vehicle IDs must be unique I think
        public static Dictionary<string, KSAGEffectsLogicInstance> GEffectsInstances => KSAGEffectsLogicInstance.NamedInstances;
        public static KSAGEffectsLogicInstance? GetLogicInstance(string vehicleId) => KSAGEffectsLogicInstance.NamedInstances.FirstOrDefault(kvp => kvp.Key == vehicleId).Value;

        private static void UpdateLogicInstance(string vehicleId, double deltaTime, double3 g)
        {
            // First version uses overall g force as Gz value
            // Gx and Gy are ignored for now
            double absoluteG = g.Length();
            KSAGEffectsLogicInstance instance = GetLogicInstance(vehicleId) ?? new KSAGEffectsLogicInstance(vehicleId);
            instance.Update(deltaTime, 0, 0, absoluteG);
        }

        [StarMapImmediateLoad]
        public void Init(Mod definingMod)
        {
            Console.WriteLine("Hello World from G Effects!");
            var harmony = new Harmony("KSAGEffects");
            harmony.PatchAll();
            new LogicLogging();
            GEffectsLogic.LogicSettings.DebugMode = false;
        }

        // Vehicles that were not in the FullPhysics or SingleSurfaceMotion updates are (probably) in free fall in space
        // This means 0 g forces and after initial grace period (to let all internal physiological effects stabilize) no further updates and effects are needed until they are updated again

        // VehicleUpdateTask
        // -> Run
        //  -> DoWorkAndStageResults
        //    -> ApplyFullPhysics
        //    -> ApplySingleVehicleMotion
        //      -> ApplyFullPhysics
        //      -> ApplySingleSurfaceMotion

        // Vehicle.UpdateFromTaskResults updates the vehicle's state but is lacking the delta time

        [HarmonyPatch(typeof(VehicleUpdateTask), "ApplyResultsToVehicles"), HarmonyPostfix]
        public static void VehicleUpdateTask_ApplyResultsToVehicles_Postfix(VehicleUpdateTask __instance)
        {
            // Since this is a postfix the vehicle states have already been updated

            List<VehicleUpdateState> vehicleStates = typeof(VehicleUpdateTask).GetField("_vehicleStates", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(__instance) as List<VehicleUpdateState>;
            if (vehicleStates == null || vehicleStates.Count == 0) return;

            double dt = __instance.SimStep.DeltaTime;
            if (dt <= 0) return;
            // Gets unreliable over 120x time warp
            // It's assumed that at such high time warps no active maneuvers are happening
            // Update paused vehicles need to be implemented to fix the high time warp issue

            List<Vehicle> vehicles = vehicleStates.Select(vs => vs.ReadOnlyVehicle).ToList();
            vehicles.Where(v => v.BubbleLeader == v).ToList().ForEach(v => vehicles.AddRange(v.NearbyVehicles));
            vehicles = vehicles.Distinct().ToList();

            vehicles.ForEach(v => UpdateLogicInstance(v.Id, dt, v.AccelerationBody / StandardGravity));
        }

        public static double StandardGravity => KSA.Constants.STANDARD_GRAVITY;
        public static float vignetteShape = 2.0f; // 1.0 is circular, higher streches it into an oval
        public static float screenSizeAdjustment = 1.0f; // Screen ratio adjustment for the vignette effect, includes vignetteShape
        public static float edgeDistance = 1.0f;

        private bool showDebugWindow = true;
        [StarMapAfterGui]
        public void AfterGui(double dt)
        {
            if (!showDebugWindow) return;

            if (GEffectBuffer.LookupSpan != null)
            {
                float2 screenSize = new float2(Program.MainViewport.Size.X, Program.MainViewport.Size.Y);

                Span<GEffectBuffer> data = GEffectBuffer.LookupSpan(KeyHash.Make("GEffectBuffer"));
                data[0].ScreenSizeAdjustment = vignetteShape * screenSize.Y / screenSize.X;
            }

            // Create a debug window for showing g forces and calculated effect parameters
            if (ImGui.Begin("G Effects debug window", ref showDebugWindow))
            {
                Dictionary<string, KSAGEffectsLogicInstance> instances = new(GEffectsInstances);

                Vehicle? activeVehicle = Program.ControlledVehicle;
                if (activeVehicle != null)
                {
                    instances.Remove(activeVehicle.Id);
                    KSAGEffectsLogicInstance instance = GetLogicInstance(activeVehicle.Id) ?? new KSAGEffectsLogicInstance(activeVehicle.Id);

                    //if (instance.ConsciousnessLevel < 0.01f)
                    //{
                    //    Vehicle.ControlsLockout
                    //}

                    ImGui.Text($"Effect parameters for vehicle {activeVehicle.Id}:");
                    ImGui.Text($"Gz: {instance.LastGz:f4}");
                    ImGui.Text($"Consciousness level: {instance.ConsciousnessLevel:f4}");
                    ImGui.Text($"Vision level: {instance.GreyScaleLevel:f4}");
                    if (instance.Enabled && ImGui.Button("Disable")) instance.Enabled = false;
                    else if (!instance.Enabled && ImGui.Button("Enable")) instance.Enabled = true;

                    if (GEffectBuffer.LookupSpan != null)
                    {
                        //float value = 1.0f - (activeVehicle.GetManualThrottle() - 0.01f) * 1.0101f;
                        Span<GEffectBuffer> data = GEffectBuffer.LookupSpan(KeyHash.Make("GEffectBuffer"));

                        if (instance.Enabled)
                        {
                            data[0].GrayScaleLevel = (float)instance.GreyScaleLevel;
                            data[0].TunnelVisionLevel = (float)instance.TunnelVisionLevel;
                        }
                        else
                        {
                            data[0].GrayScaleLevel = 0f;
                            data[0].TunnelVisionLevel = 0f;
                        }
                    }
                }
                else
                {
                    if (GEffectBuffer.LookupSpan != null)
                    {
                        Span<GEffectBuffer> data = GEffectBuffer.LookupSpan(KeyHash.Make("GEffectBuffer"));
                        data[0].GrayScaleLevel = 0f;
                        data[0].TunnelVisionLevel = 0f;
                    }
                }

                ImGui.BeginTable("GEffectsInstancesTable", 5, ImGuiTableFlags.Borders | ImGuiTableFlags.Resizable);
                ImGui.TableSetupColumn("Vehicle ID", ImGuiTableColumnFlags.WidthFixed, initWidthOrWeight: 150f);
                ImGui.TableSetupColumn("Gz", ImGuiTableColumnFlags.WidthFixed, initWidthOrWeight: 100f);
                ImGui.TableSetupColumn("Consciousness", ImGuiTableColumnFlags.WidthFixed, initWidthOrWeight: 100f);
                ImGui.TableSetupColumn("Vision", ImGuiTableColumnFlags.WidthFixed, initWidthOrWeight: 100f);
                ImGui.TableSetupColumn("Enabled", ImGuiTableColumnFlags.WidthFixed, initWidthOrWeight: 100f);
                ImGui.TableHeadersRow();

                foreach (KeyValuePair<string, KSAGEffectsLogicInstance> item in instances)
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.Text(item.Key);
                    ImGui.TableNextColumn();
                    ImGui.PushID($"{item.Key}_Gz");
                    ImGui.Text($"{item.Value.LastGz:f4}");
                    ImGui.PopID();
                    ImGui.TableNextColumn();
                    ImGui.PushID($"{item.Key}_Consciousness");
                    ImGui.Text($"{item.Value.ConsciousnessLevel:f4}");
                    ImGui.PopID();
                    ImGui.TableNextColumn();
                    ImGui.PushID($"{item.Key}_Vision");
                    ImGui.Text($"{item.Value.GreyScaleLevel:f4}");
                    ImGui.PopID();
                    ImGui.TableNextColumn();
                    ImGui.PushID($"{item.Key}_Enabled");
                    if (item.Value.Enabled && ImGui.Button("Disable")) item.Value.Enabled = false;
                    else if (!item.Value.Enabled && ImGui.Button("Enable")) item.Value.Enabled = true;
                    ImGui.PopID();
                }

                ImGui.EndTable();
            }
            ImGui.End();
        }
    }
}
