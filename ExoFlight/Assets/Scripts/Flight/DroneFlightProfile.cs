using UnityEngine;

namespace ExoFlight.Flight
{
    /// <summary>SI units. An illustrative X quadcopter, not a measured real aircraft.</summary>
    [CreateAssetMenu(menuName = "ExoFlight/Flight/Drone Profile")]
    public sealed class DroneFlightProfile : ScriptableObject
    {
        public const int RotorCount = 4;
        [Header("Body (kg, m, kg m²)")]
        [Min(.01f)] public float mass = 2.5f;
        public Vector3 centerOfMass;
        public Vector3 principalInertia = new Vector3(.65f, 1.1f, .65f);
        [Min(.1f)] public float armLength = .75f;

        [Header("Four identical fixed-pitch rotors (X layout)")]
        [Min(.01f)] public float propellerDiameter = .75f;
        [Min(1f)] public float maximumRpm = 4800f;
        [Min(0f)] public float idleRpm = 600f;
        [Tooltip("Polar inertia of one spinning rotor, kg m²; illustrative until measured.")]
        [Min(0f)] public float rotorInertia = .0006f;
        [Min(.001f)] public float thrustCoefficient = .10f;
        [Min(.0001f)] public float torqueCoefficient = .008f;
        [Min(.001f)] public float spinUpTime = .065f;
        [Min(.001f)] public float spinDownTime = .045f;

        [Header("Body drag: Cd × area, local XYZ (m²)")]
        public Vector3 dragArea = new Vector3(.35f, .7f, .35f);
        [Tooltip("Approximate aerodynamic rotational drag, N m / ((kg/m³) (rad/s)²).")]
        [Min(0f)] public float rotationalDrag = .04f;

        [Header("Stick response")]
        [Tooltip("Allow the mixer to raise collective for attitude control at zero stick throttle.")]
        public bool airMode = true;
        [Tooltip("Pitch, yaw, roll maximum commanded rates in degrees/second.")]
        public Vector3 maximumRates = new Vector3(160f, 100f, 160f);
        [Range(0f, 1f)] public float expo = .35f;
        [Range(5f, 70f)] public float maximumTilt = 40f;
        [Min(.1f)] public float angleGain = 4f;

        [Header("Body-rate PI controller")]
        [Min(0f)] public float rateP = 8f;
        [Min(0f)] public float rateI = 1.5f;
        [Tooltip("Yaw PI multiplier; rotor spin inertia makes the yaw response faster than the static thrust mixer predicts.")]
        [Min(0f)] public float yawRateGain = .5f;
        [Min(0f)] public float integralLimit = 2f;

        public Vector3 RotorPosition(int index)
        {
            float angle = (45f + index * 90f) * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * armLength;
        }

        public float RotorSpin(int index) => index % 2 == 0 ? 1f : -1f;

        public float ThrustAtRpm(float rpm, float density)
        {
            float n = Mathf.Max(0f, rpm) / 60f;
            return thrustCoefficient * Mathf.Max(0f, density) * n * n
                * Mathf.Pow(propellerDiameter, 4f);
        }

        public float TorquePerThrust => torqueCoefficient / thrustCoefficient * propellerDiameter;
    }
}
