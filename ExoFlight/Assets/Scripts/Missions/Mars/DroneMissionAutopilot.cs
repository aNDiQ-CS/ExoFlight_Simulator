using ExoFlight.Cargo;
using ExoFlight.Flight;
using ExoFlight.Triggers;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ExoFlight.Missions.Mars
{
    /// <summary>Position/velocity outer loop. All forces still come from the four simulated motors.</summary>
    [DefaultExecutionOrder(-100), DisallowMultipleComponent]
    [RequireComponent(typeof(DroneFlightController), typeof(DroneInputRouter))]
    public sealed class DroneMissionAutopilot : MonoBehaviour
    {
        public MarsMissionDirector mission;
        public Transform[] outbound, loaded;
        public Transform landing;
        public bool autoStart;
        [Min(.2f)] public float cruiseSpeed = 6f;
        [Min(.2f)] public float caveSpeed = 3f;
        [Min(.2f)] public float accelerationLimit = 1.5f;
        [Min(.1f)] public float climbSpeed = 1.8f;
        [Min(.1f)] public float descentSpeed = 1.2f;
        [Range(5f, 35f)] public float tiltLimit = 24f;
        [Min(1f)] public float yawSpeed = 22f;
        public bool Active { get; private set; }
        public bool Holding { get; private set; }
        public string Status { get; private set; } = "Ready";
        public int Waypoint { get; private set; }
        public bool FollowingRoute => Active && !Holding && (phase == 2 || phase == 3);
        public bool LoadedRoute => phase == 3;
        public Vector3 Target { get; private set; }
        public float MaximumTrackingError { get; private set; }
        DroneFlightController drone;
        DroneInputRouter router;
        PlayerCheckpoint checkpoint;
        DroneFlightMode manualMode;
        Vector3 velocityIntegral, acceleration, takeoff;
        float settleTime;
        int phase;
        bool vrPressed;

        void Awake()
        {
            drone = GetComponent<DroneFlightController>();
            router = GetComponent<DroneInputRouter>();
            checkpoint = GetComponent<PlayerCheckpoint>();
        }
        void OnEnable() { if (checkpoint != null) checkpoint.Respawned += OnRespawn; }
        void Start() { if (autoStart) BeginMission(); }
        void Update()
        {
            var kb = Keyboard.current;
            if (Application.isFocused && kb != null)
            {
                if (kb.pKey.wasPressedThisFrame) { if (Active) Stop(false); else BeginMission(); }
                else if (kb.hKey.wasPressedThisFrame) { if (Holding) Stop(false); else HoldPosition(); }
                else if (Active && kb.rKey.wasPressedThisFrame) { Stop(true); checkpoint.Respawn(); }
                else if (Active && (kb.spaceKey.wasPressedThisFrame || kb.escapeKey.wasPressedThisFrame)) Stop(true);
                // A manual stick command is an explicit takeover, not an ignored keypress.
                else if (Active && KeyboardDroneInput.HasStickInput(kb)) Stop(false);
            }
            // Right controller primary button starts the same full mission in a headset.
            var right = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.RightHand);
            bool pressed = right.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primaryButton, out bool down) && down;
            if (pressed && !vrPressed) { if (Active) HoldPosition(); else BeginMission(); }
            vrPressed = pressed;
            if (Active && mission.Stage == MarsMissionStage.Failed) Stop(true);
        }

        [ContextMenu("Start presentation (Play Mode)")]
        public void BeginMission()
        {
            if (!Application.isPlaying || !enabled || mission == null || outbound == null || outbound.Length < 2
                || loaded == null || loaded.Length < 2 || landing == null) return;
            Stop(true);
            checkpoint.Respawn(); // The only reposition: an explicit restart at the launch point.
            Acquire();
            Holding = false;
            phase = Waypoint = 0;
            takeoff = drone.Body.position + Vector3.up * 6f;
            Target = drone.Body.position;
            Status = "Briefing";
            MaximumTrackingError = 0f;
        }

        public void HoldPosition()
        {
            if (!Application.isPlaying || mission == null) return;
            if (!Active) Acquire();
            Holding = true;
            Target = drone.Body.position;
            Target += Vector3.up * Mathf.Max(0f, 3f - mission.HeightAboveGround);
            Status = "Position hold";
            Arm();
        }
        void Acquire()
        {
            manualMode = drone.mode;
            router.AcquireAutopilot();
            drone.SetMode(DroneFlightMode.Angle);
            velocityIntegral = acceleration = Vector3.zero;
            settleTime = 0f;
            Active = true;
        }
        void Arm()
        {
            if (drone.Armed) return;
            drone.SetInput(default);
            drone.SetArmed(true);
        }
        public void Stop(bool disarm = true)
        {
            if (!Active) return;
            Active = Holding = false;
            drone.SetMode(manualMode);
            router.ReleaseAutopilot(disarm);
            Status = disarm ? "Stopped" : "Manual control";
        }
        void OnRespawn() => Stop(true);
        void OnDisable()
        {
            Stop(true);
            if (checkpoint != null) checkpoint.Respawned -= OnRespawn;
        }
        void OnApplicationPause(bool paused) { if (paused) Stop(true); }

        void FixedUpdate()
        {
            if (!Active) return;
            float dt = Time.fixedDeltaTime;
            Vector3 desiredVelocity = Vector3.zero;
            Vector3 facing = drone.Body.rotation * Vector3.forward;
            if (Holding) desiredVelocity = Seek(Target, 2f);
            else if (phase == 0)
            {
                if (!mission.BriefingFinished) return;
                Arm(); phase = 1; Target = takeoff; Status = "Takeoff";
            }
            if (!Holding && phase == 1)
            {
                desiredVelocity = Seek(takeoff, 2f);
                if (Vector3.Distance(drone.Body.position, takeoff) < .6f && drone.Body.linearVelocity.magnitude < .7f)
                { phase = 2; Waypoint = 1; }
            }
            else if (!Holding && (phase == 2 || phase == 3))
            {
                bool carrying = phase == 3;
                var path = carrying ? loaded : outbound;
                Target = path[Waypoint].position;
                bool last = Waypoint == path.Length - 1;
                bool cave = mission.Stage == MarsMissionStage.Cave || mission.Stage == MarsMissionStage.CaveExit;
                float speed = cave ? caveSpeed : cruiseSpeed;
                float distance = Vector3.Distance(drone.Body.position, Target);
                // Gates wait for preceding speech and actual trigger acceptance. Do not skip them.
                bool gatePending = IsPendingGate(Target, carrying);
                float acceptance = cave ? 1.2f : 2.2f;
                if (!last && !gatePending && distance < acceptance)
                {
                    Waypoint++;
                    Target = path[Waypoint].position;
                    last = Waypoint == path.Length - 1;
                }
                Vector3 delta = Target - drone.Body.position;
                Vector3 flat = Vector3.ProjectOnPlane(delta, Vector3.up);
                bool vertical = flat.magnitude < 1.5f;
                if (!vertical && !last) facing = flat.normalized;
                // Slow before sharp bends to align with the next tunnel segment.
                float yawError = Vector3.SignedAngle(Vector3.ProjectOnPlane(drone.Body.rotation * Vector3.forward,
                    Vector3.up), facing, Vector3.up);
                speed *= Mathf.Lerp(.10f, 1f, Mathf.InverseLerp(85f, 15f, Mathf.Abs(yawError)));
                if (last) speed = Mathf.Min(speed, 1.2f);
                desiredVelocity = Seek(Target, speed);
                if (last && !carrying && mission.cargoMission.State == CargoMissionState.Transporting)
                {
                    phase = 3; Waypoint = 0; velocityIntegral = Vector3.zero;
                }
                else if (last && carrying && mission.cargoMission.State == CargoMissionState.Completed)
                {
                    phase = 4; settleTime = 0f; velocityIntegral = Vector3.zero;
                }
                Status = (carrying ? "Rover " : "Outbound ") + (Waypoint + 1) + "/" + path.Length;
            }
            else if (!Holding && phase == 4)
            {
                // Keep rotor clearance above the rover, then move to the adjacent landing spot.
                Target = landing.position + Vector3.up * 3f;
                desiredVelocity = Seek(Target, 1.5f);
                Status = "Landing approach";
                if (Vector3.Distance(Target, drone.Body.position) < .5f && drone.Body.linearVelocity.magnitude < .5f)
                { phase = 5; settleTime = 0f; }
            }
            else if (!Holding && phase == 5)
            {
                Target = landing.position - Vector3.up * .2f;
                desiredVelocity = Seek(Target, .45f);
                desiredVelocity.y = Mathf.Max(desiredVelocity.y, -.4f);
                Status = "Landing";
                bool grounded = mission.HeightAboveGround < .75f && drone.Body.linearVelocity.magnitude < .18f;
                settleTime = grounded ? settleTime + dt : 0f;
                if (settleTime > 1.2f) { Stop(true); Status = "Presentation complete"; return; }
            }
            if (!drone.Armed) { Stop(true); return; }
            ControlVelocity(desiredVelocity, facing, dt);
        }

        bool IsPendingGate(Vector3 point, bool carrying)
        {
            int index = mission.NextZoneIndex;
            if (index >= mission.zones.Length || (index > 0 && !carrying)) return false;
            return Vector3.Distance(point, mission.zones[index].transform.position) < 3f;
        }
        Vector3 Seek(Vector3 target, float speed)
        {
            Vector3 delta = target - drone.Body.position;
            Vector3 flat = Vector3.ProjectOnPlane(delta, Vector3.up);
            float limit = Mathf.Min(speed, Mathf.Sqrt(2f * accelerationLimit * flat.magnitude));
            Vector3 velocity = Vector3.ClampMagnitude(flat * .85f, limit);
            velocity.y = Mathf.Clamp(delta.y * .85f, -descentSpeed, climbSpeed);
            // Follow the three-dimensional segment instead of reaching a steep slope too low.
            float travelTime = Mathf.Max(flat.magnitude / Mathf.Max(.1f, speed),
                Mathf.Abs(delta.y) / (delta.y >= 0f ? climbSpeed : descentSpeed));
            if (travelTime > 1f) velocity = Vector3.ClampMagnitude(delta / travelTime, speed);
            return velocity;
        }

        void ControlVelocity(Vector3 desiredVelocity, Vector3 facing, float dt)
        {
            Vector3 error = desiredVelocity - drone.Body.linearVelocity;
            if (!drone.MotorsSaturated) velocityIntegral = Vector3.ClampMagnitude(velocityIntegral + error * dt, 2f);
            Vector3 demand = error * 1.5f + velocityIntegral * .22f;
            Vector3 horizontal = Vector3.ClampMagnitude(Vector3.ProjectOnPlane(demand, Vector3.up), accelerationLimit);
            demand = horizontal + Vector3.up * Mathf.Clamp(demand.y, -1.3f, 1.5f);
            acceleration = Vector3.MoveTowards(acceleration, demand, 3f * dt);
            Vector3 forcePerMass = acceleration - drone.environment.gravity;
            Vector3 up = Vector3.RotateTowards(Vector3.up, forcePerMass.normalized, tiltLimit * Mathf.Deg2Rad, 0f);
            Vector3 currentForward = Vector3.ProjectOnPlane(drone.Body.rotation * Vector3.forward, Vector3.up).normalized;
            if (currentForward.sqrMagnitude < .1f) currentForward = Vector3.forward;
            Quaternion heading = Quaternion.LookRotation(currentForward, Vector3.up);
            Vector3 localUp = Quaternion.Inverse(heading) * up;
            float pitch = Mathf.Atan2(localUp.z, localUp.y) * Mathf.Rad2Deg / drone.profile.maximumTilt;
            float roll = Mathf.Asin(Mathf.Clamp(localUp.x, -1f, 1f)) * Mathf.Rad2Deg / drone.profile.maximumTilt;
            float turn = Vector3.SignedAngle(currentForward, Vector3.ProjectOnPlane(facing, Vector3.up), Vector3.up);
            float rate = Mathf.Clamp(turn * 1.2f, -yawSpeed, yawSpeed) / drone.profile.maximumRates.y;
            float yaw = InverseExpo(rate, drone.profile.expo);
            float cosine = Mathf.Max(.5f, Vector3.Dot(drone.Body.rotation * Vector3.up, Vector3.up));
            float perRotor = drone.FlightMass * forcePerMass.y / cosine / DroneFlightController.MotorCount;
            float max = drone.profile.ThrustAtRpm(drone.profile.maximumRpm, drone.environment.airDensity);
            float idle = drone.profile.ThrustAtRpm(drone.profile.idleRpm, drone.environment.airDensity);
            float throttle = Mathf.Clamp01((perRotor - idle) / (max - idle));
            drone.SetInput(new FlightControlInput(throttle, pitch, yaw, roll));
            MaximumTrackingError = Mathf.Max(MaximumTrackingError, error.magnitude);
        }
        static float InverseExpo(float value, float expo)
        {
            float low = 0f, high = 1f, target = Mathf.Abs(value);
            for (int i = 0; i < 16; i++)
            {
                float mid = (low + high) * .5f;
                if (Mathf.Lerp(mid, mid * mid * mid, expo) < target) low = mid; else high = mid;
            }
            return (low + high) * .5f * Mathf.Sign(value);
        }
    }
}
