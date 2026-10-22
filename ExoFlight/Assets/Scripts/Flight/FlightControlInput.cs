using UnityEngine;

namespace ExoFlight.Flight
{
    public enum DroneFlightMode { Acro, Angle }

    /// <summary>Input contract for keyboard now and an RC transmitter later.</summary>
    public struct FlightControlInput
    {
        public float Throttle;
        // Positive pitch = nose down, yaw = right, roll = right.
        public float Pitch, Yaw, Roll;

        public FlightControlInput(float throttle, float pitch, float yaw, float roll)
        {
            Throttle = Mathf.Clamp01(throttle);
            Pitch = Mathf.Clamp(pitch, -1f, 1f);
            Yaw = Mathf.Clamp(yaw, -1f, 1f);
            Roll = Mathf.Clamp(roll, -1f, 1f);
        }
    }

    /// <summary>Frame-rate independent emulation of two transmitter gimbals.</summary>
    public sealed class KeyboardStickState
    {
        public float Throttle { get; private set; }
        public Vector3 Axes { get; private set; }

        public FlightControlInput Step(float throttleDirection, Vector3 directions,
            float dt, float throttleSpeed, float stickSpeed, float returnSpeed, bool precision)
        {
            dt = Mathf.Max(0f, dt);
            float scale = precision ? .25f : 1f;
            Throttle = Mathf.Clamp01(Throttle + throttleDirection * throttleSpeed * scale * dt);
            Axes = new Vector3(
                Advance(Axes.x, directions.x * scale, dt, stickSpeed, returnSpeed),
                Advance(Axes.y, directions.y * scale, dt, stickSpeed, returnSpeed),
                Advance(Axes.z, directions.z * scale, dt, stickSpeed, returnSpeed));
            return new FlightControlInput(Throttle, Axes.x, Axes.y, Axes.z);
        }

        static float Advance(float value, float target, float dt, float speed, float returnSpeed)
            => Mathf.MoveTowards(value, target, (Mathf.Approximately(target, 0f) ? returnSpeed : speed) * dt);

        public void Reset()
        {
            Throttle = 0f;
            Axes = Vector3.zero;
        }
    }
}
