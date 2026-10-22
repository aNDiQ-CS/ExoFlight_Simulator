using UnityEngine;

namespace ExoFlight.Flight
{
    /// <summary>Use 100 Hz for this scene, and restore the project's clock on exit.</summary>
    public sealed class FlightSimulationClock : MonoBehaviour
    {
        float previousStep;
        void OnEnable()
        {
            previousStep = Time.fixedDeltaTime;
            Time.fixedDeltaTime = .01f;
        }
        void OnDisable() => Time.fixedDeltaTime = previousStep;
    }
}
