using UnityEngine;

namespace ExoFlight.Flight
{
    [CreateAssetMenu(menuName = "ExoFlight/Flight/Environment")]
    public sealed class FlightEnvironment : ScriptableObject
    {
        public Vector3 gravity = new Vector3(0f, -3.71f, 0f);
        [Min(0f)] public float airDensity = .016f;
        [Tooltip("World-space wind velocity, m/s. Shared by body aerodynamics.")]
        public Vector3 windVelocity;
        [Tooltip("Wind field. When empty, the constant windVelocity is used.")]
        public WindProfile windProfile;
    }
}
