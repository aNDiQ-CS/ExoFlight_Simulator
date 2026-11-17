using ExoFlight.Cargo;
using ExoFlight.Cargo.UI;
using ExoFlight.Flight;
using ExoFlight.Triggers;
using UnityEngine;

namespace ExoFlight.Missions.Mars
{
    public enum MarsMissionStage { Briefing, Outbound, Approach, Carrying, CanyonApproach, Canyon, Cave, CaveExit, Landing, Completed, Failed }

    /// <summary>Route progression is driven by the player's sensor and the actual cargo lifecycle.</summary>
    public sealed class MarsMissionDirector : MonoBehaviour
    {
        public DroneFlightController drone;
        public CargoDeliveryMission cargoMission;
        public MarsVoicePlayer voice;
        public MarsMissionZone[] zones;
        public MarsStorm storm;
        public Light headlight;
        public CargoOnboardDisplay display;
        public Transform navigationBeacon;
        public Transform landingPoint;
        public CargoMissionAssist assist;
        public float stormDelay = 90f;
        public MarsMissionStage Stage { get; private set; }
        public int NextZoneIndex { get; private set; }
        public float HeightAboveGround { get; private set; }
        public bool MissionComplete => Stage == MarsMissionStage.Completed;
        public bool BriefingFinished => Stage != MarsMissionStage.Briefing;
        public Transform NextTarget => cargoMission.State == CargoMissionState.AwaitingPickup && Stage == MarsMissionStage.Approach
            ? cargoMission.pickupZone.transform : NextZoneIndex < zones.Length ? zones[NextZoneIndex].transform : landingPoint;
        float stageTime, flightTime, stillTime, noYawTime, awayTime, approachTime, stormTime;
        bool airborne, stormActive, touchedDown, pickupNear, failedAnnounced, recoveringCargo;
        Vector3 originalPickup;
        FlightEnvironment runtimeEnvironment;
        float baseDensity;

        void Awake()
        {
            // Weather belongs to this run. Never mutate the shared project environment asset.
            runtimeEnvironment = Instantiate(drone.environment);
            runtimeEnvironment.name = "Mars mission atmosphere (runtime)";
            runtimeEnvironment.windProfile = null;
            runtimeEnvironment.windVelocity = Vector3.zero;
            baseDensity = runtimeEnvironment.airDensity;
            drone.environment = cargoMission.cargo.environment = runtimeEnvironment;
            originalPickup = cargoMission.pickupZone.transform.position;
            cargoMission.pickedUp.AddListener(PickedUp);
            cargoMission.completed.AddListener(Delivered);
            cargoMission.restarted.AddListener(Restarted);
            cargoMission.lost.AddListener(LostCargo);
        }

        void Start() => BeginRun(false);

        void BeginRun(bool restarted)
        {
            Stage = MarsMissionStage.Briefing;
            NextZoneIndex = 0;
            stageTime = flightTime = stillTime = noYawTime = awayTime = approachTime = stormTime = 0f;
            airborne = stormActive = touchedDown = pickupNear = failedAnnounced = recoveringCargo = false;
            cargoMission.pickupAllowed = cargoMission.deliveryAllowed = false;
            cargoMission.pickupZone.transform.position = originalPickup;
            runtimeEnvironment.airDensity = baseDensity;
            runtimeEnvironment.windVelocity = Vector3.zero;
            if (storm != null) { storm.SetObserver(drone.transform); storm.EndStorm(); }
            if (headlight != null) headlight.enabled = false;
            voice.ResetPlayback();
            if (restarted) voice.Say("EV-07");
            voice.Say("S0-01", "S0-02", "S0-03", "S0-04", "S0-05");
        }

