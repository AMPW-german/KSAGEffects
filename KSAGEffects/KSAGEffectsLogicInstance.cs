using GEffectsLogic;
using KSA;
using KSAGEffects.Logging;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace KSAGEffects
{
    public class KSAGEffectsLogicInstance : GEffectsLogicInstance, IDisposable
    {
        private static readonly ConditionalWeakTable<Vehicle, KSAGEffectsLogicInstance> namedInstances = new();
        public static ConditionalWeakTable<Vehicle, KSAGEffectsLogicInstance> NamedInstances => namedInstances;

        public string VehicleId { get; private set; }
        public bool Enabled { get; set; } = true;

        public override void Update(double deltaTime, double currentGx, double currentGy, double currentGz)
        {
            if (Enabled) base.Update(deltaTime, currentGx, currentGy, currentGz);
        }

        public void Dispose()
        {
            LogicLogging.Log($"Disposing KSAGEffectsLogicInstance for vehicle {VehicleId}", this, GEffectsLogic.Logging.Logger.LogLevel.Info);
        }

        public KSAGEffectsLogicInstance(string vehicleId) : base()
        {
            VehicleId = vehicleId;
        }
    }
}
