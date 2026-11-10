# Parallel background execution of GEffectsLogic instances

Move per-kitten `GEffectsLogicInstance.Update()` calls out of the `ApplyResultsToVehicles` sync window into a fire-and-forget `Task.Run` + `Parallel.ForEach` wave that executes during the UI/render phase, with pending inputs merged (dt-weighted average) when a wave overruns.

## Summary

`PhysicsBubble.ApplyResultsToVehicles` runs inside the main-thread `ApplyVehicleSolvers` sync window (blocking `ParallelBatch` barrier). The previous design called `Update()` serially per kitten (~0.1 ms each, spikes) on whichever thread picks up the single bubble job — often the main thread.

New design: **one** postfix on `VehicleUpdateTask.ApplyResultsToMainThread` (main thread, once per frame, after every bubble apply job is awaited) does two cheap things: (1) iterates the applied bubbles' vehicle states and snapshots each kitten's input — dt + seat-frame G vector — into pending fields on its `KSAGEffectsLogicInstance`; (2) drains the pending inputs into a flat work list and launches a fire-and-forget `Task.Run(() => Parallel.ForEach(items, ...))` wave on the .NET ThreadPool. The wave runs during KSA's UI/draw/render phase; shader/ImGui output may reflect one-frame-older values only during overrun spikes. Snapshot and drain both run on the main thread — the pending channel has exactly one writer and one reader, so no locks anywhere.

## Decisions