        public bool TryEnter(MarsMissionZone zone)
        {
            if (!isActiveAndEnabled || Stage == MarsMissionStage.Briefing || Stage == MarsMissionStage.Failed
                || MissionComplete || voice.IsBusy || NextZoneIndex >= zones.Length
                || zones[NextZoneIndex] != zone || zone.order != NextZoneIndex || !zone.Contains(drone.Body.position)
                || (zone.requiresCargo && cargoMission.carrier.CurrentCargo != cargoMission.cargo)) return false;
            if (zone.order == 0 && !airborne) return false;
            NextZoneIndex++;
            SetStage(zone.stage);
            voice.Say(zone.voiceIds);
            if (Stage == MarsMissionStage.Approach) cargoMission.pickupAllowed = true;
            if (Stage == MarsMissionStage.Canyon && storm != null) storm.EndStorm();
            if (Stage == MarsMissionStage.Cave && headlight != null) headlight.enabled = true;
            if (Stage == MarsMissionStage.Landing)
            {
                cargoMission.deliveryAllowed = true;
                stormActive = false;
                if (storm != null) storm.EndStorm();
                if (headlight != null) headlight.enabled = false;
            }
            Debug.Log($"[Mars route] {NextZoneIndex}/{zones.Length}: {zone.name}", this);
            return true;
        }

        void SetStage(MarsMissionStage value)
        {
            if (Stage == value) return;
            Stage = value;
            stageTime = 0f;
        }

        void PickedUp()
        {
            recoveringCargo = false;
            SetStage(MarsMissionStage.Carrying);
            cargoMission.pickupAllowed = false;
            voice.Say("S3-01");
            voice.SayAfter(2f, "S3-02", "S3-03");
            StartStorm();
        }

        void Delivered()
        {
            touchedDown = true;
            voice.Say("S6-06");
            // The motors-off line waits for an actual landing and disarm, not just a dropped payload.
        }

        void Restarted() => BeginRun(Stage == MarsMissionStage.Failed);

        void LostCargo()
        {
            recoveringCargo = true;
            Vector3 point = cargoMission.cargo.Body.position;
            cargoMission.pickupZone.transform.position = new Vector3(point.x, point.y - .5f, point.z);
            // Keep the remaining route, but require recapture before its next zone.
            SetStage(MarsMissionStage.Approach);
            cargoMission.pickupAllowed = true;
            cargoMission.deliveryAllowed = false;
            voice.Say("S3-09");
        }

        void Update()
        {
            if (drone == null || drone.Body == null) return;
            if (assist != null) assist.routePlaybackReady = !voice.IsBusy && Stage != MarsMissionStage.Briefing;
            float dt = Time.deltaTime;
            HeightAboveGround = GroundDistance(drone.Body.position);
            stageTime += dt;
            if (Stage == MarsMissionStage.Briefing)
            {
                if (!voice.IsBusy) SetStage(MarsMissionStage.Outbound);
                return;
            }
            if (MissionComplete || Stage == MarsMissionStage.Failed) return;
            if (!airborne && drone.Armed && HeightAboveGround > 2f) airborne = true;
            if (airborne) flightTime += dt;
            if (airborne && flightTime >= 2f) voice.Say("S1-01", "S1-06");
            if (!airborne && stageTime >= 10f) voice.Hint("S1-02", 30f);

            if (Stage == MarsMissionStage.Approach)
            {
                if (recoveringCargo)
                {
                    var load = cargoMission.cargo;
                    float halfHeight = load.GetComponent<Collider>().bounds.extents.y;
                    cargoMission.pickupZone.transform.position = load.Body.position - Vector3.up * (halfHeight + .035f);
                }
                approachTime += dt;
                float distance = Vector3.Distance(drone.Body.position, cargoMission.cargo.Body.position);
                if (!pickupNear && distance <= 50f) { pickupNear = true; voice.Say("S2-04"); }
                if (approachTime >= Mathf.Max(0f, stormDelay - 60f)) voice.Say("S2-11");
                if (approachTime >= stormDelay) StartStorm();
                if (pickupNear && distance < 18f) GuidePickup();
            }
            if (stormActive)
            {
                stormTime += dt;
                if (stormTime >= 1f && Stage <= MarsMissionStage.Carrying) voice.Say("S2-03");
                if (stormTime >= 8f && Stage == MarsMissionStage.Carrying) voice.Say("S3-04");
                if (Stage == MarsMissionStage.Carrying && stormTime >= 30f && Time.time - voice.LastLineTime >= 30f)
                    voice.Hint("S3-08", 50f);
            }
            if (Stage == MarsMissionStage.Canyon && stageTime >= 10f) voice.Say("S4-03");
            if (Stage == MarsMissionStage.Canyon && stageTime >= 20f) voice.Hint("S4-08", 35f);
            if (Stage == MarsMissionStage.Cave && stageTime >= 5f) voice.Say("S5-02");
            if (Stage == MarsMissionStage.CaveExit) voice.Say("S5-04");

            if (Stage == MarsMissionStage.Landing)
            {
                Vector3 delta = drone.Body.position - landingPoint.position;
                float altitude = delta.y;
                if (Vector3.ProjectOnPlane(delta, Vector3.up).magnitude < 13f && drone.Body.linearVelocity.y < -.05f)
                {
                    if (altitude <= 10.5f && altitude > 5.5f) voice.Hint("S6-03", 120f);
                    if (altitude <= 5.5f && altitude > 2.5f) voice.Hint("S6-04", 120f);
                    if (altitude <= 2.5f) voice.Hint("S6-05", 120f);
                }
                if (touchedDown && !drone.Armed && HeightAboveGround < 1.3f && drone.Body.linearVelocity.magnitude < 1f)
                {
                    SetStage(MarsMissionStage.Completed);
                    voice.Say("S6-07", "S6-08");
                }
            }
            if (cargoMission.State == CargoMissionState.Failed && !failedAnnounced) FailMission();
            UpdateAdvice(dt);
            UpdateWeather(dt);
        }

