using Brutal.ImGuiApi;
using Brutal.Numerics;
using Core;
using HarmonyLib;
using KSA;
using KSAGEffects.Logging;
using KSAGEffects.Shaders;
using KSAGEffects.Ui;
using StarMap.API;
using StarMap.API.Services;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Tomlet;
using Tomlet.Attributes;

namespace KSAGEffects
{
    [StarMapMod]
    [HarmonyPatch]
    public class KSAGEffects
    {
        private readonly IModMenuService _modMenu;
        private readonly GEffectsPlayerUi _playerUi = new();
        private readonly GEffectsDebugUi _debugUi = new();
        private IDisposable? _statusMenuItem;
        private IDisposable? _diagnosticsMenuItem;

        public KSAGEffects(IModMenuService modMenu)
        {
            _modMenu = modMenu;
        }

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
            _statusMenuItem = _modMenu.AddModMenuItem("G Effects status", _playerUi.OpenExpanded);
            if (ReadDebugFlag())
                _diagnosticsMenuItem = _modMenu.AddModMenuItem("G Effects diagnostics", () => _debugUi.IsOpen = true);
        }

        [StarMapUnload]
        public void Unload()
        {
            _statusMenuItem?.Dispose();
            _diagnosticsMenuItem?.Dispose();
        }

