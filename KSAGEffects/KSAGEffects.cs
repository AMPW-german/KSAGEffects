using Brutal.ImGuiApi;
using Brutal.Numerics;
using Core;
using HarmonyLib;
using KSA;
using KSAGEffects.Logging;
using KSAGEffects.Shaders;
using StarMap.API;
using System.Reflection;

namespace KSAGEffects
{
    [StarMapMod]
    [HarmonyPatch]
    public class KSAGEffects
    {
        public const int GaussianBlurMaxRadius = 20; // Max blurHorizontal radius in pixels
        public static KeyHash GaussianBlurHorizontalHash = KeyHash.Make("GEffectsGaussianBlurShaderHorizontalPostPushConstantsBuffer");
        public static KeyHash GaussianBlurVerticalHash = KeyHash.Make("GEffectsGaussianBlurShaderVerticalPostPushConstantsBuffer");
        public static KeyHash GEffectBufferHash = KeyHash.Make("GEffectBuffer");

        public static bool negativeG = false;

        // Vehicle IDs must be unique I think
        public static Dictionary<string, KSAGEffectsLogicInstance> GEffectsInstances => KSAGEffectsLogicInstance.NamedInstances;
        public static KSAGEffectsLogicInstance? GetLogicInstance(string vehicleId) => KSAGEffectsLogicInstance.NamedInstances.FirstOrDefault(kvp => kvp.Key == vehicleId).Value;

