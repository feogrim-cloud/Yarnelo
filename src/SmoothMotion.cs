using System;

namespace Polka {
    // A critically damped spring. Retargeting preserves both the displayed
    // position and velocity, so pointer and drag changes never restart motion.
    internal sealed class MotionValue {
        public double Value;
        public double Velocity;
        public double Target;

        public MotionValue(double value) {
            Reset(value);
        }

        public void Reset(double value) {
            Value = value;
            Target = value;
            Velocity = 0;
        }

        public void Step(double dt, double response) {
            if (dt <= 0 || response <= 0 || Double.IsNaN(dt) || Double.IsNaN(response)) return;

            double time = response * dt;
            // Past this point the analytic remainder is negligible. Handling
            // it explicitly also avoids overflow after suspension/debugging.
            if (time >= 50) {
                SnapToTarget();
                return;
            }

            double displacement = Value - Target;
            double combinedVelocity = Velocity + response * displacement;
            double decay = Math.Exp(-time);
            Value = Target + (displacement + combinedVelocity * dt) * decay;
            Velocity = (Velocity - response * combinedVelocity * dt) * decay;
        }

        public bool IsSettled(double tolerance) {
            return Math.Abs(Value - Target) <= tolerance && Math.Abs(Velocity) <= tolerance;
        }

        public void SnapToTarget() {
            Value = Target;
            Velocity = 0;
        }
    }
}
