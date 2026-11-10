# Future improvement: per-postfix streaming dispatch (Option C)

Possible alternative to the centralized per-frame wave in
[parallel-glogic-execution.md](parallel-glogic-execution.md). Not implemented —
documented here for later evaluation.

## Concept

Instead of a single `ApplyResultsToMainThread` postfix that snapshots all
applied bubbles on the main thread, each `PhysicsBubble.ApplyResultsToVehicles`
postfix (running on vehicle worker threads) dispatches its own work
immediately:

1. Postfix merges the kitten's physics input into the instance's pending
   fields (`Σdt`, `Σg·dt`), then tries to CAS-claim the instance via
   `Interlocked.CompareExchange` on a per-instance `_inFlight` flag.
2. Claimed instances are collected into a per-postfix list; the postfix
   launches one `Task.Run(() => Parallel.ForEach(claimed, DrainPending))`
   per bubble — per-kitten parallelism is preserved because a multi-crew
   vessel lives in a single bubble.
3. The worker drains pending in a loop: take pending → `Update` → release
   `_inFlight` → re-check pending → re-claim if new input arrived during the
   update. The loop self-drains backlog; if a postfix sees `_inFlight` still
   set it just merges and returns — the running worker picks it up.

## Benefits vs Option A

- Starts ~0.1–1 ms earlier (no waiting for the whole apply batch plus the
  synchronized/render-event tails). Bounded benefit: the wave's real
  deadline is `AfterGui` (~3–8 ms later), which both options meet
  comfortably.
- Per-instance self-drain instead of all-or-nothing wave skip — only
  overloaded instances lag.

## Costs / risks

- The pending-merge vs drain-take race needs a tiny per-instance lock or
  `Interlocked` fields — pending fields would have a true second writer
  (postfixes on vehicle worker threads vs draining workers), unlike
  Option A where the pending channel is single-threaded by construction.
- The claim → release → re-check → re-claim ordering is subtle; an
  incorrect sequence either loses enqueued work or double-drains an
  instance (`PhysModel` data race).
- More ThreadPool churn: one task per bubble per frame instead of one per
  frame (negligible in practice).
- Work runs inside the apply window and may contend with remaining apply
  jobs on VehicleWorkerPool threads — ThreadPool `Normal` priority already
  yields to KSA's `Highest`-priority workers, so the effect is small.
- Note: Option A's earlier disadvantages (second Harmony patch, dirty bag,
  dedup flag) no longer apply — it now uses a single patch with a
  main-thread snapshot loop over `SyncWindowBubbles`, leaving "earlier
  start + finer drain" as Option C's only real edge.

## Rejected variant: dedicated persistent threads (Option B)

Persistent consumer threads on a `BlockingCollection` were considered and
rejected:

- The early-start delta vs Option A is sub-millisecond; the wave's real
  deadline is `AfterGui` (~3–8 ms later) which all options meet
  comfortably.
- Persistent threads add lifecycle management (start/stop, mod unload,
  queue disposal) for no measurable benefit.
- They can contend with KSA's apply jobs inside the main-thread barrier
  window; the only mitigation they uniquely offer — `BelowNormal` thread
  priority — is already approximated by `Normal`-priority ThreadPool items
  vs KSA's `Highest`-priority vehicle workers.
