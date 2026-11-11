using UnityEngine;

namespace ExoFlight.Flight
{
    /// <summary>
    /// Dust thrown up by the rotors: emission follows throttle, and only happens while the
    /// drone is low enough for downwash to reach the ground.
    /// </summary>
    public sealed class RotorDust : MonoBehaviour
    {
        public DroneFlightController drone;
        public ParticleSystem dust;
        public float baseRate = 140f;
        [Tooltip("Height above the ground where downwash still lifts dust")]
        public float groundRange = 3.5f;
        [Tooltip("Throttle below this throws no dust")]
        public float minThrottle = .25f;

        void Update()
        {
            if (drone == null || dust == null) return;

            float rate = 0f;
            if (drone.Armed)
            {
                float throttle = drone.Input.Throttle;
                float power = Mathf.InverseLerp(minThrottle, 1f, throttle);

                float ground = 0f;
                Vector3 origin = drone.transform.position + Vector3.up * .2f;
                if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, groundRange))
                    ground = 1f - Mathf.Clamp01(hit.distance / groundRange);

                rate = baseRate * power * ground;
            }

            var emission = dust.emission;
            emission.rateOverTimeMultiplier = rate;
        }
    }
}
