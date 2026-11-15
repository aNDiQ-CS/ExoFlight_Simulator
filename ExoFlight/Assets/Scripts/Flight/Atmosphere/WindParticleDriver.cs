using UnityEngine;

namespace ExoFlight.Flight
{
    /// <summary>
    /// Moves every live particle with the same wind field the drone feels,
    /// so dust flies exactly where the physics says the air goes.
    /// Velocity is refreshed every few frames — plenty for dust and cheap on mobile.
    /// </summary>
    public sealed class WindParticleDriver : MonoBehaviour
    {
        public FlightEnvironment environment;
        [Tooltip("1 = particles fully follow the air")]
        public float followSpeed = 1f;
        [Tooltip("Slight upward bias: real dust does not stay glued to the ground")]
        public float lift = .2f;
        public float maxSpeed = 55f;
        public int updateEveryFrames = 3;

        ParticleSystem ps;
        ParticleSystem.Particle[] buffer;
        int frame;

        void Awake() => ps = GetComponent<ParticleSystem>();

        void Update()
        {
            if (ps == null || environment == null || environment.windProfile == null) return;
            if (frame++ % Mathf.Max(1, updateEveryFrames) != 0) return;

            int max = ps.main.maxParticles;
            if (buffer == null || buffer.Length < max) buffer = new ParticleSystem.Particle[max];

            int count = ps.GetParticles(buffer);
            float time = Time.time;
            for (int i = 0; i < count; i++)
            {
                Vector3 wind = WindField.Sample(environment.windProfile, buffer[i].position, time) * followSpeed;
                wind.y += lift;
                buffer[i].velocity = Vector3.ClampMagnitude(wind, maxSpeed);
            }
            ps.SetParticles(buffer, count);
        }
    }
}