        void GuidePickup()
        {
            var target = cargoMission.pickupZone.Volume.bounds.center;
            var camera = drone.GetComponent<KeyboardDroneInput>().fpvCamera;
            Vector3 forward = Vector3.ProjectOnPlane(camera.transform.forward, Vector3.up).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            Vector3 delta = target - drone.Body.position;
            float x = Vector3.Dot(delta, right), z = Vector3.Dot(delta, forward);
            if (cargoMission.HoldProgress > .05f) voice.Hint("S2-10", 10f);
            else if (delta.y < -2f) voice.Hint("S2-09", 12f);
            else if (Mathf.Abs(x) > 2f && Mathf.Abs(x) > Mathf.Abs(z)) voice.Hint(x > 0f ? "S2-06" : "S2-05", 12f);
            else if (Mathf.Abs(z) > 2f) voice.Hint(z > 0f ? "S2-07" : "S2-08", 12f);
        }

        void UpdateAdvice(float dt)
        {
            Vector3 velocity = drone.Body.linearVelocity;
            if (airborne && drone.Armed && HeightAboveGround < 8f && velocity.y < -3f) voice.Hint("S1-07", 15f);
            stillTime = airborne && velocity.magnitude < .7f ? stillTime + dt : 0f;
            noYawTime = airborne && Mathf.Abs(drone.Input.Yaw) < .08f ? noYawTime + dt : 0f;
            if (Stage == MarsMissionStage.Outbound && stillTime > 10f) voice.Hint("S1-03", 30f);
            if (Stage == MarsMissionStage.Outbound && noYawTime > 20f) voice.Hint("S1-04", 35f);
            if (Stage == MarsMissionStage.Outbound && stillTime > 3f && stillTime < 6f) voice.Hint("S1-05", 90f);
            if (HeightAboveGround > 80f) voice.Hint("EV-11", 35f);
            if (NextTarget != null)
            {
                Vector3 delta = NextTarget.position - drone.Body.position;
                awayTime = Vector3.Dot(velocity, delta.normalized) < -1f ? awayTime + dt : 0f;
                if (awayTime > 10f) voice.Hint("EV-12", 25f);
                if (awayTime > 20f && Stage == MarsMissionStage.Outbound) voice.Hint("S1-08", 45f);
            }
            if (Stage == MarsMissionStage.Canyon || Stage == MarsMissionStage.Cave || Stage == MarsMissionStage.CaveExit)
            {
                foreach (var hit in Physics.RaycastAll(drone.Body.position, drone.transform.forward, 5f, ~0, QueryTriggerInteraction.Ignore))
                    if (hit.rigidbody != drone.Body && hit.rigidbody != cargoMission.cargo.Body)
                    { voice.Hint("S4-06", 15f); break; }
            }
        }

