using ExoFlight.Triggers;
using UnityEngine;

namespace ExoFlight.Flight
{
    [DefaultExecutionOrder(-150)]
    [DisallowMultipleComponent, RequireComponent(typeof(DroneFlightController))]
    public sealed class JoystickDroneInput : MonoBehaviour
    {
        [Header("Ссылки")]
        [Tooltip("Скрипт, читающий оси джойстика. Если пусто — найдётся на этом же объекте.")]
        [SerializeField] private DroneJoystickInput joystick;

        [Header("Маппинг aux-каналов (индексы в списке auxPaths)")]
        [Tooltip("Aux-канал для Arm/Disarm (тумблер). Значение > 0.5 = ARMED.")]
        [SerializeField] private int armChannel = 0;

        [Tooltip("Aux-канал для переключения Acro/Angle. Переключение по фронту (>0.5).")]
        [SerializeField] private int modeChannel = 1;

        [Tooltip("Aux-канал для Respawn. Срабатывает по фронту (>0.5).")]
        [SerializeField] private int resetChannel = 2;

        [Header("Пороги")]
        [Range(0.3f, 0.9f)]
        [SerializeField] private float switchThreshold = 0.5f;

        [Tooltip("Минимальный газ для разрешения ARM (защита от случайного взлёта).")]
        [Range(0f, 0.1f)]
        [SerializeField] private float armThrottleLimit = 0.05f;

        [Tooltip("Газ считается «в нуле» с этим допуском. Пульт может не доходить ровно до 0 " +
            "из-за триммера или калибровки канала.")]
        [Range(0f, 0.3f)]
        [SerializeField] private float armLowThreshold = 0.12f;

        [Header("Пуск без AUX-тумблера")]
        [Tooltip("Если AUX для Arm не настроен, моторы включаются сами: газ внизу — холостой ход, " +
            "подъём газа — взлёт. Так пульт работает без тумблеров и без клавиатуры в шлеме.")]
        [SerializeField] private bool autoArmFromThrottle = true;
        [Tooltip("Порог газа, после которого происходит ARM.")]
        [Range(0.01f, 0.4f)]
        [SerializeField] private float autoArmThrottle = 0.15f;
        [Tooltip("Сколько держать газ внизу после полёта, чтобы выключить моторы.")]
        [Range(0.5f, 5f)]
        [SerializeField] private float disarmHoldSeconds = 2f;

        [Header("Камера (как в KeyboardDroneInput)")]
        public Camera fpvCamera;
        [Range(-10f, 45f)] public float cameraTilt = 10f;
        [Range(50f, 120f)] public float fieldOfView = 85f;

        private DroneFlightController drone;
        private PlayerCheckpoint checkpoint;

        // Состояние тумблеров в предыдущем кадре (для детекции фронта)
        private bool prevArm;
        private bool prevMode;
        private bool prevReset;
        private bool armLowSeen;
        private bool armAtIdle;
        private bool blockIdleArm;
        private bool wasAirborne;
        private float lowHold;

        public float Throttle { get; private set; }

        void Awake()
        {
            drone = GetComponent<DroneFlightController>();
            checkpoint = GetComponent<PlayerCheckpoint>();

            if (joystick == null)
                joystick = GetComponent<DroneJoystickInput>();

            if (joystick == null)
                Debug.LogError("[JoystickDroneInput] DroneJoystickInput не найден!");
        }

        void OnEnable()
        {
            prevArm = prevMode = prevReset = armLowSeen = armAtIdle = blockIdleArm = wasAirborne = false;
            lowHold = 0f;
            if (checkpoint != null) checkpoint.Respawned += OnRespawned;
        }

        void OnDisable()
        {
            if (checkpoint != null) checkpoint.Respawned -= OnRespawned;
            drone?.SetArmed(false);
        }

