using GEffectsLogic;
using System;
using System.Collections.Generic;
using System.Text;

namespace KSAGEffects
{
    public class NamedGEffectsLogicInstance : GEffectsLogicInstance, IDisposable
    {
        private static Dictionary<string, NamedGEffectsLogicInstance> namedInstances = new Dictionary<string, NamedGEffectsLogicInstance>();
        public static Dictionary<string, NamedGEffectsLogicInstance> NamedInstances => namedInstances;

        public static string GetInstanceName(int index) => NamedInstances.FirstOrDefault(kvp => kvp.Value.UniqueID == index).Value?.VehicleId ?? "Unknown";

        public string VehicleId { get; private set; }

        public void Dispose()
        {
            namedInstances.Remove(VehicleId);
        }

        public NamedGEffectsLogicInstance(string vehicleId) : base()
        {
            VehicleId = vehicleId;
            namedInstances.Add(vehicleId, this);
        }
    }
}
