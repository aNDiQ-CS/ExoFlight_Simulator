using UnityEngine;

namespace ExoFlight.Flight
{
    /// <summary>
    /// Dust blown off the propeller discs: rate follows throttle while the drone is armed.
    /// </summary>
    public sealed class RotorWash : MonoBehaviour
    {
        public DroneFlightController drone;
        public ParticleSystem[] wash = new ParticleSystem[DroneFlightController.MotorCount];
        public float baseRate = 55f;
        [Tooltip("Throttle below this leaves the props dry")]
        public float minThrottle = .15f;

        void Update()
        {
            if (drone == null) return;

            float rate = 0f;
            if (drone.Armed)
                rate = baseRate * Mathf.InverseLerp(minThrottle, 1f, drone.Input.Throttle);

            for (int i = 0; i < wash.Length; i++)
            {
                if (wash[i] == null) continue;
                var emission = wash[i].emission;
                emission.rateOverTimeMultiplier = rate;
            }
        }
    }
}
