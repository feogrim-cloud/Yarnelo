using System;
using System.Collections.Generic;

namespace Polka {
    // Deterministic time-based easing, separate from icon springs. Retargeting
    // captures current visual values in the caller and starts a fresh interval.
    internal sealed class ThemeProgress {
        internal const double Duration=.30;
        internal bool Active { get; private set; }
        internal double Elapsed { get; private set; }
        internal double Amount { get; private set; }
        internal static double ForegroundAmount(double amount,bool polarityChanges) {
            if(!polarityChanges) return amount;
            // Algebraically (.35,.65), written to keep amount=.5 exactly .5
            // before byte rounding the displayed color channels.
            double t=Math.Max(0,Math.Min(1,(amount*20-7)/6));
            return t*t*(3-2*t);
        }
        internal void Start() { Active=true; Elapsed=0; Amount=0; }
        internal void Advance(double dt) {
            if(!Active || !Store.IsFinite(dt) || dt<=0) return;
            Elapsed=Math.Min(Duration,Elapsed+dt);
            if(Duration-Elapsed<1e-12) Elapsed=Duration;
            double t=Elapsed/Duration;
            if(Math.Abs(t-.5)<1e-12) t=.5;
            Amount=t*t*(3-2*t);
            if(Elapsed>=Duration) { Amount=1; Active=false; }
        }
        internal void Cancel() { Active=false; }
    }

    internal sealed class ThemeMotion {
        static readonly Dictionary<string,ThemeMotion> cache=new Dictionary<string,ThemeMotion>();
        internal double Response=25, HoverLift=6, ClickPop=.19, ClickHop=12, ClickDuration=.43, PopDuration=.36,
            ClickResponse=30, PressResponse=34, PressScale=.93, LiftResponse=25, ShiftResponse=26, AlphaResponse=35,
            DragResponse=55, DragScaleResponse=28, DragAlphaResponse=35, PlaceholderResponse=28,
            DragScale=1.10, DragLift=7, DragTilt=5;
        internal static ThemeMotion For(string theme) {
            string id=ThemeCatalog.Normalize(theme);
            lock(cache) { ThemeMotion motion; if(!cache.TryGetValue(id,out motion)) { motion=Create(id); cache.Add(id,motion); } return motion; }
        }
        static ThemeMotion Create(string theme) {
            var value=new ThemeMotion();
            switch(ThemeCatalog.Normalize(theme)) {
                case "frost": value.Response=21; value.HoverLift=5; value.ClickPop=.16; value.ClickHop=9; value.ClickDuration=.46; value.PopDuration=.39; value.ClickResponse=26; value.PressResponse=29; value.PressScale=.94; value.LiftResponse=22; value.ShiftResponse=23; value.DragResponse=48; value.DragScaleResponse=24; value.PlaceholderResponse=24; value.DragScale=1.08; value.DragLift=6; value.DragTilt=3; break;
                case "graphite": value.Response=32; value.HoverLift=8; value.ClickPop=.12; value.ClickHop=7; value.ClickDuration=.30; value.PopDuration=.26; value.ClickResponse=36; value.PressResponse=39; value.PressScale=.96; value.LiftResponse=32; value.ShiftResponse=33; value.AlphaResponse=39; value.DragResponse=66; value.DragScaleResponse=34; value.PlaceholderResponse=34; value.DragScale=1.05; value.DragLift=5; value.DragTilt=2; break;
                case "ember": value.Response=24; value.HoverLift=7; value.ClickPop=.14; value.ClickHop=8; value.ClickDuration=.42; value.PopDuration=.35; value.ClickResponse=29; value.PressResponse=32; value.PressScale=.95; value.LiftResponse=24; value.ShiftResponse=25; value.DragResponse=51; value.DragScaleResponse=27; value.PlaceholderResponse=26; value.DragScale=1.07; value.DragLift=6; value.DragTilt=3; break;
                case "aurora": value.Response=27; value.HoverLift=8; value.ClickPop=.20; value.ClickHop=10; value.ClickDuration=.46; value.PopDuration=.38; value.ClickResponse=32; value.PressResponse=36; value.LiftResponse=28; value.ShiftResponse=29; value.DragResponse=59; value.DragScaleResponse=30; value.PlaceholderResponse=30; value.DragScale=1.12; value.DragLift=8; value.DragTilt=5; break;
            }
            return value;
        }
    }

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