        private static bool ReadDebugFlag()
        {
            try
            {
                LocalManifest manifest = TomletMain.To<LocalManifest>(File.ReadAllText(ModLibrary.LocalManifestPath));
                return manifest.Mods.FirstOrDefault(mod => mod.Id == "KSAGEffects" && mod.Enabled)?.Debug ?? false;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private sealed class LocalManifest
        {
            [TomlDoNotInlineObject]
            [TomlField("mods")]
            public List<LocalModEntry> Mods = [];
        }

        private sealed class LocalModEntry
        {
            [TomlField("id")]
            public string Id = "";

            [TomlField("enabled")]
            public bool Enabled = true;

            [TomlField("debug")]
            public bool Debug = false;
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
        // unless a powered probe core takes over — mirroring the game's own
        // incapacitation rule. The game resolves control state in
        // Vehicle.ScanControls/ComputeControl: command-pod crew feed
        // CommandPodCrew and probe cores feed ProbeCorePower. G-unconsciousness is
        // an additional incapacitation cause, so downgrade pod Able ->
        // Incapacitated and let the game's probe-core power model decide.
        // This plugs into the game's ControlsLockout path (OnKey rejection,
        // flight computer gating, "out of control" alert).
        [HarmonyPatch(typeof(Vehicle), "ScanControls"), HarmonyPostfix]
        public static void Vehicle_ScanControls_Postfix(Vehicle __instance, ref CommandPodCrew pod)
        {
            if (pod == CommandPodCrew.Able && EvaluateCrewGState(__instance) == CrewGState.AllUnconscious)
                pod = CommandPodCrew.Incapacitated;
        }

        public static double StandardGravity => KSA.Constants.STANDARD_GRAVITY;
        public static float vignetteShape = 2.0f; // 1.0 is circular, higher stretches it into an oval
        public static float screenSizeAdjustment = 1.0f; // Screen ratio adjustment for the vignette effect, includes vignetteShape
        public static float edgeDistance = 1.0f;

        [StarMapAfterGui]
        public unsafe void AfterGui(double dt)
        {
            Vehicle? activeVehicle = Program.ControlledVehicle;
            bool probeCoreMode = false;
            CrewGState crewGState = activeVehicle == null ? CrewGState.NoCrew : EvaluateCrewGState(activeVehicle);
            if (activeVehicle != null &&
                crewGState == CrewGState.AllUnconscious &&
                !OverrideIsControllableRef() &&
                activeVehicle.IsControllable)
            {
                probeCoreMode = true;
                if (!_probeCoreAlertActive)
                {
                    TimedAlert.CreateWarning(activeVehicle.Id + ": all crew unconscious, probe core engaged", 10.0);
                }
            }
            _probeCoreAlertActive = probeCoreMode;

            if (GEffectBuffer.LookupSpan != null)
            {
                Span<GEffectBuffer> data = GEffectBuffer.LookupSpan(GEffectBufferHash);
                float2 screenSize = new(Program.MainViewport.Size.X, Program.MainViewport.Size.Y);
                data[0].ScreenSizeAdjustment = vignetteShape * screenSize.Y / screenSize.X;
                data[0].TunnelVisionColor = new float4(0.0f, 0.0f, 0.0f, 1.0f);
                data[0].RedoutColor = new float4(1.0f, 0.0f, 0.0f, 1.0f);
                data[0].LoCColor = new float4(0.0f, 0.0f, 0.0f, 1.0f);
                Renderer renderer = Program.GetRenderer();
                data[0].FilmGrainData.X = renderer.Extent.Width;
                data[0].FilmGrainData.Y = renderer.Extent.Height;
                data[0].FilmGrainData.Z = 2.0f;
                data[0].FilmGrainData.W += (float)dt;
            }

#if DEBUG
            SyncVirtualInstances();
#endif
            List<KSAGEffectsLogicInstance> crewInstances = activeVehicle == null
                ? []
                : GetVehicleInstances(activeVehicle);
            if (activeVehicle is KittenEva)
                crewInstances.Add(GetLogicInstance(activeVehicle.Id));
            KSAGEffectsLogicInstance? instance = crewInstances.Count == 0 ? null
                : crewGState == CrewGState.AllUnconscious
                    ? crewInstances.MinBy(item => item.ConsciousnessLevel)
                    : crewInstances.MaxBy(item => item.ConsciousnessLevel);

            UpdateBlur(instance, probeCoreMode);
            UpdateVisualEffects(instance, probeCoreMode);

            string crewStatus = activeVehicle switch
            {
                null => "No controlled vessel; current G-force status is unavailable.",
                KittenEva => "EVA crew member; showing their current G-force response.",
                _ when probeCoreMode => "All assigned crew are G-unconscious; the probe core is controlling the vessel.",
                _ when crewGState == CrewGState.NoCrew => "No living crew are assigned; crew G-effects cannot be assessed.",
                _ when crewGState == CrewGState.AllUnconscious => "All assigned crew are G-unconscious; vessel control is unavailable.",
                _ => "Vessel control is available; at least one crew member can still fly."
            };
            _playerUi.Draw(activeVehicle, instance, crewStatus, crewInstances);
            _debugUi.Draw();
        }

        private static unsafe void UpdateBlur(KSAGEffectsLogicInstance? instance, bool probeCoreMode)
        {
            if (GEffectsBlurPushConstantsBuffer.LookupSpan == null) return;
            Span<GEffectsBlurPushConstantsBuffer> dataHorizontal = GEffectsBlurPushConstantsBuffer.LookupSpan(GaussianBlurHorizontalHash);
            ref GEffectsBlurPushConstantsBuffer blurHorizontal = ref dataHorizontal[0];
            Span<GEffectsBlurPushConstantsBuffer> dataVertical = GEffectsBlurPushConstantsBuffer.LookupSpan(GaussianBlurVerticalHash);
            ref GEffectsBlurPushConstantsBuffer blurVertical = ref dataVertical[0];
            Span<double> weights = stackalloc double[GaussianBlurMaxRadius + 1];
            float radius = instance is { Enabled: true } && !probeCoreMode
                ? GaussianBlurMaxRadius * (float)instance.VisualBlurLevel
                : 0.0f;
            CalculateGaussianWeights(radius, weights, out int shaderRadius);
            blurHorizontal.Radius = shaderRadius;
            blurVertical.Radius = shaderRadius;
            fixed (float* destination = blurHorizontal.Weights)
            {
                for (int i = 0; i <= GaussianBlurMaxRadius; i++)
                    destination[i] = (float)weights[i];
            }
            fixed (float* destination = blurVertical.Weights)
            {
                for (int i = 0; i <= GaussianBlurMaxRadius; i++)
                    destination[i] = (float)weights[i];
            }
        }

        private static void UpdateVisualEffects(KSAGEffectsLogicInstance? instance, bool probeCoreMode)
        {
            if (GEffectBuffer.LookupSpan == null) return;
            Span<GEffectBuffer> data = GEffectBuffer.LookupSpan(GEffectBufferHash);
            if (instance is not { Enabled: true })
            {
                data[0].GrayScaleLevel = 0.0f;
                data[0].TunnelVisionLevel = 0.0f;
                data[0].RedoutLevel = 0.0f;
                data[0].FilmGrainLevel = 0.0f;
                data[0].LoCLevel = 0.0f;
                return;
            }

            if (probeCoreMode)
            {
                data[0].GrayScaleLevel = 0.0f;
                data[0].TunnelVisionLevel = (float)(instance.VisualTunnelVisionLevel * ProbeCoreHintScale);
                data[0].RedoutLevel = (float)(instance.VisualRedoutLevel * ProbeCoreHintScale);
                data[0].FilmGrainLevel = 0.0f;
                data[0].LoCLevel = 0.0f;
                return;
            }

            data[0].GrayScaleLevel = (float)instance.VisualGrayscaleLevel;
            data[0].TunnelVisionLevel = (float)instance.VisualTunnelVisionLevel;
            data[0].RedoutLevel = (float)instance.VisualRedoutLevel;
            data[0].FilmGrainLevel = (float)instance.VisualFilmGrainLevel;
            data[0].LoCLevel = (float)instance.VisualLoCLevel;
        }
    }
}