        void StartStorm()
        {
            if (stormActive) return;
            stormActive = true; stormTime = 0f;
            if (storm != null) storm.BeginStorm();
        }

        void UpdateWeather(float dt)
        {
            bool shelter = Stage == MarsMissionStage.Cave || Stage == MarsMissionStage.CaveExit;
            bool canyon = Stage == MarsMissionStage.Canyon;
            float speed = shelter ? .3f : canyon ? 2f : stormActive ? 9f + 3f * Mathf.Sin(stormTime * .7f) : 1f;
            runtimeEnvironment.windVelocity = Vector3.Lerp(runtimeEnvironment.windVelocity, new Vector3(speed, 0f, -speed * .2f), 1f - Mathf.Exp(-dt));
            runtimeEnvironment.airDensity = Mathf.Lerp(runtimeEnvironment.airDensity, canyon ? baseDensity * 1.03f : baseDensity, dt);
            if (Stage == MarsMissionStage.Carrying && stormActive && speed > 11.7f)
                voice.Hint(Vector3.Dot(runtimeEnvironment.windVelocity, drone.transform.right) > 0f ? "S3-06" : "S3-05", 25f);
        }

        void LateUpdate()
        {
            if (display != null)
            {
                display.routeInstruction = Stage == MarsMissionStage.CaveExit ? "Продолжайте к выходу из пещеры"
                    : Stage == MarsMissionStage.Cave ? "Следуйте по тоннелю"
                    : Stage == MarsMissionStage.Canyon || Stage == MarsMissionStage.CanyonApproach ? "Следуйте по каньону к пещере"
                    : Stage == MarsMissionStage.Landing ? "Следуйте к площадке доставки" : "Следуйте к каньону";
            }
            if (navigationBeacon != null && NextTarget != null)
            {
                navigationBeacon.gameObject.SetActive(Stage != MarsMissionStage.Briefing && !MissionComplete && Stage != MarsMissionStage.Failed);
                navigationBeacon.position = NextTarget == cargoMission.pickupZone.transform
                    ? cargoMission.pickupZone.Volume.bounds.center : NextTarget.position;
                navigationBeacon.Rotate(0f, 30f * Time.deltaTime, 0f, Space.World);
            }
        }

        public float GroundDistance(Vector3 origin)
        {
            float result = 500f;
            foreach (var hit in Physics.RaycastAll(origin, Vector3.down, 500f, ~0, QueryTriggerInteraction.Ignore))
                if (hit.rigidbody != drone.Body && hit.rigidbody != cargoMission.cargo.Body) result = Mathf.Min(result, hit.distance);
            return result;
        }

        public void FailMission()
        {
            if (Stage == MarsMissionStage.Failed || MissionComplete) return;
            failedAnnounced = true;
            SetStage(MarsMissionStage.Failed);
            cargoMission.pickupAllowed = cargoMission.deliveryAllowed = false;
            voice.Say("EV-13");
        }

        // Integration points for future battery, damage, arena and result systems. No invented telemetry.
        public void ReportEvent(string id) => voice.Say(id);
        public void ReportBattery(float fraction)
        {
            if (fraction <= .05f) voice.Say("EV-03");
            else if (fraction <= .15f) voice.Say("EV-02");
            else if (fraction <= .30f) voice.Say("EV-01");
        }

        void OnDestroy()
        {
            if (cargoMission != null)
            {
                cargoMission.pickedUp.RemoveListener(PickedUp); cargoMission.completed.RemoveListener(Delivered);
                cargoMission.restarted.RemoveListener(Restarted); cargoMission.lost.RemoveListener(LostCargo);
            }
            if (runtimeEnvironment != null) Destroy(runtimeEnvironment);
        }
    }
}
