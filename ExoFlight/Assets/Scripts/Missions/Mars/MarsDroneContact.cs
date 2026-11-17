using UnityEngine;

namespace ExoFlight.Missions.Mars
{
    public sealed class MarsDroneContact : MonoBehaviour
    {
        public MarsMissionDirector mission;
        float nextImpact;
        void OnCollisionEnter(Collision collision)
        {
            if (mission == null || Time.time < nextImpact || !mission.drone.Armed || !mission.BriefingFinished) return;
            float speed = collision.relativeVelocity.magnitude;
            if (speed < 2f) return;
            nextImpact = Time.time + 15f;
            if (speed < 12f) mission.voice.Hint("EV-04", 20f);
            else
            {
                mission.voice.Say("EV-06");
                mission.drone.SetArmed(false);
                mission.FailMission();
            }
        }
    }
}
