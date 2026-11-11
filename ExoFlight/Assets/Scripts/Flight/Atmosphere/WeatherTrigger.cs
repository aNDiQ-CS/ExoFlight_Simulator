using UnityEngine;

namespace ExoFlight.Flight
{
    /// <summary>
    /// Volume trigger that pushes weather into the level's WeatherDirector:
    /// fly into it and a storm (or any other kind) is queued for a set time.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class WeatherTrigger : MonoBehaviour
    {
        public WeatherDirector director;
        public WeatherKind kind = WeatherKind.Storm;
        public float seconds = 180f;
        [Tooltip("Fire only once, not every time the player re-enters")]
        public bool once = true;

        bool fired;

        void Reset() => GetComponent<Collider>().isTrigger = true;

        void OnTriggerEnter(Collider other)
        {
            if (director == null || (once && fired)) return;
            if (other.GetComponentInParent<Rigidbody>() == null) return;
            fired = true;
            director.Enqueue(kind, seconds);
        }
    }
}