        void FixedUpdate()
        {
            if (drone == null) return;
            if (joystick == null || !joystick.IsReady)
            {
                Throttle = 0f;
                drone.SetArmed(false);
                armLowSeen = armAtIdle = blockIdleArm = wasAirborne = false;
                lowHold = 0f;
                return;
            }

            // --- Читаем оси ---
            float throttle = Mathf.Clamp01(joystick.Throttle);
            float pitch = Mathf.Clamp(joystick.Pitch, -1f, 1f);
            float yaw = Mathf.Clamp(joystick.Yaw, -1f, 1f);
            float roll = Mathf.Clamp(joystick.Roll, -1f, 1f);

            Throttle = throttle;
            drone.SetInput(new FlightControlInput(throttle, pitch, yaw, roll));

            // Tolerance comes from the physical stick and the channel trim, not from a hard 0.02.
            float armLow = Mathf.Max(armThrottleLimit, armLowThreshold);

            // --- Arm / Disarm ---
            bool hasArmAux = joystick.HasAux(armChannel);
            bool armSwitch = hasArmAux && joystick.GetAux(armChannel) > switchThreshold;
            if (!armSwitch) armLowSeen = true;
            if (hasArmAux && armSwitch && !prevArm && armLowSeen)
            {
                if (throttle <= armLow) Arm("тумблер AUX");
                else Debug.LogWarning("[Joystick] Arm отклонён: газ не в нуле.");
            }
            else if (hasArmAux && !armSwitch && prevArm)
            {
                drone.SetArmed(false);
                Debug.Log("[Joystick] DISARMED");
            }
            else if (!hasArmAux && autoArmFromThrottle)
            {
                // No AUX switch and no keyboard in a headset: motors idle as soon as the
                // transmitter is ready with the stick down, and raising the stick takes off.
                // Holding the stick down after a flight disarms, so the motors can be stopped.
                if (drone.Armed)
                {
                    armAtIdle = false;
                    if (drone.Body != null && drone.Body.linearVelocity.sqrMagnitude > 1f) wasAirborne = true;
                    if (wasAirborne && throttle <= armLow)
                    {
                        lowHold += Time.fixedDeltaTime;
                        if (lowHold >= disarmHoldSeconds)
                        {
                            drone.SetArmed(false);
                            armAtIdle = true;
                            blockIdleArm = true;
                            wasAirborne = false;
                            lowHold = 0f;
                            Debug.Log("[Joystick] DISARMED (газ внизу)");
                        }
                    }
                    else lowHold = 0f;
                }
                else
                {
                    if (throttle > autoArmThrottle) blockIdleArm = false;
                    if (throttle <= armLow && !blockIdleArm) armAtIdle = true;
                    if (armAtIdle && !blockIdleArm)
                    {
                        if (throttle <= armLow) Arm("газ внизу — холостой ход");
                        else if (throttle > autoArmThrottle) Arm("подъём газа");
                    }
                }
            }
            prevArm = armSwitch;

            // --- Mode (по фронту) ---
            bool modeSwitch = joystick.GetAux(modeChannel) > switchThreshold;
            if (modeSwitch && !prevMode)
            {
                var newMode = drone.mode == DroneFlightMode.Acro
                    ? DroneFlightMode.Angle
                    : DroneFlightMode.Acro;
                drone.SetMode(newMode);
                Debug.Log($"[Joystick] Mode: {newMode}");
            }
            prevMode = modeSwitch;

            // --- Reset / Respawn (по фронту) ---
            bool resetSwitch = joystick.GetAux(resetChannel) > switchThreshold;
            if (resetSwitch && !prevReset)
            {
                drone.ResetFlight();
                if (checkpoint != null) checkpoint.Respawn();
                Debug.Log("[Joystick] Respawn");
            }
            prevReset = resetSwitch;

            // --- Передаём управление ---
            if (drone.Armed) drone.SetInput(new FlightControlInput(throttle, pitch, yaw, roll));
        }

        /// <summary>Arm at neutral input: SetArmed refuses to engage while collective is above zero.</summary>
        void Arm(string reason)
        {
            if (drone == null || drone.Armed) return;
            drone.SetInput(default);
            drone.SetArmed(true);
            if (drone.Armed) Debug.Log("[Joystick] ARMED (" + reason + ")");
        }

        void Update()
        {
            // With a USB transmitter exposing only four axes, the operator can still arm/reset.
            var keys = UnityEngine.InputSystem.Keyboard.current;
            if (!Application.isFocused || keys == null || joystick == null || !joystick.IsReady) return;
            if (keys.escapeKey.wasPressedThisFrame) drone.SetArmed(false);
            else if (keys.rKey.wasPressedThisFrame) { drone.ResetFlight(); checkpoint?.Respawn(); }
            else if (!joystick.HasAux(armChannel) && keys.spaceKey.wasPressedThisFrame)
            {
                if (drone.Armed) drone.SetArmed(false);
                else if (joystick.Throttle <= Mathf.Max(armThrottleLimit, armLowThreshold)) Arm("Space");
            }
        }

        void LateUpdate()
        {
            if (fpvCamera == null || UnityEngine.XR.XRSettings.isDeviceActive
                || (TryGetComponent<DroneCameraView>(out var cameraView) && cameraView.enabled)) return;
            fpvCamera.transform.localRotation = Quaternion.Euler(-cameraTilt, 0f, 0f);
            fpvCamera.fieldOfView = fieldOfView;
        }

        void OnRespawned()
        {
            drone.SetArmed(false);
            wasAirborne = false;
            lowHold = 0f;
        }
    }
}
