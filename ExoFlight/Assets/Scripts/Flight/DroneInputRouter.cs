using UnityEngine;
using UnityEngine.InputSystem;

namespace ExoFlight.Flight
{
    /// <summary>
    /// Держит включённым ровно один источник управления дроном: клавиатуру или джойстик.
    /// Оба компонента пишут в DroneFlightController каждый кадр, поэтому одновременно они
    /// несовместимы: роутер гасит лишний и обнуляет последнюю команду.
    /// Переключение — клавишей (по умолчанию F1); при подключении/отключении джойстика
    /// источник выбирается сам.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(DroneFlightController))]
    public sealed class DroneInputRouter : MonoBehaviour
    {
        public enum Source { Keyboard = 0, Joystick = 1, Autopilot = 2 }

        [Header("Ссылки (находятся сами, если пусто)")]
        [SerializeField] private DroneFlightController drone;
        [SerializeField] private KeyboardDroneInput keyboard;
        [SerializeField] private DroneJoystickInput joystickReader;
        [SerializeField] private JoystickDroneInput joystickFlight;

        [Header("Поведение")]
        [Tooltip("Если джойстик подключён — он главный по умолчанию.")]
        [SerializeField] private bool preferJoystickWhenPresent = true;

        [Tooltip("Клавиша ручного переключения источника ввода (например F1).")]
        [SerializeField] private string toggleKeyName = "F1";

        [Tooltip("Запирать курсор при возврате на клавиатуру.")]
        [SerializeField] private bool lockCursorForKeyboard = true;

        [Tooltip("Писать в консоль при смене источника.")]
        [SerializeField] private bool logSwitches = true;

        public Source Current { get; private set; } = Source.Keyboard;

        public bool JoystickAvailable =>
            joystickReader != null && joystickReader.IsReady && joystickFlight != null;

        private bool manual;
        private bool applied;
        private bool hasToggleKey;
        private Key toggleKey = Key.F1;

        private void Awake()
        {
            if (drone == null) drone = GetComponent<DroneFlightController>();
            if (keyboard == null) keyboard = GetComponent<KeyboardDroneInput>();
            if (joystickReader == null) joystickReader = GetComponent<DroneJoystickInput>();
            if (joystickFlight == null) joystickFlight = GetComponent<JoystickDroneInput>();
            hasToggleKey = System.Enum.TryParse(toggleKeyName, true, out toggleKey);
            if (!hasToggleKey && logSwitches)
                Debug.LogWarning("[Input] не понял клавишу переключения «" + toggleKeyName + "», беру F1");
            if (!hasToggleKey) toggleKey = Key.F1;
        }

        private void OnEnable()
        {
            Apply(Preferred(), "включение");
        }

        private void Update()
        {
            if (Current == Source.Autopilot) return;
            // A manual selection must not suppress the disconnect failsafe.
            if (Current == Source.Joystick && !JoystickAvailable)
            {
                drone.SetArmed(false);
                manual = false;
                Apply(Source.Keyboard, "пульт отключён или каналы недоступны");
                return;
            }
            var kb = Keyboard.current;
            if (kb != null && kb[toggleKey].wasPressedThisFrame)
            {
                manual = true;
                Apply(Current == Source.Keyboard && JoystickAvailable ? Source.Joystick : Source.Keyboard,
                    "клавиша " + toggleKeyName);
                return;
            }

            if (manual) return;

            var want = Preferred();
            if (!applied || want != Current) Apply(want, "состояние устройств");
        }

        public void AcquireAutopilot() => Apply(Source.Autopilot, "автопилот");

        public void ReleaseAutopilot(bool disarm)
        {
            float throttle = drone.Armed && !disarm ? drone.Input.Throttle : 0f;
            if (disarm) drone.SetArmed(false);
            // A physical RC throttle may be anywhere. Hand off in flight to the keyboard
            // with retained collective; select an RC transmitter explicitly after landing.
            manual = true;
            Apply(Source.Keyboard, "автопилот завершён");
            keyboard.ResumeManualControl(throttle);
            keyboard.SuppressCurrentFrame();
        }

        /// <summary>Снять ручной выбор и вернуться к автоматике.</summary>
        public void UseAutomaticSource()
        {
            if (Current == Source.Autopilot) return;
            manual = false;
            Apply(Preferred(), "автоматически");
        }

        private Source Preferred()
        {
            return preferJoystickWhenPresent && JoystickAvailable ? Source.Joystick : Source.Keyboard;
        }

        private void Apply(Source source, string why)
        {
            bool changed = !applied || source != Current;
            Current = source;
            applied = true;

            if (keyboard != null) keyboard.enabled = source == Source.Keyboard;
            if (joystickFlight != null) joystickFlight.enabled = source == Source.Joystick;

            // Никто не должен унаследовать последнюю команду выключенного источника.
            if (drone != null) drone.SetInput(default);

            if (source == Source.Keyboard && lockCursorForKeyboard)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }

            if (changed && logSwitches)
            {
                Debug.Log("[Input] управление: " +
                          source +
                          " (" + why + ") — " + toggleKeyName + " переключает");
            }
        }
    }
}
