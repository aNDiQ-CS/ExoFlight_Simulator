using ExoFlight.Triggers;
using UnityEngine;

namespace ExoFlight.Flight
{
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
            if (checkpoint != null) checkpoint.Respawned += OnRespawned;
        }

        void OnDisable()
        {
            if (checkpoint != null) checkpoint.Respawned -= OnRespawned;
            drone?.SetArmed(false);
        }

        void FixedUpdate()
        {
            if (joystick == null || drone == null) return;

            // --- Читаем оси ---
            float throttle = Mathf.Clamp01(joystick.Throttle);
            float pitch = Mathf.Clamp(joystick.Pitch, -1f, 1f);
            float yaw = Mathf.Clamp(joystick.Yaw, -1f, 1f);
            float roll = Mathf.Clamp(joystick.Roll, -1f, 1f);

            Throttle = throttle;

            // --- Arm / Disarm (по фронту тумблера) ---
            bool armSwitch = joystick.GetAux(armChannel) > switchThreshold;
            if (armSwitch && !prevArm)
            {
                if (throttle <= armThrottleLimit)
                {
                    drone.SetArmed(true);
                    Debug.Log("[Joystick] ARMED");
                }
                else
                {
                    Debug.LogWarning("[Joystick] Arm отклонён: газ не в нуле.");
                }
            }
            else if (!armSwitch && prevArm)
            {
                drone.SetArmed(false);
                Debug.Log("[Joystick] DISARMED");
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
            drone.SetInput(new FlightControlInput(throttle, pitch, yaw, roll));
        }

        void LateUpdate()
        {
            if (fpvCamera == null) return;
            fpvCamera.transform.localRotation = Quaternion.Euler(-cameraTilt, 0f, 0f);
            fpvCamera.fieldOfView = fieldOfView;
        }

        void OnRespawned()
        {
            drone.SetArmed(false);
        }
    }
}