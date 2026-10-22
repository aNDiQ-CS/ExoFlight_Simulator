using ExoFlight.Triggers;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ExoFlight.Flight
{
    [DisallowMultipleComponent, RequireComponent(typeof(DroneFlightController))]
    public sealed class KeyboardDroneInput : MonoBehaviour
    {
        [Min(.01f)] public float throttleChangePerSecond = .25f;
        [Min(.01f)] public float stickDeflectionPerSecond = 2.5f;
        [Min(.01f)] public float stickReturnPerSecond = 6f;
        public Camera fpvCamera;
        [Range(-10f, 45f)] public float cameraTilt = 10f;
        [Range(50f, 120f)] public float fieldOfView = 85f;

        readonly KeyboardStickState sticks = new KeyboardStickState();
        DroneFlightController drone;
        PlayerCheckpoint checkpoint;
        public float Throttle => sticks.Throttle;

        void Awake()
        {
            drone = GetComponent<DroneFlightController>();
            checkpoint = GetComponent<PlayerCheckpoint>();
        }

        void OnEnable()
        {
            if (checkpoint != null) checkpoint.Respawned += ResetInput;
        }

        void Start()
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            Debug.Log("[Flight] " + drone.mode + " | Space: arm/disarm | W/S: throttle | A/D: yaw | arrows: pitch/roll | Shift: precision | F: Acro/Angle | R: reset | Esc: release");
        }

        void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null || !Application.isFocused) return;
            if (keyboard.escapeKey.wasPressedThisFrame)
            {
                Release();
                return;
            }
            if (Cursor.lockState != CursorLockMode.Locked)
            {
                // The Editor can unlock the cursor without delivering Escape to the game.
                // Never leave the last non-zero stick command latched in that state.
                Release();
                bool startWithSpace = keyboard.spaceKey.wasPressedThisFrame;
                bool clicked = Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
                if (!startWithSpace && !clicked) return;
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
                // A single Space both captures the game and arms the motors.
                if (!startWithSpace) return;
            }
            ReadKeyboard(keyboard, Mathf.Min(Time.unscaledDeltaTime, .1f));
        }

        /// <summary>Separate sampling entry point also exercised by the integration checks.</summary>
        public void ReadKeyboard(Keyboard keyboard, float dt)
        {
            if (keyboard.rKey.wasPressedThisFrame)
            {
                ResetInput();
                drone.ResetFlight();
                if (checkpoint != null) checkpoint.Respawn();
                return;
            }
            if (keyboard.fKey.wasPressedThisFrame)
            {
                drone.SetMode(drone.mode == DroneFlightMode.Acro ? DroneFlightMode.Angle : DroneFlightMode.Acro);
                Debug.Log("[Flight] Mode: " + drone.mode);
            }
            if (keyboard.spaceKey.wasPressedThisFrame)
            {
                if (drone.Armed)
                {
                    drone.SetArmed(false);
                    ResetInput();
                    Debug.Log("[Flight] Моторы выключены (DISARMED).");
                }
                else
                {
                    ResetInput();
                    drone.SetArmed(true);
                    Debug.Log("[Flight] Моторы включены (ARMED), прежний газ сброшен. W — добавить газ.");
                }
            }
            float throttle = (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f);
            Vector3 axes = new Vector3(
                (keyboard.upArrowKey.isPressed ? 1f : 0f) - (keyboard.downArrowKey.isPressed ? 1f : 0f),
                (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f),
                (keyboard.rightArrowKey.isPressed ? 1f : 0f) - (keyboard.leftArrowKey.isPressed ? 1f : 0f));
            drone.SetInput(sticks.Step(throttle, axes, dt, throttleChangePerSecond,
                stickDeflectionPerSecond, stickReturnPerSecond, keyboard.leftShiftKey.isPressed));
        }

        void LateUpdate()
        {
            if (fpvCamera == null) return;
            fpvCamera.transform.localRotation = Quaternion.Euler(-cameraTilt, 0f, 0f);
            fpvCamera.fieldOfView = fieldOfView;
        }

        public void ResetInput()
        {
            sticks.Reset();
            if (drone != null) drone.SetInput(default);
        }

        void Release()
        {
            ResetInput();
            if (drone != null) drone.SetArmed(false);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        void OnApplicationFocus(bool focused) { if (!focused) Release(); }
        void OnApplicationPause(bool paused) { if (paused) Release(); }

        void OnDisable()
        {
            if (checkpoint != null) checkpoint.Respawned -= ResetInput;
            Release();
        }
    }
}
