using GEffectsLogic;
using KSA;
using KSAGEffects.Logging;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text;

namespace KSAGEffects
{
    public class KSAGEffectsLogicInstance : GEffectsLogicInstance, IDisposable
    {
        private static readonly ConditionalWeakTable<string, KSAGEffectsLogicInstance> namedInstances = new();
        public static ConditionalWeakTable<string, KSAGEffectsLogicInstance> NamedInstances => namedInstances;

        private readonly object _stateLock = new();
        private long _lastUpdateTickCount;

        public string KittenId { get; private set; }
        public long LastUpdateTickCount => Interlocked.Read(ref _lastUpdateTickCount);
        public bool IsUpdateRecent =>
            LastUpdateTickCount != 0 && Environment.TickCount64 - LastUpdateTickCount <= 5000;
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
            lock (_stateLock)
            {
                if (!Enabled) return;
                base.Update(deltaTime, currentGx, currentGy, currentGz);
                Interlocked.Exchange(ref _lastUpdateTickCount, Environment.TickCount64);
            }
        }

        public override void Reset()
        {
            lock (_stateLock)
            {
                base.Reset();
                Interlocked.Exchange(ref _lastUpdateTickCount, 0);
            }
        }

        public new string CreateDiagnosticDump()
        {
            string modelDump;
            long lastUpdateTickCount;
            bool isEnabled;
            lock (_stateLock)
            {
                modelDump = base.CreateDiagnosticDump();
                lastUpdateTickCount = LastUpdateTickCount;
                isEnabled = Enabled;
            }

            var builder = new StringBuilder();
            builder.AppendLine("KSAInstance");
            builder.Append("KittenId=").AppendLine(KittenId);
            builder.Append("Enabled=").AppendLine(isEnabled.ToString());
            builder.Append("HasUpdated=").AppendLine((lastUpdateTickCount != 0).ToString());
            builder.Append("LastUpdateAgeMilliseconds=").AppendLine(
                lastUpdateTickCount == 0
                    ? "unavailable"
                    : (Environment.TickCount64 - lastUpdateTickCount).ToString(System.Globalization.CultureInfo.InvariantCulture));
            builder.Append(modelDump);
            return builder.ToString();
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