- Concurrency: `Task.Run` + `Parallel.ForEach` (same .NET ThreadPool substrate as Brutal's JobRunners; dynamic work stealing; exception-safe; minimal code). KSA's own `JobScheduler` instances cannot be reused mid-frame (`Queue()` throws unless all runners are idle).
- Overrun: never block — pending inputs merge and the wave is skipped until the previous one completes.
- Input merge: dt-weighted average G (`Σg·dt / Σdt`), matching how KSA time-weights `AccelerationBody`.
- Hardening: minimal — pending/flag mechanics + `volatile Enabled`; reads and `Reset()` stay unsynchronized (accepted exposure).
- Architecture: **Option A** (centralized per-frame wave). Option C (per-postfix streaming dispatch) is documented in [per-postfix-streaming-dispatch.md](per-postfix-streaming-dispatch.md) as a possible future improvement.
- Worker count: match KSA — `JobSystems.VehicleWorkerPool.WorkerCount` (read lazily at dispatch so it follows the `orbitSolvers` game setting).
- Single Harmony patch on `VehicleUpdateTask.ApplyResultsToMainThread`; `SyncWindowBubbles` (public) for the bubble list + cached `AccessTools.FieldRefAccess` for each bubble's `_vehicleStates`.

## Implementation Steps

### 1. `KSAGEffects/KSAGEffectsLogicInstance.cs` — pending input state

- Change `Enabled` auto-property to a `volatile bool` backed field (cross-thread visibility from UI toggle → worker).
- Add private pending fields: `_pendingDt`, `_pendingGxDt`, `_pendingGyDt`, `_pendingGzDt` (plain doubles — single writer/reader, see below).
- `internal void AddPendingInput(double dt, double gx, double gy, double gz)`: `_pendingDt += dt; _pendingGxDt += gx * dt; ...` (called only on the main thread).
- `internal bool TryTakePending(out double dt, out double gx, out double gy, out double gz)` (called only on the main thread):
  - If `_pendingDt <= 0` or `!Enabled`: zero the pending fields, return false. (Dropping pending while disabled prevents a giant merged dt when re-enabled; matches the old behavior where `Update` early-returns without advancing `time`.)
  - Otherwise compute `gx = _pendingGxDt / _pendingDt` (etc.), zero the pending fields, return true.

### 2. `KSAGEffects/GEffectsScheduler.cs` (new file) — dispatcher + background wave

```csharp
internal static class GEffectsScheduler
{
    private readonly record struct PendingUpdate(KSAGEffectsLogicInstance Instance, double Dt, double Gx, double Gy, double Gz);

    private static readonly List<PendingUpdate> _items = new(capacity: 64); // main-thread-owned; only mutated when _activeTask completed
    private static Task? _activeTask;
    private static bool _faultLogged;

    // Called once per frame on the main thread from the ApplyResultsToMainThread postfix.
    internal static void Dispatch(List<KSAGEffectsLogicInstance> touchedInstances)
    {
        Task? task = _activeTask;
        if (task is { IsCompleted: false }) return; // wave still running — pending keeps merging
        if (task is { IsFaulted: true } && !_faultLogged) { _faultLogged = true; /* log task.Exception via DefaultCategory.Log.Error */ }
        if (task is not { IsFaulted: true }) _faultLogged = false;

        _items.Clear();
        foreach (KSAGEffectsLogicInstance instance in touchedInstances)
            if (instance.TryTakePending(out double dt, out double gx, out double gy, out double gz))
                _items.Add(new PendingUpdate(instance, dt, gx, gy, gz));
        if (_items.Count == 0) return;

        List<PendingUpdate> items = _items;
        _activeTask = Task.Run(() => Parallel.ForEach(items,
            new ParallelOptions { MaxDegreeOfParallelism = JobSystems.VehicleWorkerPool?.WorkerCount ?? 4 },
            static w =>
            {
                try { w.Instance.Update(w.Dt, w.Gx, w.Gy, w.Gz); }
                catch (Exception e) { /* DefaultCategory.Log.Error — never let it reach the ThreadPool */ }
            }));
    }
}
```

- `MaxDegreeOfParallelism = JobSystems.VehicleWorkerPool.WorkerCount` reuses KSA's own vehicle-worker count (derived from the `orbitSolvers` setting), capped so the wave doesn't starve KSA's worker threads; read lazily so it follows setting changes. Fallback 4 if the pool isn't initialized.
- Per-item `try/catch` is mandatory: a leaked exception inside a work item would fault the wave task (unhandled ThreadPool exceptions are fatal).
- No `Wait()`/`Result` anywhere — fully fire-and-forget.

### 3. `KSAGEffects/KSAGEffects.cs` — single snapshot+dispatch postfix

Replace `VehicleUpdateTask_ApplyResultsToVehicles_Postfix` with:

```csharp
private static readonly AccessTools.FieldRef<PhysicsBubble, List<VehicleUpdateState>> VehicleStatesRef =
    AccessTools.FieldRefAccess<PhysicsBubble, List<VehicleUpdateState>>("_vehicleStates");

private static readonly List<KSAGEffectsLogicInstance> _touchedInstances = new(capacity: 64); // main thread only

[HarmonyPatch(typeof(VehicleUpdateTask), "ApplyResultsToMainThread"), HarmonyPostfix]
public static void VehicleUpdateTask_ApplyResultsToMainThread_Postfix(VehicleUpdateTask __instance)
{
    // Runs once per frame on the main thread after every bubble's apply job
    // finished; vehicle states and AccelerationBody are already updated.
    ReadOnlySpan<PhysicsBubble> bubbles = __instance.SyncWindowBubbles;
    _touchedInstances.Clear();

    foreach (PhysicsBubble bubble in bubbles)
    {
        if (!bubble.IsResultsReady) continue; // only ready bubbles applied results this frame
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
                instance.AddPendingInput(dt, g.X, g.Y, g.Z);
                _touchedInstances.Add(instance);
            }
            else for (int i = 0; i < vehicle.Crew.Length; i++)
            {
                // ... existing seat-axis transform math unchanged (forward/right/up,
                // gx/gy/gz dot products, KittenRoster.Find) ...
                KSAGEffectsLogicInstance instance = GetLogicInstance(kittenRosterEntryData.Name);
                instance.AddPendingInput(dt, gx, gy, gz);
                _touchedInstances.Add(instance);
            }
        }
    }

    GEffectsScheduler.Dispatch(_touchedInstances);
}
```

- `SyncWindowBubbles` is a public `ReadOnlySpan<PhysicsBubble>` on `VehicleUpdateTask`; it throws only while `Run()` is executing, which cannot overlap this call site (the previous step's `VehicleSolver.Wait()` completed before `ApplyVehicleSolvers`).
- `AccessTools.FieldRefAccess<PhysicsBubble, List<VehicleUpdateState>>("_vehicleStates")` is Harmony's cached compiled `ref` getter — faster and simpler than `BindingFlags` reflection per call.
- `!IsResultsReady` bubbles are skipped — identical semantics to the old postfix, which only ever ran inside `BubbleApplyResultsJob` for ready bubbles (failed bubbles never called `ApplyResultsToVehicles`).
- `vehicle.AccelerationBody` is read on the main thread after the apply batch's `Interlocked` completion counter drained — the worker writes are published, and it's the same value the old postfix read.
- Keep the `dt <= 0` guard, the `KittenRoster.Find` lookup (now on the main thread — strictly safer than on vehicle workers), and all seat-axis math identical.
- Remove the `PhysicsBubble.ApplyResultsToVehicles` patch; do not touch `AfterGui`, `Vehicle_OnKey_Prefix`, `GetVehicleInstances`, the commented-out `VehicleUpdateTask`/`DoWorkAndStageResults` code, or `negativeG`.

## Files to Modify

- `KSAGEffects/KSAGEffectsLogicInstance.cs` — pending-input fields, `AddPendingInput`, `TryTakePending`, `volatile Enabled`.
- `KSAGEffects/GEffectsScheduler.cs` — **new** dispatcher/wave runner.
- `KSAGEffects/KSAGEffects.cs` — replace apply postfix with single snapshot+dispatch postfix; add `VehicleStatesRef` + `_touchedInstances` fields.
- `.agents/plans/parallel-glogic-execution.md` — this file.
- `.agents/plans/per-postfix-streaming-dispatch.md` — future-improvement note (Option C).

No changes to `GEffectsLogic` (library), shaders, csproj, or AfterGui/OnKey code paths.

## Why no locks are needed anywhere

- `AddPendingInput` and `TryTakePending` both execute only on the main thread inside the same postfix — the pending channel is single-writer/single-reader by construction.
- During an overrun wave, workers only mutate `PhysModel`/`time`/`lastG*` via `Update`; the main-thread postfix merges only `_pending*` — disjoint fields.
- `_items`/`_touchedInstances` are only mutated on the main thread, and `_items` only after `_activeTask.IsCompleted` (task completion publishes worker writes) → single buffer is safe.
- Instance references held by the work list keep removed instances alive; `Update` on a roster-removed kitten is harmless.

## Verification

- [ ] `dotnet build KSAGEffects/KSAGEffects.csproj -c Release` compiles clean (requires the same `KSADir`/`KSAModsDir` env vars used for previous builds).
- [ ] In-game: G-effects debug window still shows updating `Gz`, `Consciousness`, and `Visual*` values during flight and burns.
- [ ] In-game: multi-kitten vessel (≥2 crew) — all instances in the instances table advance.
- [ ] In-game: time warp in stable orbit — `stable` early-out keeps waves trivially cheap; `Gz`/`LastGx` still refresh.
- [ ] In-game: blackout still blocks input via `Vehicle_OnKey_Prefix` (≤1 frame later than before — imperceptible).
- [ ] Optional: measure postfix+Dispatch main-thread cost (µs-scale snapshot math + drain vs ~0.1 ms × kittens today).

## Risks / Considerations

- **One-frame-stale outputs**: `AfterGui` (postfix of `OnDrawUiViewports`, before `ImGui.Render`) currently always sees same-frame values. After this change, outputs can lag one frame only when a wave hasn't finished by DrawUi — typical waves finish well before. Explicitly accepted.
- **Unsynchronized reads/Reset** (`Visual*`, `ConsciousnessLevel`, `Reset()`, `Enabled` toggles vs in-flight `Update`): `double` reads are atomic on x64; worst case is a torn *combination* of output fields — accepted per "minimal" scope.
- **Overrun coarsening**: sustained slow waves merge consecutive frames into dt-weighted-average inputs — correct total sim time, slightly smoothed G transients; only under spikes.
- **`Task` starvation**: if the .NET ThreadPool is saturated by KSA's own JobRunners, wave start may be delayed a frame — harmless (pending merges). ThreadPool grows on demand.
- **Disabled instances**: pending input is dropped while `Enabled == false`, mirroring the existing early-return semantics (no accumulated dt shock on re-enable).
- **Cost skew at high warp**: `Update(dt > 1 s)` still splits internally — expensive only during active maneuvers at warp, and now off the main thread instead of inside the apply barrier.
- **`SyncWindowBubbles` invariant**: relies on `ApplyResultsToMainThread` only ever being called after `VehicleSolver.Wait()` — true for the game's own call path; if a future KSA version calls it while `_isRunning`, the property throws inside our postfix (logged by Harmony, no crash).
- **Deferred alternatives**: Option C (per-postfix streaming dispatch) documented in [per-postfix-streaming-dispatch.md](per-postfix-streaming-dispatch.md); Option B (dedicated persistent threads) rejected — see that note.
