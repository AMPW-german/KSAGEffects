using GEffectsLogic;
using KSA;
using KSAGEffects.Logging;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace KSAGEffects
{
    public class KSAGEffectsLogicInstance : GEffectsLogicInstance, IDisposable
    {
        private static readonly ConditionalWeakTable<string, KSAGEffectsLogicInstance> namedInstances = new();
        public static ConditionalWeakTable<string, KSAGEffectsLogicInstance> NamedInstances => namedInstances;

        public string KittenId { get; private set; }
        private volatile bool enabled = true;
        public bool Enabled
        {
            get => enabled;
            set => enabled = value;
        }

        private double _pendingDt;
        private double _pendingGxDt;
        private double _pendingGyDt;
        private double _pendingGzDt;

        // EVA locomotion servos emit high-frequency acceleration jitter that the
        // model would otherwise integrate as sustained load.
        private const double InputSmoothingTauSeconds = 0.4;
        private double _smoothGx;
        private double _smoothGy;
        private double _smoothGz;
        private bool _hasSmoothingState;

        internal void AddPendingInput(double dt, double gx, double gy, double gz, bool smoothInputs = false)
        {
            if (smoothInputs)
            {
                if (_hasSmoothingState)
                {
                    double alpha = 1.0 - Math.Exp(-dt / InputSmoothingTauSeconds);
                    _smoothGx += (gx - _smoothGx) * alpha;
                    _smoothGy += (gy - _smoothGy) * alpha;
                    _smoothGz += (gz - _smoothGz) * alpha;
                }
                else
                {
                    _smoothGx = gx;
                    _smoothGy = gy;
                    _smoothGz = gz;
                    _hasSmoothingState = true;
                }
                gx = _smoothGx;
                gy = _smoothGy;
                gz = _smoothGz;
            }

            // Gy and Gz are inverted from KSA
            _pendingDt += dt;
            _pendingGxDt += gx * dt;
            _pendingGyDt -= gy * dt;
            _pendingGzDt -= gz * dt;
        }

        internal bool TryTakePending(out double dt, out double gx, out double gy, out double gz)
        {
            if (_pendingDt <= 0 || !Enabled)
            {
                _pendingDt = 0;
                _pendingGxDt = 0;
                _pendingGyDt = 0;
                _pendingGzDt = 0;
                dt = gx = gy = gz = 0;
                return false;
            }

            dt = _pendingDt;
            gx = _pendingGxDt / dt;
            gy = _pendingGyDt / dt;
            gz = _pendingGzDt / dt;
            _pendingDt = 0;
            _pendingGxDt = 0;
            _pendingGyDt = 0;
            _pendingGzDt = 0;
            return true;
        }

        public override void Update(double deltaTime, double currentGx, double currentGy, double currentGz)
        {
            if (Enabled) base.Update(deltaTime, currentGx, currentGy, currentGz);
        }

        public void Dispose()
        {
            LogicLogging.Log($"Disposing KSAGEffectsLogicInstance for {KittenId}", this, GEffectsLogic.Logging.Logger.LogLevel.Info);
        }

        public KSAGEffectsLogicInstance(string kittenId) : base()
        {
            KittenId = kittenId;
        }
    }
}
