using GEffectsLogic;
using System;
using System.Collections.Generic;
using System.Text;

namespace KSAGEffects
{
    public class KSAGEffectsLogicInstance : GEffectsLogicInstance, IDisposable
    {
        private static Dictionary<string, KSAGEffectsLogicInstance> namedInstances = new Dictionary<string, KSAGEffectsLogicInstance>();
        public static Dictionary<string, KSAGEffectsLogicInstance> NamedInstances => namedInstances;

        public static string GetInstanceName(int index) => NamedInstances.FirstOrDefault(kvp => kvp.Value.UniqueID == index).Value?.VehicleId ?? "Unknown";

        public string VehicleId { get; private set; }
        public bool Enabled { get; set; } = true;

        public override void Update(double deltaTime, double currentGx, double currentGy, double currentGz)
        {
            if (Enabled) base.Update(deltaTime, currentGx, currentGy, currentGz);
        }

        public void Dispose()
        {
            namedInstances.Remove(VehicleId);
        }

        public KSAGEffectsLogicInstance(string vehicleId) : base()
        {
            VehicleId = vehicleId;
            namedInstances.Add(vehicleId, this);
        }
    }
}
