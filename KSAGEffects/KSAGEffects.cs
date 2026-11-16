using Brutal.ImGuiApi;
using Brutal.Numerics;
using Core;
using HarmonyLib;
using KSA;
using KSAGEffects.Logging;
using KSAGEffects.Shaders;
using StarMap.API;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

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
        public static ConditionalWeakTable<string, KSAGEffectsLogicInstance> GEffectsInstances => KSAGEffectsLogicInstance.NamedInstances;
        public static KSAGEffectsLogicInstance GetLogicInstance(string kittenId) => KSAGEffectsLogicInstance.NamedInstances.GetOrAdd(kittenId, v => new KSAGEffectsLogicInstance(kittenId));
        public static List<KSAGEffectsLogicInstance> GetVehicleInstances(Vehicle vehicle)
        {
            ReadOnlySpan<IVASeat> crewSeats = vehicle.Crew;
            List<KSAGEffectsLogicInstance> instances = new();
            for (int i = 0; i < vehicle.Crew.Length; i++)
            {
                IVASeat iVASeat = vehicle.Crew[i];
                if (!(iVASeat.AssignedKittenHash == KeyHash.Zero))
                {
                    KittenRosterEntryData? kittenRosterEntryData = Universe.KittenRoster.Find(iVASeat.AssignedKittenHash);
                    if (kittenRosterEntryData != null && !kittenRosterEntryData.Kia && kittenRosterEntryData.AssignedToVehicle)
                    {
                        instances.Add(GetLogicInstance(kittenRosterEntryData.Name));
                    }
                }
            }
            return instances;
        }

        private struct PendingKittenDeath
        {
            public Vehicle Vehicle;
            public IVASeat? Seat;
            public KittenRosterEntryData Entry;
        }

        private static readonly List<PendingKittenDeath> _pendingKittenDeaths = new();

        private static void RemoveLogicInstance(string kittenName)
        {
            if (GEffectsInstances.TryGetValue(kittenName, out KSAGEffectsLogicInstance? instance))
            {
                GEffectsInstances.Remove(kittenName);
                instance.Dispose();
            }
        }

        // Every path that takes a kitten off a vehicle mission (recovery, vehicle
        // destruction, death, crew reassignment, save-load cleanup) ends in one of
        // these two roster entry methods, so they are the single cleanup point.
        [HarmonyPatch(typeof(KittenRosterEntryData), "LeaveVehicle"), HarmonyPostfix]
        public static void KittenRosterEntry_LeaveVehicle_Postfix(KittenRosterEntryData __instance) => RemoveLogicInstance(__instance.Name);

        [HarmonyPatch(typeof(KittenRosterEntryData), "EndMission"), HarmonyPostfix]
        public static void KittenRosterEntry_EndMission_Postfix(KittenRosterEntryData __instance) => RemoveLogicInstance(__instance.Name);

        [StarMapImmediateLoad]
        public void Init(Mod definingMod)
        {
            Brutal.Logging.DefaultCategory.Log.Info("Hello World from G Effects!");
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

        private static readonly AccessTools.FieldRef<PhysicsBubble, List<VehicleUpdateState>> VehicleStatesRef =
            AccessTools.FieldRefAccess<PhysicsBubble, List<VehicleUpdateState>>("_vehicleStates");
        // _overrideIsControllable is a static debug flag shared by all vehicles.
        private static readonly AccessTools.FieldRef<bool> OverrideIsControllableRef =
            AccessTools.StaticFieldRefAccess<bool>(AccessTools.Field(typeof(Vehicle), "_overrideIsControllable"));
        private static readonly AccessTools.FieldRef<Vehicle, bool> HasProbeCoreRef =
            AccessTools.FieldRefAccess<Vehicle, bool>("_hasProbeCore");
        private static readonly List<KSAGEffectsLogicInstance> _touchedInstances = new(capacity: 64);
        private static bool _probeCoreAlertActive;
        private const float ProbeCoreHintScale = 0.25f;
#if DEBUG
        private static int _virtualInstanceCount;
        private static string? _virtualSyncedId;
        private static int _virtualSyncedCount = -1;
        private static readonly List<string> _virtualKeyPool = new();

        private static void SyncVirtualInstances()
        {
            string? id = Program.ControlledVehicle?.Id;
            if (id == _virtualSyncedId && _virtualInstanceCount == _virtualSyncedCount) return;
            if (id != _virtualSyncedId) _virtualKeyPool.Clear();
            _virtualSyncedId = id;
            _virtualSyncedCount = _virtualInstanceCount;
            if (id == null) return;
            while (_virtualKeyPool.Count < _virtualInstanceCount)
                _virtualKeyPool.Add($"{id}_gInstance_{_virtualKeyPool.Count}");
        }
#endif

        [HarmonyPatch(typeof(VehicleUpdateTask), "ApplyResultsToMainThread"), HarmonyPostfix]
        public static void VehicleUpdateTask_ApplyResultsToMainThread_Postfix(VehicleUpdateTask __instance)
        {
            ReadOnlySpan<PhysicsBubble> bubbles = __instance.SyncWindowBubbles;
            _touchedInstances.Clear();
            _pendingKittenDeaths.Clear();
#if DEBUG
            SyncVirtualInstances();
#endif

            foreach (PhysicsBubble bubble in bubbles)
            {
                if (!bubble.IsResultsReady) continue;
                double dt = bubble.SimStep.DeltaTime;
                if (dt <= 0) continue;

                foreach (VehicleUpdateState vehicleState in VehicleStatesRef(bubble))
                {
                    Vehicle vehicle = vehicleState.ReadOnlyVehicle;
                    if (vehicle == null) continue;

                    double3 g = vehicle.AccelerationBody / StandardGravity;

                    if (vehicle is KittenEva)
                    {
                        KSAGEffectsLogicInstance instance = GetLogicInstance(vehicle.Id);
                        instance.AddPendingInput(dt, g.X, g.Y, g.Z, smoothInputs: true);
                        _touchedInstances.Add(instance);
                        if (instance.IsDead)
                        {
                            KittenRosterEntryData? entry = Universe.KittenRoster.Find(vehicle.Id);
                            if (entry != null)
                            {
                                _pendingKittenDeaths.Add(new PendingKittenDeath { Vehicle = vehicle, Entry = entry });
                            }
                        }
                    }
                    else for (int i = 0; i < vehicle.Crew.Length; i++)
                    {
                        IVASeat iVASeat = vehicle.Crew[i];
                        if (!(iVASeat.AssignedKittenHash == KeyHash.Zero))
                        {
                            KittenRosterEntryData? kittenRosterEntryData = Universe.KittenRoster.Find(iVASeat.AssignedKittenHash);
                            if (kittenRosterEntryData != null && !kittenRosterEntryData.Kia && kittenRosterEntryData.AssignedToVehicle)
                            {
                                // Seat axes are authored in the part's assembly frame.
                                // Transform directions into the vehicle/body frame.
                                double3 forward = double3.Normalize(
                                    iVASeat.ForwardAxisAsmb.Transform(iVASeat.Parent.Asmb2VehicleAsmb));

                                double3 up = double3.Normalize(
                                    iVASeat.UpAxisAsmb.Transform(iVASeat.Parent.Asmb2VehicleAsmb));

                                // Match KSA's seat-axis construction.
                                double3 right = double3.Normalize(double3.Cross(-up, forward));
                                up = -double3.Cross(forward, right);

                                double gx = double3.Dot(g, forward);
                                double gy = double3.Dot(g, right);
                                double gz = double3.Dot(g, -up); // feetward is opposite seat-up
                                KSAGEffectsLogicInstance instance = GetLogicInstance(kittenRosterEntryData.Name);
                                instance.AddPendingInput(dt, gx, gy, gz);
                                _touchedInstances.Add(instance);
                                if (instance.IsDead)
                                {
                                    _pendingKittenDeaths.Add(new PendingKittenDeath { Vehicle = vehicle, Seat = iVASeat, Entry = kittenRosterEntryData });
                                }
                            }
                        }
                    }
#if DEBUG
                    if (_virtualInstanceCount > 0 && vehicle.Id == _virtualSyncedId)
                    {
                        for (int i = 0; i < _virtualInstanceCount; i++)
                        {
                            KSAGEffectsLogicInstance instance = GetLogicInstance(_virtualKeyPool[i]);
                            instance.AddPendingInput(dt, g.X, g.Y, g.Z);
                            _touchedInstances.Add(instance);
                        }
                    }
#endif
                }
            }

            ApplyPendingKittenDeaths();
            GEffectsScheduler.Dispatch(_touchedInstances);

            //List<Vehicle> vehicles = vehicleStates.Select(vs => vs.ReadOnlyVehicle).ToList();
            //vehicles.Where(v => v.BubbleLeader == v).ToList().ForEach(v => vehicles.AddRange(v.NearbyVehicles));
            //vehicles = vehicles.Distinct().ToList();

            //vehicles.ForEach(v => UpdateLogicInstance(v, dt, v.AccelerationBody / StandardGravity));
        }

        private static void ApplyPendingKittenDeaths()
        {
            foreach (PendingKittenDeath death in _pendingKittenDeaths)
            {
                if (death.Entry.Kia) continue;
                death.Entry.Kill(death.Vehicle.HasLaunched, KittenDeathCause.ExcessiveGForce);
                if (death.Seat != null)
                {
                    death.Seat.AssignedKittenHash = KeyHash.Zero;
                    TimedAlert.CreateWarning(death.Entry.Name + " died: excessive G-force!", 10.0);
                }
                else
                {
                    TimedAlert.CreateWarning(death.Entry.Name + " died on EVA: excessive G-force!", 10.0);
                    Universe.DestroyVehicle(death.Vehicle, CrewDisposition.Kill, KittenDeathCause.ExcessiveGForce);
                }
            }
            _pendingKittenDeaths.Clear();
        }

        // The physiological model already kills kittens at their G-tolerance, so
        // the stock structural G-load check is redundant for EVA kittens. The
        // frame's peak G-load is the only input to that check; zeroing it for
        // KittenEva states leaves kitten impact and dynamic-pressure destruction intact.
        [HarmonyPatch(typeof(PhysicsBubble), "DetectStructuralFailure"), HarmonyPrefix]
        public static void PhysicsBubble_DetectStructuralFailure_Prefix(VehicleUpdateState vehicleState)
        {
            if (vehicleState.IsKitten)
            {
                vehicleState.PeakGLoad = 0;
            }
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

        private enum CrewGState
        {
            NoCrew,
            Able,
            AllUnconscious
        }

        // Returns Able as soon as one living crew member can still fly
        // (conscious, effects disabled, or without a G-effects instance).
        private static CrewGState EvaluateCrewGState(Vehicle vehicle)
        {
            bool anyCrew = false;
            ReadOnlySpan<IVASeat> crew = vehicle.Crew;
            for (int i = 0; i < crew.Length; i++)
            {
                IVASeat seat = crew[i];
                if (seat.AssignedKittenHash == KeyHash.Zero) continue;
                KittenRosterEntryData? entry = Universe.KittenRoster.Find(seat.AssignedKittenHash);
                if (entry == null || entry.Kia || !entry.AssignedToVehicle) continue;

                anyCrew = true;
                if (!GEffectsInstances.TryGetValue(entry.Name, out KSAGEffectsLogicInstance? instance) ||
                    !instance.Enabled || !instance.IsUnconscious)
                {
                    return CrewGState.Able;
                }
            }
            return anyCrew ? CrewGState.AllUnconscious : CrewGState.NoCrew;
        }

        // A vehicle is uncontrollable when every living crew member is G-unconscious,
        // unless it has a probe core — mirroring the game's own incapacitation rule.
        // This plugs into the game's ControlsLockout path (OnKey rejection,
        // flight computer gating, "out of control" alert).
        [HarmonyPatch(typeof(Vehicle), "get_IsControllable"), HarmonyPostfix]
        public static void Vehicle_IsControllable_Postfix(Vehicle __instance, ref bool __result)
        {
            if (!__result || OverrideIsControllableRef()) return;
            if (EvaluateCrewGState(__instance) == CrewGState.AllUnconscious)
                __result = HasProbeCoreRef(__instance);
        }

        public static double StandardGravity => KSA.Constants.STANDARD_GRAVITY;
        public static float vignetteShape = 2.0f; // 1.0 is circular, higher stretches it into an oval
        public static float screenSizeAdjustment = 1.0f; // Screen ratio adjustment for the vignette effect, includes vignetteShape
        public static float edgeDistance = 1.0f;

        private bool showDebugWindow = true;
        [StarMapAfterGui]
        public unsafe void AfterGui(double dt)
        {
            // The probe-core crew alert is gameplay information, so it must fire
            // even while the debug window (and with it the shader effects) is closed.
            Vehicle? probeVehicle = Program.ControlledVehicle;
            bool probeCoreMode = false;
            if (probeVehicle != null &&
                EvaluateCrewGState(probeVehicle) == CrewGState.AllUnconscious &&
                HasProbeCoreRef(probeVehicle))
            {
                probeCoreMode = true;
                if (!_probeCoreAlertActive)
                {
                    TimedAlert.CreateWarning(probeVehicle.Id + ": all crew unconscious, probe core engaged", 10.0);
                }
            }
            _probeCoreAlertActive = probeCoreMode;

            if (!showDebugWindow) return;

            if (GEffectBuffer.LookupSpan != null)
            {
                float2 screenSize = new(Program.MainViewport.Size.X, Program.MainViewport.Size.Y);

                Span<GEffectBuffer> data = GEffectBuffer.LookupSpan(KeyHash.Make("GEffectBuffer"));
                data[0].ScreenSizeAdjustment = vignetteShape * screenSize.Y / screenSize.X;
                data[0].TunnelVisionColor = new float4(0.0f, 0.0f, 0.0f, 1.0f);
                data[0].RedoutColor = new float4(1.0f, 0.0f, 0.0f, 1.0f);
                data[0].LoCColor = new float4(0.0f, 0.0f, 0.0f, 1.0f);
            }

            ImGui.SetNextWindowPos(
                new float2(100.0f, 100.0f),
                ImGuiCond.FirstUseEver
            );

#if DEBUG
            SyncVirtualInstances();
#endif

            // Create a debug window for showing g forces and calculated effect parameters
            if (ImGui.Begin("G Effects debug window", ref showDebugWindow, ImGuiWindowFlags.NoSavedSettings))
            {
#if DEBUG
                ImGui.SetNextItemWidth(200.0f);
                ImGui.InputInt("Virtual instances", ref _virtualInstanceCount);
                _virtualInstanceCount = Math.Max(0, _virtualInstanceCount);
#endif

                Dictionary<string, KSAGEffectsLogicInstance> instances = new(GEffectsInstances);

                Vehicle? activeVehicle = Program.ControlledVehicle;
                if (activeVehicle != null)
                {
                    List<KSAGEffectsLogicInstance> crewInstances = GetVehicleInstances(activeVehicle);
                    if (activeVehicle is KittenEva)
                    {
                        crewInstances.Add(GetLogicInstance(activeVehicle.Id));
                    }

                    CrewGState crewGState = EvaluateCrewGState(activeVehicle);

                    // Screen effects follow the crew member best able to fly. When
                    // everyone is out, the worst instance drives the blackout — or,
                    // on probe-core flights, only a faint peripheral hint.
                    KSAGEffectsLogicInstance? instance = crewInstances.Count == 0 ? null
                        : crewGState == CrewGState.AllUnconscious
                            ? crewInstances.MinBy(i => i.ConsciousnessLevel)
                            : crewInstances.MaxBy(i => i.ConsciousnessLevel);

                    //if (instance.ConsciousnessLevel < 0.01f)
                    //{
                    //    Vehicle.ControlsLockout
                    //}

                    ImGui.Text($"Effect parameters for vehicle {activeVehicle.Id}:");
                    if (probeCoreMode) ImGui.Text("Probe core engaged - all crew G-unconscious");
                    if (instance == null)
                    {
                        ImGui.Text("No kittens found for this vehicle.");
                        ImGui.End();
                        return;
                    }
                    else
                    {
                        ImGui.Text($"G: {Math.Sqrt(Math.Pow(instance.LastGx, 2) + Math.Pow(instance.LastGy, 2) + Math.Pow(instance.LastGz, 2)):f4}");
                        ImGui.Text($"Gx: {instance.LastGx:f4}, Gy: {instance.LastGy:f4}, Gz: {instance.LastGz:f4}");
                        ImGui.Text($"Consciousness level: {instance.ConsciousnessLevel:f4}");
                        ImGui.Text($"Grayscale level: {instance.VisualGrayscaleLevel:f4}");
                        ImGui.Text($"Tunnel vision level: {instance.VisualTunnelVisionLevel:f4}");
                        ImGui.Text($"Redout level: {instance.VisualRedoutLevel:f4}");
                        ImGui.Text($"Film grain level: {instance.VisualFilmGrainLevel:f4}");
                        ImGui.Text($"Blur level: {instance.VisualBlurLevel:f4}");
                        ImGui.Text($"Consciousness level: {instance.VisualLoCLevel:f4}");
                    }

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

                        // Max blurHorizontal radius = 20 px
                        float radius = instance.Enabled && !probeCoreMode ? GaussianBlurMaxRadius * (float)instance.VisualBlurLevel : 0.0f;

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

                    if (GEffectBuffer.LookupSpan != null)
                    {
                        //float value = 1.0f - (activeVehicle.GetManualThrottle() - 0.01f) * 1.0101f;
                        Span<GEffectBuffer> data = GEffectBuffer.LookupSpan(GEffectBufferHash);

                        Renderer renderer = Program.GetRenderer();
                        data[0].FilmGrainData.X = renderer.Extent.Width;
                        data[0].FilmGrainData.Y = renderer.Extent.Height;
                        data[0].FilmGrainData.Z = 2.0f; // film grain scale
                        data[0].FilmGrainData.W += (float)dt;

                        if (probeCoreMode && instance.Enabled)
                        {
                            // The probe core is flying while the crew is out: keep the
                            // view navigable and only hint at the crew's state in the
                            // periphery.
                            data[0].GrayScaleLevel = 0.0f;
                            data[0].TunnelVisionLevel = (float)(instance.VisualTunnelVisionLevel * ProbeCoreHintScale);
                            data[0].RedoutLevel = (float)(instance.VisualRedoutLevel * ProbeCoreHintScale);
                            data[0].FilmGrainLevel = 0.0f;
                            data[0].LoCLevel = 0.0f;
                        }
                        else if (instance.Enabled)
                        {
                            data[0].GrayScaleLevel = (float)instance.VisualGrayscaleLevel;
                            data[0].TunnelVisionLevel = (float)instance.VisualTunnelVisionLevel;
                            data[0].RedoutLevel = (float)instance.VisualRedoutLevel;
                            data[0].FilmGrainLevel = (float)instance.VisualFilmGrainLevel;
                            data[0].LoCLevel = (float)instance.VisualLoCLevel;
                        }
                        else
                        {
                            data[0].GrayScaleLevel = 0.0f;
                            data[0].TunnelVisionLevel = 0.0f;
                            data[0].RedoutLevel = 0.0f;
                            data[0].FilmGrainLevel = 0.0f;
                            data[0].LoCLevel = 0.0f;
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

                        Span<double> weights = stackalloc double[GaussianBlurMaxRadius + 1];

                        CalculateGaussianWeights(0.0, weights, out int shaderRadius);
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

                    if (GEffectBuffer.LookupSpan != null)
                    {
                        Span<GEffectBuffer> data = GEffectBuffer.LookupSpan(GEffectBufferHash);
                        data[0].GrayScaleLevel = 0.0f;
                        data[0].TunnelVisionLevel = 0.0f;
                        data[0].RedoutLevel = 0.0f;
                        data[0].FilmGrainLevel = 0.0f;
                        data[0].LoCLevel = 0.0f;
                    }
                }

                ImGui.BeginTable("GEffectsInstancesTable", 11, ImGuiTableFlags.Borders | ImGuiTableFlags.Resizable);
                ImGui.TableSetupColumn("Vehicle ID", ImGuiTableColumnFlags.WidthFixed, initWidthOrWeight: 150f);
                ImGui.TableSetupColumn("Gz", ImGuiTableColumnFlags.WidthFixed, initWidthOrWeight: 100f);
                ImGui.TableSetupColumn("Consciousness", ImGuiTableColumnFlags.WidthFixed, initWidthOrWeight: 100f);
                ImGui.TableSetupColumn("Grayscale", ImGuiTableColumnFlags.WidthFixed, initWidthOrWeight: 100f);
                ImGui.TableSetupColumn("Tunnel Vision", ImGuiTableColumnFlags.WidthFixed, initWidthOrWeight: 100f);
                ImGui.TableSetupColumn("Redout", ImGuiTableColumnFlags.WidthFixed, initWidthOrWeight: 100f);
                ImGui.TableSetupColumn("Film Grain", ImGuiTableColumnFlags.WidthFixed, initWidthOrWeight: 100f);
                ImGui.TableSetupColumn("Blur", ImGuiTableColumnFlags.WidthFixed, initWidthOrWeight: 100f);
                ImGui.TableSetupColumn("Visual LoC", ImGuiTableColumnFlags.WidthFixed, initWidthOrWeight: 100f);
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
                    ImGui.PushID($"{item.Key}_Grayscale");
                    ImGui.Text($"{item.Value.VisualGrayscaleLevel:f4}");
                    ImGui.PopID();
                    ImGui.TableNextColumn();
                    ImGui.PushID($"{item.Key}_TunnelVision");
                    ImGui.Text($"{item.Value.VisualTunnelVisionLevel:f4}");
                    ImGui.PopID();
                    ImGui.TableNextColumn();
                    ImGui.PushID($"{item.Key}_Redout");
                    ImGui.Text($"{item.Value.VisualRedoutLevel:f4}");
                    ImGui.PopID();
                    ImGui.TableNextColumn();
                    ImGui.PushID($"{item.Key}_FilmGrain");
                    ImGui.Text($"{item.Value.VisualFilmGrainLevel:f4}");
                    ImGui.PopID();
                    ImGui.TableNextColumn();
                    ImGui.PushID($"{item.Key}_Blur");
                    ImGui.Text($"{item.Value.VisualBlurLevel:f4}");
                    ImGui.PopID();
                    ImGui.TableNextColumn();
                    ImGui.PushID($"{item.Key}_VisualLoC");
                    ImGui.Text($"{item.Value.VisualLoCLevel:f4}");
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