        private static void UpdateLogicInstance(string vehicleId, double deltaTime, double3 g)
        {
            // First version uses overall g force as Gz value
            // Gx and Gy are ignored for now
            double absoluteG = g.Length();
            if (negativeG) absoluteG = -absoluteG;
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
        //      -> FullPhysicsPreStep
        //      -> FullPhysicsUnconstrainedStep
        //    -> ApplySingleVehicleMotion
        //      -> ApplyFullPhysics
        //        -> FullPhysicsPreStep
        //        -> FullPhysicsUnconstrainedStep
        //      -> ApplySingleSurfaceMotion

        // Vehicle.UpdateFromTaskResults updates the vehicle's state but is lacking the delta time

        [HarmonyPatch(typeof(PhysicsBubble), "ApplyResultsToVehicles"), HarmonyPostfix]
        public static void VehicleUpdateTask_ApplyResultsToVehicles_Postfix(PhysicsBubble __instance)
        {
            // Since this is a postfix the vehicle states have already been updated

            List<VehicleUpdateState> vehicleStates = typeof(PhysicsBubble)?.GetField("_vehicleStates", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(__instance) as List<VehicleUpdateState> ?? [];
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


        // For KSA v2026.6.2.4531:
        // DoWorkAndStageResults gives (probably) correct acceleration for active burns
        // AccelerationBody is not updated for atmospheric flight

        //[HarmonyPatch(typeof(VehicleUpdateTask), "DoWorkAndStageResults"), HarmonyPostfix]
        //public static void VehicleUpdateTask_DoWorkAndStageResults_Postfix(VehicleUpdateTask __instance)
        //{
        //    // Since this is a postfix the vehicle states have already been updated

        //    List<VehicleUpdateState> vehicleStates = typeof(VehicleUpdateTask).GetField("_vehicleStates", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(__instance) as List<VehicleUpdateState>;
        //    if (vehicleStates == null || vehicleStates.Count == 0) return;

        //    double dt = __instance.SimStep.DeltaTime;
        //    if (dt <= 0) return;
        //    // Gets unreliable over 120x time warp
        //    // It's assumed that at such high time warps no active maneuvers are happening
        //    // Update paused vehicles need to be implemented to fix the high time warp issue

        //    List<Vehicle> vehicles = vehicleStates.Select(vs => vs.ReadOnlyVehicle).ToList();
        //    vehicles.Where(v => v.BubbleLeader == v).ToList().ForEach(v => vehicles.AddRange(v.NearbyVehicles));
        //    vehicles = vehicles.Distinct().ToList();

        //    foreach (Vehicle v in vehicles)
        //    {
        //        ReadOnlyPhysicsStates physicsState = v.GetPhysicsStates();
        //        KinematicStates kinematic = physicsState.Kinematic;
        //        double a = kinematic.PositionPhys.Length() * dt / StandardGravity;
        //        double b = kinematic.VelocityPhys.Length() * dt / StandardGravity;
        //    }

        //    //vehicles.ForEach(v => UpdateLogicInstance(v.Id, dt, v.AccelerationBody / StandardGravity));
        //    vehicles.ForEach(v => UpdateLogicInstance(v.Id, dt, v.GetPhysicsStates().Kinematic.VelocityPhys * dt / StandardGravity));
        //}

        private static void CalculateGaussianWeights(double radius, Span<double> weights, out int shaderRadius)
        {
            weights.Clear();
            radius = Math.Clamp(radius, 0.0, GaussianBlurMaxRadius);
            shaderRadius = (int)Math.Ceiling(radius);

            if (radius <= 0.0 || shaderRadius == 0)
            {
                weights[0] = 1.0;
                return;
            }

            double sigma = radius / 3.0;
            double twoSigmaSquared = 2.0 * sigma * sigma;
            double total = 0.0;

            for (int i = 0; i <= shaderRadius; i++)
            {
                double weight = Math.Exp(-(i * i) / twoSigmaSquared);
                weights[i] = weight;
                total += i == 0 ? weight : weight * 2.0;
            }

            for (int i = 0; i <= shaderRadius; i++)
            {
                weights[i] /= total;
            }
        }

        [HarmonyPatch(typeof(Vehicle), "OnKey"), HarmonyPrefix]
        public static bool Vehicle_OnKey_Prefix(Vehicle __instance)
        {
            KSAGEffectsLogicInstance logicInstance = GetLogicInstance(__instance.Id) ?? new KSAGEffectsLogicInstance(__instance.Id);

            return !logicInstance.Enabled || logicInstance.ConsciousnessLevel >= 0.05f;
        }

        public static double StandardGravity => KSA.Constants.STANDARD_GRAVITY;
        public static float vignetteShape = 2.0f; // 1.0 is circular, higher streches it into an oval
        public static float screenSizeAdjustment = 1.0f; // Screen ratio adjustment for the vignette effect, includes vignetteShape
        public static float edgeDistance = 1.0f;

        private bool showDebugWindow = true;
        [StarMapAfterGui]
        public unsafe void AfterGui(double dt)
        {
            if (!showDebugWindow) return;

            if (GEffectBuffer.LookupSpan != null)
            {
                float2 screenSize = new(Program.MainViewport.Size.X, Program.MainViewport.Size.Y);

                Span<GEffectBuffer> data = GEffectBuffer.LookupSpan(KeyHash.Make("GEffectBuffer"));
                data[0].ScreenSizeAdjustment = vignetteShape * screenSize.Y / screenSize.X;
            }

            ImGui.SetNextWindowPos(
                new float2(100.0f, 100.0f),
                ImGuiCond.FirstUseEver
            );

            // Create a debug window for showing g forces and calculated effect parameters
            if (ImGui.Begin("G Effects debug window", ref showDebugWindow, ImGuiWindowFlags.NoSavedSettings))
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
                    ImGui.Text($"Greyscale level: {instance.GreyScaleLevel:f4}");
                    ImGui.Text($"Tunnel vision level: {instance.TunnelVisionLevel:f4}");
                    ImGui.Text($"Film grain level: {instance.FilmGrainLevel:f4}");
                    ImGui.Text($"Blur level: {instance.BlurLevel:f4}");
                    if (instance.Enabled && ImGui.Button("Disable")) instance.Enabled = false;
                    else if (!instance.Enabled && ImGui.Button("Enable")) instance.Enabled = true;
                    if (ImGui.Button("Reset")) instance.Reset();
                    if (ImGui.Button($"Negative G: {negativeG}")) negativeG = !negativeG;

                    // Very Important TODO: Fix completly black screen when instance is disabled
                    if (GEffectsBlurPushConstantsBuffer.LookupSpan != null)
                    {
                        Span<GEffectsBlurPushConstantsBuffer> dataHorizontal = GEffectsBlurPushConstantsBuffer.LookupSpan(GaussianBlurHorizontalHash);
                        ref GEffectsBlurPushConstantsBuffer blurHorizontal = ref dataHorizontal[0];
                        Span<GEffectsBlurPushConstantsBuffer> dataVertical = GEffectsBlurPushConstantsBuffer.LookupSpan(GaussianBlurVerticalHash);
                        ref GEffectsBlurPushConstantsBuffer blurVertical = ref dataVertical[0];

                        Span<double> weights = stackalloc double[GaussianBlurMaxRadius + 1];

                        if (instance.Enabled)
                        {
                            // Max blurHorizontal radius = 20 px
                            float radius = GaussianBlurMaxRadius * (float)instance.BlurLevel;

                            CalculateGaussianWeights(radius, weights, out int shaderRadius);
                            blurHorizontal.Radius = shaderRadius;
                            blurVertical.Radius = shaderRadius;

                            fixed (float* destination = blurHorizontal.Weights)
                            {
                                for (int i = 0; i <= GaussianBlurMaxRadius; i++)
                                {
                                    destination[i] = (float)weights[i];
                                }
                            }
                            fixed (float* destination = blurVertical.Weights)
                            {
                                for (int i = 0; i <= GaussianBlurMaxRadius; i++)
                                {
                                    destination[i] = (float)weights[i];
                                }
                            }
                        }
                        else
                        {
                            blurHorizontal.Radius = 0;
                            blurVertical.Radius = 0;

                            fixed (float* destination = blurHorizontal.Weights)
                            {
                                for (int i = 0; i <= GaussianBlurMaxRadius; i++)
                                {
                                    destination[i] = 0.0f;
                                }
                            }
                            fixed (float* destination = blurVertical.Weights)
                            {
                                for (int i = 0; i <= GaussianBlurMaxRadius; i++)
                                {
                                    destination[i] = 0.0f;
                                }
                            }
                        }

                    }

                    if (GEffectBuffer.LookupSpan != null)
                    {
                        //float value = 1.0f - (activeVehicle.GetManualThrottle() - 0.01f) * 1.0101f;
                        Span<GEffectBuffer> data = GEffectBuffer.LookupSpan(GEffectBufferHash);

                        Renderer renderer = Program.GetRenderer();
                        data[0].FilmGrainData.X = renderer.Extent.Width;
                        data[0].FilmGrainData.Y = renderer.Extent.Height;
                        data[0].FilmGrainData.Z = 2.0f; // film grain scale
                        data[0].FilmGrainData.W += (float)dt;

                        if (instance.Enabled)
                        {
                            data[0].GrayScaleLevel = (float)instance.GreyScaleLevel;
                            data[0].TunnelVisionLevel = (float)instance.TunnelVisionLevel;
                            data[0].FilmGrainLevel = (float)instance.FilmGrainLevel;
                            data[0].TunnelVisionColor = instance.PrimaryColor ? new float4(0.0f, 0.0f, 0.0f, 1.0f) : new float4(1.0f, 0.0f, 0.0f, 1.0f);
                        }
                        else
                        {
                            data[0].GrayScaleLevel = 0f;
                            data[0].TunnelVisionLevel = 0f;
                            data[0].FilmGrainLevel = 0f;
                            data[0].TunnelVisionColor = new float4(0.0f, 0.0f, 0.0f, 1.0f);
                        }
                    }
                }
                else
                {
                    if (GEffectsBlurPushConstantsBuffer.LookupSpan != null)
                    {
                        Span<GEffectsBlurPushConstantsBuffer> dataHorizontal = GEffectsBlurPushConstantsBuffer.LookupSpan(GaussianBlurHorizontalHash);
                        ref GEffectsBlurPushConstantsBuffer blurHorizontal = ref dataHorizontal[0];
                        Span<GEffectsBlurPushConstantsBuffer> dataVertical = GEffectsBlurPushConstantsBuffer.LookupSpan(GaussianBlurVerticalHash);
                        ref GEffectsBlurPushConstantsBuffer blurVertical = ref dataVertical[0];
                        blurHorizontal.Radius = 0;
                        blurVertical.Radius = 0;
                        fixed (float* destination = blurHorizontal.Weights)
                        {
                            for (int i = 0; i <= GaussianBlurMaxRadius; i++)
                            {
                                destination[i] = 0f;
                            }
                        }
                        fixed (float* destination = blurVertical.Weights)
                        {
                            for (int i = 0; i <= GaussianBlurMaxRadius; i++)
                            {
                                destination[i] = 0f;
                            }
                        }
                    }

                    if (GEffectBuffer.LookupSpan != null)
                    {
                        Span<GEffectBuffer> data = GEffectBuffer.LookupSpan(GEffectBufferHash);
                        data[0].GrayScaleLevel = 0f;
                        data[0].TunnelVisionLevel = 0f;
                        data[0].FilmGrainLevel = 0f;
                    }
                }

                ImGui.BeginTable("GEffectsInstancesTable", 9, ImGuiTableFlags.Borders | ImGuiTableFlags.Resizable);
                ImGui.TableSetupColumn("Vehicle ID", ImGuiTableColumnFlags.WidthFixed, initWidthOrWeight: 150f);
                ImGui.TableSetupColumn("Gz", ImGuiTableColumnFlags.WidthFixed, initWidthOrWeight: 100f);
                ImGui.TableSetupColumn("Consciousness", ImGuiTableColumnFlags.WidthFixed, initWidthOrWeight: 100f);
                ImGui.TableSetupColumn("Greyscale", ImGuiTableColumnFlags.WidthFixed, initWidthOrWeight: 100f);
                ImGui.TableSetupColumn("Tunnel Vision", ImGuiTableColumnFlags.WidthFixed, initWidthOrWeight: 100f);
                ImGui.TableSetupColumn("Film Grain", ImGuiTableColumnFlags.WidthFixed, initWidthOrWeight: 100f);
                ImGui.TableSetupColumn("Blur", ImGuiTableColumnFlags.WidthFixed, initWidthOrWeight: 100f);
                ImGui.TableSetupColumn("Enabled", ImGuiTableColumnFlags.WidthFixed, initWidthOrWeight: 100f);
                ImGui.TableSetupColumn("Reset", ImGuiTableColumnFlags.WidthFixed, initWidthOrWeight: 100f);
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
                    ImGui.PushID($"{item.Key}_Greyscale");
                    ImGui.Text($"{item.Value.GreyScaleLevel:f4}");
                    ImGui.PopID();
                    ImGui.TableNextColumn();
                    ImGui.PushID($"{item.Key}_TunnelVision");
                    ImGui.Text($"{item.Value.TunnelVisionLevel:f4}");
                    ImGui.PopID();
                    ImGui.TableNextColumn();
                    ImGui.PushID($"{item.Key}_FilmGrain");
                    ImGui.Text($"{item.Value.FilmGrainLevel:f4}");
                    ImGui.PopID();
                    ImGui.TableNextColumn();
                    ImGui.PushID($"{item.Key}_Blur");
                    ImGui.Text($"{item.Value.BlurLevel:f4}");
                    ImGui.PopID();
                    ImGui.TableNextColumn();
                    ImGui.PushID($"{item.Key}_Enabled");
                    if (item.Value.Enabled && ImGui.Button("Disable")) item.Value.Enabled = false;
                    else if (!item.Value.Enabled && ImGui.Button("Enable")) item.Value.Enabled = true;
                    ImGui.PopID();
                    ImGui.TableNextColumn();
                    ImGui.PushID($"{item.Key}_Reset");
                    if (ImGui.Button("Reset")) item.Value.Reset();
                    ImGui.PopID();
                }

                ImGui.EndTable();
            }
            ImGui.End();
        }
    }
}
