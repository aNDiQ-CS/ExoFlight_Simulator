using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;
public class DroneJoystickInput : MonoBehaviour
{

    [Header("Поиск устройства")]
    [Tooltip("Если пусто — берётся первый найденный Joystick. Если задано — " +
        "ищется устройство, чьё displayName/product содержит эту подстроку " +
        "(регистронезависимо). Для TX12 часто помогает 'RadioMaster' или 'TX12'.")]
    [SerializeField] private string deviceNameFilter = "";

    [Header("Отладка")]
    [Tooltip("Включить, чтобы раз в кадр выводить в консоль все оси устройства " +
        "с их текущими значениями. Используйте для определения маппинга, " +
        "затем выключите — это дорого по производительности.")]
    [SerializeField] private bool debugLogRawValues = false;

    [Tooltip("Как часто логировать (сек), чтобы не заспамить консоль.")]
    [SerializeField] private float debugLogInterval = 0.25f;

    [Header("Маппинг осей (заполнить после отладки через Input Debugger)")]
    [Tooltip("Пример путей: \"axis0\", \"axis1\", \"stick/x\", \"axis2\" — " +
        "зависит от HID-дескриптора TX12, см. инструкцию выше.")]
    [SerializeField] private string throttlePath = "axis1";
    [SerializeField] private string yawPath = "axis0";
    [SerializeField] private string pitchPath = "axis3";
    [SerializeField] private string rollPath = "axis2";

    [Tooltip("Доп. каналы (переключатели/потенциометры TX12 — SA, SB, SC, SD и т.п.). " +
        "Задайте столько путей, сколько ауксов реально нужно игре.")]
    [SerializeField] private List<string> auxPaths = new List<string>();

    [Header("Инверсия и калибровка")]
    [SerializeField] private bool invertThrottle = false;
    [SerializeField] private bool invertYaw = false;
    [SerializeField] private bool invertPitch = false;
    [SerializeField] private bool invertRoll = false;

    [Tooltip("Мёртвая зона по центру стика (0..1), чтобы убрать дрейф на yaw/pitch/roll.")]
    [Range(0f, 0.2f)]
    [SerializeField] private float deadzone = 0.03f;

    public InputDevice Device { get; private set; }

    /// Throttle обычно НЕ центрируется (0..1, газ "от себя"), поэтому
    /// для него дедзона и инверсия применяются отдельно, без обнуления центра.
    public float Throttle { get; private set; }
    public float Yaw { get; private set; }
    public float Pitch { get; private set; }
    public float Roll { get; private set; }

    private readonly List<float> _auxValues = new List<float>();
    private float _debugTimer; 

    public float GetAux(int index)
    {
        if (index < 0 || index >= _auxValues.Count) return 0f;
        return _auxValues[index];
    }

    private void OnEnable()
    {
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        Application.runInBackground = true;
        InputSystem.onDeviceChange += OnDeviceChange;
        TryBindExistingDevice();
    }

    private void OnDisable()
    {
        InputSystem.onDeviceChange -= OnDeviceChange;
        Device = null;
    }

    private void OnDeviceChange(InputDevice device, InputDeviceChange change)
    {
        if (!isCandidate(device)) return;

        switch (change)
        {
            case InputDeviceChange.Added:
            case InputDeviceChange.Reconnected:
                if (Device == null && MatchesFilter(device))
                {
                    Device = device;
                    Debug.Log($"[DroneJoystickInput] Подключено устройство: " +
                         $"{device.displayName} layout = {device.layout}");
                }
                break;

            case InputDeviceChange.Removed:
            case InputDeviceChange.Disconnected:
                if (Device == device)
                    Device = null;
                break;
        }
    }

    private void TryBindExistingDevice()
    {
        foreach (var d in InputSystem.devices)
        {
            if (isCandidate(d) && MatchesFilter(d))
            {
                Device = d;
                Debug.Log($"[DroneJoystickInput] Найдено: {d.displayName} " +
                    $"layout = {d.layout}");
                break;
            }
        }
    }

    private bool MatchesFilter(InputDevice d)
    {
        if (string.IsNullOrEmpty(deviceNameFilter)) return true;

        string name = (d.displayName + " " + d.description.product).ToLowerInvariant();
        return name.Contains(deviceNameFilter.ToLowerInvariant());
    }

    private void FixedUpdate()
    {
        if (Device == null)
        {
            TryBindExistingDevice();
            return;
        }

        if (debugLogRawValues)
        {
            LogRawValues();
        }

        Throttle = ReadAxis(throttlePath, invertThrottle, applyDeadzone: false);
        Yaw = ReadAxis(yawPath, invertYaw, applyDeadzone: true);
        Pitch = ReadAxis(pitchPath, invertPitch, applyDeadzone: true);
        Roll = ReadAxis(rollPath, invertRoll, applyDeadzone: true);

        _auxValues.Clear();
        foreach (var path in auxPaths)
        {
            _auxValues.Add(ReadAxis(path, invert: false, applyDeadzone: false));
        }
    }

    private float ReadAxis(string controlPath, bool invert, bool applyDeadzone)
    {
        if (Device == null || string.IsNullOrEmpty(controlPath)) return 0f;

        var control = Device.TryGetChildControl<AxisControl>(controlPath);
        if (control == null) return 0f;

        float value = control.ReadValue();

        if (applyDeadzone && Mathf.Abs(value) < deadzone)
        {
            value = 0f;
        }

        return invert ? -value : value;
    }

    private void LogRawValues()
    {
        _debugTimer += Time.unscaledDeltaTime;
        if (_debugTimer < debugLogInterval) return;
        _debugTimer = 0f;

        var sb = new StringBuilder();
        sb.AppendLine($"[DroneJoystickInput] Raw controls of '{Device.displayName}':");

        foreach (var control in Device.allControls)
        {
            if (control is AxisControl axis)
            {
                sb.AppendLine($" {axis.name} (path: {axis.path}) = {axis.ReadValue():F3}");
            }
            else if (control is ButtonControl button)
            {
                sb.AppendLine($" {button.name} (path: {button.path}) = {button.ReadValue():F0}");
            }
        }

        Debug.Log(sb.ToString());
    }
    private static bool isCandidate(InputDevice d)
    {
        if (d is Joystick || d is Gamepad) return true;
        return d.description.interfaceName == "Android" && !d.layout.Contains("XR") && !d.layout.Contains("Oculus") && !(d is Keyboard) && !(d is Pointer);
    }
}

