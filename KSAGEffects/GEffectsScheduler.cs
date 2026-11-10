// GEffectsLogic
// Copyright (C) 2026 AMPW
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <http://www.gnu.org/licenses/>.

using Core;
using KSA;

namespace KSAGEffects;

internal static class GEffectsScheduler
{
    private readonly record struct PendingUpdate(KSAGEffectsLogicInstance Instance, double Dt, double Gx, double Gy, double Gz);

    private static readonly List<PendingUpdate> _items = new(capacity: 64);
    private static Task? _activeTask;
    private static bool _faultLogged;

    internal static void Dispatch(List<KSAGEffectsLogicInstance> touchedInstances)
    {
        Task? task = _activeTask;
        if (task is { IsCompleted: false }) return;
        if (task is { IsFaulted: true } && !_faultLogged)
        {
            _faultLogged = true;
            Brutal.Logging.DefaultCategory.Log.Error($"G-effects update wave failed: {task.Exception}");
        }
        if (task is not { IsFaulted: true }) _faultLogged = false;

        _items.Clear();
        foreach (KSAGEffectsLogicInstance instance in touchedInstances)
            if (instance.TryTakePending(out double dt, out double gx, out double gy, out double gz))
                _items.Add(new PendingUpdate(instance, dt, gx, gy, gz));
        if (_items.Count == 0) return;

        List<PendingUpdate> items = _items;
        _activeTask = Task.Run(() => Parallel.ForEach(items,
            new ParallelOptions { MaxDegreeOfParallelism = JobSystems.VehicleWorkerPool?.WorkerCount ?? 4 },
            static update =>
            {
                try
                {
                    update.Instance.Update(update.Dt, update.Gx, update.Gy, update.Gz);
                }
                catch (Exception exception)
                {
                    Brutal.Logging.DefaultCategory.Log.Error($"G-effects update failed for {update.Instance.KittenId}: {exception}");
                }
            }));
    }
}
