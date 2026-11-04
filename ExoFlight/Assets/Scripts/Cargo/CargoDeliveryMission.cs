using ExoFlight.Triggers;
using UnityEngine;
using UnityEngine.Events;

namespace ExoFlight.Cargo
{
    public enum CargoMissionState { AwaitingPickup, Transporting, AwaitingLanding, Completed, Failed }

    /// <summary>Reusable pickup/transport/delivery sequence, independent of the particular cargo subclass.</summary>
    public sealed class CargoDeliveryMission : MonoBehaviour
    {
        public DroneCargoCarrier carrier;
        public TransportableCargo cargo;
        public CargoHoverZone pickupZone;
        public CargoHoverZone deliveryZone;
        [Min(.1f)] public float holdSeconds = 1.5f;
        [Min(.1f)] public float settleSeconds = .5f;
        [Min(1f)] public float landingTimeout = 15f;
        public UnityEvent pickedUp = new UnityEvent();
        public UnityEvent released = new UnityEvent();
        public UnityEvent completed = new UnityEvent();
        public UnityEvent restarted = new UnityEvent();
        [SerializeField] CargoMissionState state;
        [SerializeField] float holdProgress;
        public CargoMissionState State => state;
        public float HoldProgress => holdProgress;
        float landingTime, settledTime;
        PlayerCheckpoint checkpoint;

        void Start()
        {
            if (carrier == null || cargo == null || pickupZone == null || deliveryZone == null
                || pickupZone == deliveryZone || cargo.environment != carrier.Drone.environment)
            {
                Debug.LogError("[Cargo] Assign a carrier, cargo, two different zones and the same environment.", this);
                enabled = false;
                return;
            }
            checkpoint = carrier.GetComponent<PlayerCheckpoint>();
            if (checkpoint != null) checkpoint.Respawned += ResetMission;
            cargo.RememberHome();
            UpdateLabels();
            Debug.Log($"[Cargo] Подлетите к кубу PICKUP и удерживайтесь над ним {holdSeconds:F1} с. R — начать заново.", this);
        }

        void OnDestroy() { if (checkpoint != null) checkpoint.Respawned -= ResetMission; }
        void OnDisable() { holdProgress = settledTime = 0f; }
        void FixedUpdate() => Tick(Time.fixedDeltaTime);

        public void Tick(float dt)
        {
            if (!isActiveAndEnabled || dt <= 0f || state == CargoMissionState.Completed || state == CargoMissionState.Failed) return;
            if (carrier == null || cargo == null || !cargo.isActiveAndEnabled)
            {
                Fail("Груз или дрон недоступен.");
                return;
            }
            if (state == CargoMissionState.AwaitingPickup)
            {
                if (!Hold(pickupZone.IsHovering(carrier) && carrier.CanAttach(cargo), dt)) return;
                if (!carrier.TryAttach(cargo)) { holdProgress = 0f; return; }
                state = CargoMissionState.Transporting;
                holdProgress = 0f;
                Debug.Log($"[Cargo] Груз захвачен: {cargo.Mass:F2} кг. Перевезите его в DELIVERY; добавьте газ.", this);
                pickedUp.Invoke();
            }
            else if (state == CargoMissionState.Transporting)
            {
                if (carrier.CurrentCargo != cargo) { Fail("Крепление груза потеряно. R — повторить миссию."); return; }
                if (!Hold(deliveryZone.IsHovering(carrier) && deliveryZone.ContainsCargoFootprint(cargo), dt)) return;
                carrier.Release();
                state = CargoMissionState.AwaitingLanding;
                holdProgress = landingTime = settledTime = 0f;
                Debug.Log("[Cargo] Груз отпущен. Ожидаем приземления на площадку.", this);
                released.Invoke();
            }
            else if (state == CargoMissionState.AwaitingLanding)
            {
                landingTime += dt;
                bool settled = cargo.Carrier == null && cargo.HasGroundContact
                    && deliveryZone.ContainsCargoFootprint(cargo) && cargo.Body.linearVelocity.magnitude < .15f
                    && cargo.Body.angularVelocity.magnitude < .3f;
                settledTime = settled ? settledTime + dt : 0f;
                if (settledTime >= settleSeconds)
                {
                    state = CargoMissionState.Completed;
                    cargo.MarkDelivered();
                    Debug.Log("[Cargo] Миссия выполнена: груз доставлен и стоит на площадке.", this);
                    completed.Invoke();
                }
                else if (landingTime >= landingTimeout) Fail("Груз не приземлился внутри площадки. R — повторить.");
            }
            UpdateLabels();
        }

        bool Hold(bool valid, float dt)
        {
            holdProgress = valid ? holdProgress + dt : 0f;
            UpdateLabels();
            return holdProgress >= Mathf.Max(.1f, holdSeconds);
        }

        public void ResetMission()
        {
            if (carrier != null && carrier.CurrentCargo == cargo) carrier.Release();
            if (cargo != null) cargo.ResetToHome();
            state = CargoMissionState.AwaitingPickup;
            holdProgress = landingTime = settledTime = 0f;
            Physics.SyncTransforms();
            UpdateLabels();
            restarted.Invoke();
        }

        void Fail(string reason)
        {
            state = CargoMissionState.Failed;
            holdProgress = 0f;
            UpdateLabels();
            Debug.Log("[Cargo] " + reason, this);
        }

        void UpdateLabels()
        {
            if (pickupZone != null) pickupZone.ShowStatus(state == CargoMissionState.AwaitingPickup
                ? $"HOLD {holdProgress:F1} / {holdSeconds:F1} s" : "PICKED UP");
            if (deliveryZone != null) deliveryZone.ShowStatus(state == CargoMissionState.Transporting
                ? $"HOLD {holdProgress:F1} / {holdSeconds:F1} s" : state == CargoMissionState.AwaitingLanding
                ? "LANDING..." : state == CargoMissionState.Completed ? "DELIVERED"
                : state == CargoMissionState.Failed ? "RESET: R" : "PICK UP CARGO FIRST");
        }
    }
}
