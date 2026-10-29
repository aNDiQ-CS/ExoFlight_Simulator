using UnityEngine;

namespace ExoFlight.Flight
{
    [CreateAssetMenu(menuName = "ExoFlight/Flight/Wind Profile")]
    public sealed class WindProfile : ScriptableObject
    {
        [Header("Mean wind (world space, m/s)")]
        public Vector3 baseVelocity;
        [Tooltip("Slow pulsation of the mean wind speed, m/s")]
        [Min(0f)] public float wanderSpeed;
        [Min(1f)] public float wanderPeriod = 40f;

        [Header("Gusts: divergence-free field (curl of noise)")]
        [Tooltip("Gust amplitude, m/s. Zero means a steady mean wind only")]
        [Min(0f)] public float gustSpeed;
        [Tooltip("Eddy size, m")]
        [Min(1f)] public float gustScale = 30f;
        [Tooltip("How fast eddies are carried downwind, m/s. Zero takes the mean wind speed")]
        [Min(0f)] public float advectionSpeed;
        [Range(1, 4)] public int octaves = 2;

        [Header("Altitude profile")]
        [Tooltip("Height at which the wind reaches full speed, m")]
        [Min(1f)] public float fullSpeedAltitude = 20f;
        [Tooltip("Share of the full speed at the surface")]
        [Range(0f, 1f)] public float groundSpeedFactor = .5f;

        [Header("General")]
        public int seed = 12345;
        [Tooltip("Multiplies the wind speed, not the force. 1 is physical; force grows with its square")]
        [Range(0f, 4f)] public float speedScale = 1f;
    }
}
