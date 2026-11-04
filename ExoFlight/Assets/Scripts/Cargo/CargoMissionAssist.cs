using ExoFlight.Flight;
using ExoFlight.Triggers;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ExoFlight.Cargo
{
    /// <summary>Opt-in test pilot. Extra servo forces deliberately bypass manual piloting difficulty.</summary>
    [DefaultExecutionOrder(100)]
    [DisallowMultipleComponent, RequireComponent(typeof(DroneFlightController))]
    public sealed class CargoMissionAssist : MonoBehaviour
    {
        public CargoDeliveryMission mission;
        [Min(.5f)] public float cruiseHeightAboveZones = 1.5f;
        [Min(.2f)] public float maximumSpeed = 2f;
        [Min(.5f)] public float positionGain = 1.5f;
        [Min(.5f)] public float velocityGain = 4f;
        [SerializeField] bool active;
        [SerializeField] bool holdingPosition;
        [SerializeField] string stage = "Off — press P in Game";
        [SerializeField] Vector3 target;
        public bool Active => active;
        public bool HoldingPosition => active && holdingPosition;
        public string Stage => stage;
        public Vector3 Target => target;

        DroneFlightController drone;
        KeyboardDroneInput input;
        PlayerCheckpoint checkpoint;
        DroneFlightMode originalMode;
        DroneCameraView cameraView;
        bool originalThirdPerson;
        Quaternion heading;
        float cruiseY;
        int leg;

        void Awake()
        {
            drone = GetComponent<DroneFlightController>();
            input = GetComponent<KeyboardDroneInput>();
            checkpoint = GetComponent<PlayerCheckpoint>();
            cameraView = GetComponent<DroneCameraView>();
        }

        void OnEnable()
        {
            if (checkpoint != null) checkpoint.Respawned += OnRespawn;
        }

        void Update()
        {
            if (Application.isFocused && Keyboard.current != null) ReadKeyboard(Keyboard.current);
        }

        public void ReadKeyboard(Keyboard keyboard)
        {
            if (keyboard.pKey.wasPressedThisFrame)
            {
                if (active && !holdingPosition) StopAssist();
                else BeginDemo();
            }
            else if (keyboard.hKey.wasPressedThisFrame)
            {
                if (HoldingPosition) StopAssist();
                else BeginHold();
            }
        }

        [ContextMenu("Start assisted demo (Play Mode)")]
        public void BeginDemo() => BeginAssist(true);

        [ContextMenu("Hold position (Play Mode)")]
        public void BeginHold() => BeginAssist(false);

        void BeginAssist(bool demonstration)
        {
            if (!Application.isPlaying || !isActiveAndEnabled || mission == null || !mission.isActiveAndEnabled
                || mission.carrier == null || mission.carrier.gameObject != gameObject
                || mission.cargo == null || mission.pickupZone == null || mission.deliveryZone == null) return;
            if (active) StopAssist();
            // Explicit demo restart; every subsequent flight leg uses forces and real triggers.
            if (demonstration)
            {
                if (checkpoint != null) checkpoint.Respawn();
                else mission.ResetMission();
            }
            originalMode = drone.mode;
            if (input != null)
            {
                input.ResetInput();
                input.ExternalControlActive = true;
            }
            if (cameraView != null)
            {
                originalThirdPerson = cameraView.ThirdPerson;
                if (demonstration) cameraView.SetThirdPerson(true);
            }
            drone.SetInput(default);
            drone.SetArmed(true);
            drone.SetMode(DroneFlightMode.Angle);
            drone.SetInput(new FlightControlInput(drone.HoverThrottle, 0f, 0f, 0f));
            Vector3 forward = Vector3.ProjectOnPlane(drone.Body.rotation * Vector3.forward, Vector3.up);
            heading = demonstration || forward.sqrMagnitude < .001f ? Quaternion.identity
                : Quaternion.LookRotation(forward.normalized, Vector3.up);
            cruiseY = Mathf.Max(mission.pickupZone.Volume.bounds.max.y, mission.deliveryZone.Volume.bounds.max.y)
                + cruiseHeightAboveZones;
            holdingPosition = !demonstration;
            var zone = mission.State == CargoMissionState.Transporting ? mission.deliveryZone : mission.pickupZone;
            float holdY = drone.Body.position.y < zone.Volume.bounds.min.y
                ? ZoneCenter(zone).y : drone.Body.position.y;
            target = new Vector3(drone.Body.position.x, demonstration ? cruiseY : holdY, drone.Body.position.z);
            leg = 0;
            active = true;
            stage = demonstration ? "Takeoff" : "Holding position — H: manual";
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            Debug.Log(demonstration
                ? "[Cargo assist] P: автопоказ со старта. V — камера; H — зависание; R — сброс."
                : "[Cargo assist] H: удержание позиции включено. Повторное H — ручное управление. V — камера.", this);
        }

        public void StopAssist(bool disarm = false)
        {
            if (!active) return;
            active = false;
            stage = "Off — press P in Game";
            if (disarm) drone.SetArmed(false);
            float throttle = drone.Armed ? drone.Input.Throttle : 0f;
            drone.SetMode(originalMode);
            if (input != null) input.ResumeManualControl(throttle);
            if (cameraView != null && !holdingPosition) cameraView.SetThirdPerson(originalThirdPerson);
            holdingPosition = false;
        }

        void FixedUpdate() => StepAssist(Time.fixedDeltaTime);

        /// <summary>Runs after motor forces; leaves cargo, collision, gravity and mission logic active.</summary>
        public void StepAssist(float dt)
        {
            if (!active || dt <= 0f) return;
            if (!drone.Armed || mission == null || !mission.isActiveAndEnabled
                || mission.cargo == null || (!holdingPosition && mission.State == CargoMissionState.Failed))
            {
                StopAssist();
                return;
            }
            if (!holdingPosition) AdvanceRoute();
            drone.SetInput(new FlightControlInput(drone.HoverThrottle, 0f, 0f, 0f));
            Vector3 desiredVelocity = Vector3.ClampMagnitude((target - drone.Body.position) * positionGain, maximumSpeed);
            Vector3 acceleration = Vector3.ClampMagnitude((desiredVelocity - drone.Body.linearVelocity) * velocityGain, 6f);
            Vector3 rotorForce = drone.Body.rotation * Vector3.up * drone.TotalThrust;
            // Deliberately strong test support. Not a model of extra physical engines.
            Vector3 force = drone.FlightMass * (acceleration - drone.environment.gravity) - rotorForce;
            Vector3 assemblyCom = drone.Body.position + drone.Body.rotation * drone.FlightCenterOfMass;
            drone.Body.AddForceAtPosition(force, assemblyCom, ForceMode.Force);

            Quaternion error = heading * Quaternion.Inverse(drone.Body.rotation);
            error.ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180f) angle -= 360f;
            Vector3 rotationError = Mathf.Abs(angle) < .001f ? Vector3.zero : axis * (angle * Mathf.Deg2Rad);
            Vector3 angularAcceleration = rotationError * 20f - drone.Body.angularVelocity * 8f;
            Vector3 localAcceleration = Quaternion.Inverse(drone.Body.rotation) * angularAcceleration;
            drone.Body.AddRelativeTorque(Vector3.Scale(drone.Body.inertiaTensor, localAcceleration), ForceMode.Force);
        }

        void AdvanceRoute()
        {
            bool reached = Vector3.Distance(drone.Body.position, target) < .12f
                && drone.Body.linearVelocity.magnitude < .2f;
            Vector3 pickup = ZoneCenter(mission.pickupZone);
            pickup.x = mission.cargo.Body.position.x;
            pickup.z = mission.cargo.Body.position.z;
            Vector3 delivery = ZoneCenter(mission.deliveryZone);
            delivery.y += .35f; // Leave a visible fall after release instead of resting the load on the pad.

            if (mission.State == CargoMissionState.AwaitingPickup)
            {
                if (leg == 0 && reached) { leg = 1; target = new Vector3(pickup.x, cruiseY, pickup.z); }
                else if (leg == 1 && reached) { leg = 2; target = pickup; }
                stage = leg == 0 ? "Takeoff" : leg == 1 ? "Approaching pickup" : "Holding for pickup";
            }
            else if (mission.State == CargoMissionState.Transporting)
            {
                if (leg < 3)
                {
                    leg = 3;
                    target = new Vector3(drone.Body.position.x, cruiseY, drone.Body.position.z);
                }
                else if (leg == 3 && reached) { leg = 4; target = new Vector3(delivery.x, cruiseY, delivery.z); }
                else if (leg == 4 && reached) { leg = 5; target = delivery; }
                stage = leg == 3 ? "Lifting cargo" : leg == 4 ? "Carrying cargo" : "Holding for release";
            }
            else stage = mission.State == CargoMissionState.Completed ? "Delivered — P: manual, R: restart" : "Waiting for landing";
        }

        static Vector3 ZoneCenter(CargoHoverZone zone) => zone.transform.TransformPoint(zone.Volume.center);

        void OnRespawn() => StopAssist(true);
        void OnApplicationFocus(bool focused) { if (!focused) StopAssist(true); }
        void OnApplicationPause(bool paused) { if (paused) StopAssist(true); }
        void OnDisable()
        {
            if (checkpoint != null) checkpoint.Respawned -= OnRespawn;
            StopAssist(true);
        }
    }
}
