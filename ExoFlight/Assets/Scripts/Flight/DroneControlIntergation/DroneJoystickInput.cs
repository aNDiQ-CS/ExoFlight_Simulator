using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;
[DefaultExecutionOrder(-250)]
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

    [Header("Маппинг осей (сверять с Input Debugger на конкретной платформе)")]
    [Tooltip("Android/шлем: TX12 определяется как Gamepad/AndroidGamepad, поэтому " +
        "рабочие пути — rightStick/* и leftStick/*. " +
        "Windows: тот же TX12 виден как Joystick с путями stick/x, stick/y, z, rz — " +
        "их нужно выставить в Inspector вручную.")]
    // Android/headset config verified with the real transmitter. A scene can override
    // these; keep them here so transferring only this .cs keeps the working mapping.
    [SerializeField] private string throttlePath = "rightStick/x";
    [SerializeField] private string yawPath = "rightStick/y";
    [SerializeField] private string pitchPath = "leftStick/y";
    [SerializeField] private string rollPath = "leftStick/x";

    [Tooltip("Доп. каналы (переключатели/потенциометры TX12 — SA, SB, SC, SD и т.п.). " +
        "Задайте столько путей, сколько ауксов реально нужно игре.")]
    [SerializeField] private List<string> auxPaths = new List<string>();

    [Header("Fallback для HID-джойстика")]
    [Tooltip("Если основных путей rightStick/leftStick нет, а устройство — Joystick " +
        "(например HID::OpenTX Radiomaster TX12 Joystick в редакторе/Windows), " +
        "читать эти пути. Порядок AETR: CH1 крен → stick/x, CH2 тангаж → stick/y, " +
        "CH3 газ → z, CH4 рыскание → rx (у OpenTX/EdgeTX это ось Rx, не Rz).")]
    [SerializeField] private bool useHidJoystickFallback = true;
    [SerializeField] private string hidThrottlePath = "z";
    [Tooltip("CH4 в USB-режиме пульта (OpenTX/EdgeTX) обычно отдаётся как ось Rx, а не Rz.")]
    [SerializeField] private string hidYawPath = "rx";
    [SerializeField] private string hidPitchPath = "stick/y";
    [SerializeField] private string hidRollPath = "stick/x";
    [Tooltip("Unity сам инвертирует HID Y, поэтому для stick/y инверсия обычно НЕ нужна " +
        "(в отличие от leftStick/y на Android-геймпаде).")]
    [SerializeField] private bool hidInvertPitch = false;

    [Header("Рыскание: автопоиск оси")]
    [Tooltip("Если ось рыскания молчит, а одна из этих осей двигается — используется она. " +
        "CH4 в USB-режиме пульта обычно называется rx, на Android-геймпаде — rightStick/y.")]
    [SerializeField] private List<string> yawCandidates = new List<string>
        { "rx", "rightStick/y", "rz" };

    [Header("Инверсия и калибровка")]
    [SerializeField] private bool invertThrottle = false;
    [SerializeField] private bool invertYaw = false;
    [Tooltip("Проверено на реальном TX12 (Android/шлем): leftStick/y инвертируется.")]
    [SerializeField] private bool invertPitch = true;
    [SerializeField] private bool invertRoll = false;
    [Tooltip("Raw throttle endpoints from Input Debugger. Most USB RC axes use -1..1.")]
    public Vector2 throttleRange = new Vector2(-1f, 1f);
    [Tooltip("Enable only after verifying the four channel paths and inversions on the actual transmitter.")]
    public bool mappingVerified;

    [Tooltip("Player build only: treat the mapping as verified when all four axes exist, " +
        "even if Mapping Verified is off. Lets a test build use the transmitter by default. " +
        "The editor still requires Mapping Verified.")]
    public bool trustMappingInPlayerBuild = true;

    [Tooltip("Мёртвая зона по центру стика (0..1), чтобы убрать дрейф на yaw/pitch/roll.")]
    [Range(0f, 0.2f)]
    [SerializeField] private float deadzone = 0.03f;

    public InputDevice Device { get; private set; }

    // Actual paths used this frame: primary (Gamepad) or HID fallback (Joystick).
    string activeThrottlePath, activeYawPath, activePitchPath, activeRollPath;
    bool bindingsResolved, usingHidFallback;

    bool MappingApproved => mappingVerified || (!Application.isEditor && trustMappingInPlayerBuild);

    public bool IsReady
    {
        get
        {
            RefreshBindings();
            // RadioTest proves the transmitter delivers values without checking Device.enabled,
            // so neither that flag nor the added flag may block manual control.
            return MappingApproved && Device != null && bindingsResolved
                && Mathf.Abs(throttleRange.y - throttleRange.x) > .001f;
        }
    }

    /// <summary>Raw throttle axis this frame, before normalisation (diagnostics).</summary>
    public float ThrottleRaw { get; private set; }

    /// <summary>True once the throttle stick travelled enough to normalise it from observations.</summary>
    public bool ThrottleCalibrated =>
        throttleMin <= throttleMax && throttleMax - throttleMin >= .5f;

    float throttleMin = float.PositiveInfinity, throttleMax = float.NegativeInfinity;
    InputDevice lastDevice;
    string lastThrottlePath;

    void ResetThrottleCalibration()
    {
        throttleMin = float.PositiveInfinity;
        throttleMax = float.NegativeInfinity;
        ThrottleRaw = 0f;
    }

    public bool HasAux(int index) => index >= 0 && index < auxPaths.Count && HasAxis(auxPaths[index]);

    // Read-only diagnostics for the in-headset overlay (no adb cable on the single USB port).
    public bool UsingHidFallback => usingHidFallback;
    public bool BindingsResolved => bindingsResolved;
    public string ActivePathSummary => ActivePaths();
    public string ProblemSummary => MappingProblem();
    public string YawPathInUse => yawFromRaw
        ? $"сырая ось (offset {rawYawOffset})"
        : string.IsNullOrEmpty(yawPathInUse) ? activeYawPath : yawPathInUse;

    string yawPathInUse;
    float yawSilence;
    float yawCooldown;
    bool yawFromRaw;
    int rawYawOffset = -1;
    byte[] rawBaseline;
    float baselineAge;

    /// <summary>
    /// Unity's Android gamepad layout exposes only 4 of the axes in the device state. CH4 (yaw)
    /// arrives in the raw state at an offset that has no control, so find it directly: snapshot
    /// the axis area, and adopt the float that changes while the configured yaw axis is silent.
    /// </summary>
    float ReadRawAndroidYaw()
    {
        var device = Device;
        if (device == null || !device.layout.Contains("Android")) return 0f;

        byte[] state;
        try { state = device.ReadValueAsObject() as byte[]; }
        catch (System.Exception) { return 0f; }
        if (state == null || state.Length < 8) return 0f;

        if (rawYawOffset >= 0)
        {
            if (rawYawOffset + sizeof(float) > state.Length) { rawYawOffset = -1; return 0f; }
            float value = System.BitConverter.ToSingle(state, rawYawOffset);
            return float.IsNaN(value) || Mathf.Abs(value) > 2f ? 0f : value;
        }

        if (!TryAxisRegion(state.Length, out int start, out int end, out int[] used)) return 0f;
        RawScanActive = true;
        baselineAge += Time.fixedDeltaTime;
        if (rawBaseline == null || rawBaseline.Length != state.Length)
        {
            rawBaseline = (byte[])state.Clone();
            baselineAge = 0f;
            return 0f;
        }

        int found = -1;
        int bestOffset = -1;
        float bestDelta = 0f;
        for (int offset = start; offset + sizeof(float) <= end; offset += sizeof(float))
        {
            if (System.Array.IndexOf(used, offset) >= 0) continue;
            float now = System.BitConverter.ToSingle(state, offset);
            if (float.IsNaN(now) || Mathf.Abs(now) > 1.5f) continue;
            float delta = Mathf.Abs(now - System.BitConverter.ToSingle(rawBaseline, offset));
            if (delta > bestDelta) { bestDelta = delta; bestOffset = offset; }
            if (delta <= .4f) continue;
            found = offset;
        }
        RawScanPeak = bestDelta;
        RawScanOffset = bestOffset;

        if (found < 0)
        {
            if (baselineAge > .5f) { rawBaseline = (byte[])state.Clone(); baselineAge = 0f; }
            return 0f;
        }

        rawYawOffset = found;
        Debug.Log($"[DroneJoystickInput] Рыскание: сырая ось найдена в состоянии устройства (offset {found}).");
        return System.BitConverter.ToSingle(state, found);
    }

    /// <summary>Largest change seen in the raw axis area — proves whether the channel arrives at all.</summary>
    public float RawScanPeak { get; private set; }

    /// <summary>Offset of that change inside the device state, -1 when nothing moves.</summary>
    public int RawScanOffset { get; private set; } = -1;

    /// <summary>True when the state was scanned at least once (Android layout only).</summary>
    public bool RawScanActive { get; private set; }

    /// <summary>Axis area of AndroidGameControllerState plus the four offsets already in use.</summary>
    bool TryAxisRegion(int stateLength, out int start, out int end, out int[] used)
    {
        start = end = -1;
        used = null;
        var z = Device != null ? Device.TryGetChildControl<AxisControl>("rightStick/x") : null;
        if (z == null) return false; // не Android-раскладка
        int zOffset = (int)z.stateBlock.byteOffset; // AXIS_Z
        start = zOffset - 11 * sizeof(float);       // AXIS_X
        end = Mathf.Min(stateLength, start + 48 * sizeof(float));
        var list = new System.Collections.Generic.List<int>(5);
        foreach (var path in new[] { activeThrottlePath, activePitchPath, activeRollPath, yawPathInUse })
        {
            var control = Device.TryGetChildControl<AxisControl>(path);
            if (control != null) list.Add((int)control.stateBlock.byteOffset);
        }
        used = list.ToArray();
        return end - start >= 4 * sizeof(float);
    }

    /// <summary>
    /// CH4 is exposed as Rx in HID and has no control at all in Unity's Android gamepad layout,
    /// so the configured yaw path can exist while never moving. Use the raw Android RX axis when
    /// it moves, otherwise adopt whichever candidate axis actually moves.
    /// </summary>
    void UpdateYawBinding(float dt, float rawYaw)
    {
        if (string.IsNullOrEmpty(yawPathInUse)) yawPathInUse = activeYawPath;
        if (yawCooldown > 0f) yawCooldown -= dt;
        if (yawFromRaw) return;

        float current = Mathf.Abs(ReadAxis(yawPathInUse, false, false));
        yawSilence = current > .15f ? 0f : yawSilence + dt;

        if (rawYawOffset >= 0 && Mathf.Abs(rawYaw) > .2f && yawSilence > .3f)
        {
            yawFromRaw = true;
            yawSilence = 0f;
            Debug.Log("[DroneJoystickInput] Рыскание: читаю сырую ось Android RX (CH4).");
            return;
        }
        if (yawSilence < .4f || yawCooldown > 0f) return;

        string bestPath = FindMovingAxis(.3f, true) ?? FindMovingAxis(.5f, false);
        if (bestPath == null) return;
        yawPathInUse = bestPath;
        yawSilence = 0f;
        yawCooldown = 2f;
        Debug.Log($"[DroneJoystickInput] Рыскание: ось '{activeYawPath}' молчит, использую '{bestPath}'.");
    }

    string FindMovingAxis(float threshold, bool candidatesOnly)
    {
        if (candidatesOnly)
        {
            string best = null;
            float bestValue = threshold;
            foreach (var path in yawCandidates)
            {
                if (string.IsNullOrEmpty(path) || path == yawPathInUse || !HasAxis(path)) continue;
                float value = Mathf.Abs(ReadAxis(path, false, false));
                if (value <= bestValue) continue;
                bestValue = value;
                best = path;
            }
            return best;
        }

        if (Device == null) return null;
        string result = null;
        float peak = threshold;
        string prefix = Device.path + "/";
        foreach (var control in Device.allControls)
        {
            if (!(control is AxisControl axis) || axis.synthetic) continue;
            string path = axis.path.StartsWith(prefix, System.StringComparison.Ordinal)
                ? axis.path.Substring(prefix.Length) : axis.name;
            if (path == yawPathInUse || path == activeThrottlePath || path == activePitchPath
                || path == activeRollPath) continue;
            if (path.Contains("dpad") || path.Contains("hat")) continue;
            float value = Mathf.Abs(axis.ReadValue());
            if (value <= peak) continue;
            peak = value;
            result = path;
        }
        return result;
    }

    bool HasAxis(string path) => Device != null && !string.IsNullOrEmpty(path)
        && Device.TryGetChildControl<AxisControl>(path) != null;

    void RefreshBindings()
    {
        if (Device == null)
        {
            bindingsResolved = false;
            usingHidFallback = false;
            activeThrottlePath = throttlePath;
            activeYawPath = yawPath;
            activePitchPath = pitchPath;
            activeRollPath = rollPath;
            return;
        }

        if (HasAxis(throttlePath) && HasAxis(yawPath) && HasAxis(pitchPath) && HasAxis(rollPath))
        {
            activeThrottlePath = throttlePath;
            activeYawPath = yawPath;
            activePitchPath = pitchPath;
            activeRollPath = rollPath;
            usingHidFallback = false;
            bindingsResolved = true;
            return;
        }

        if (useHidJoystickFallback
            && HasAxis(hidThrottlePath) && HasAxis(hidYawPath)
            && HasAxis(hidPitchPath) && HasAxis(hidRollPath))
        {
            activeThrottlePath = hidThrottlePath;
            activeYawPath = hidYawPath;
            activePitchPath = hidPitchPath;
            activeRollPath = hidRollPath;
            usingHidFallback = true;
            bindingsResolved = true;
            return;
        }

        activeThrottlePath = throttlePath;
        activeYawPath = yawPath;
        activePitchPath = pitchPath;
        activeRollPath = rollPath;
        usingHidFallback = false;
        bindingsResolved = false;
    }

    bool PitchInverted => usingHidFallback ? hidInvertPitch : invertPitch;

    void ClearValues() { Throttle = Yaw = Pitch = Roll = 0f; _auxValues.Clear(); }

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
        ClearValues();
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
                    LogBinding(device);
                }
                break;

            case InputDeviceChange.Removed:
            case InputDeviceChange.Disconnected:
            case InputDeviceChange.Disabled:
                if (Device == device)
                {
                    Device = null;
                    ClearValues();
                }
                break;
        }
    }

    private void TryBindExistingDevice()
    {
        foreach (var d in InputSystem.devices)
        {
            if (d.enabled && isCandidate(d) && MatchesFilter(d))
            {
                Device = d;
                LogBinding(d);
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

    private void LogBinding(InputDevice device)
    {
        RefreshBindings();
        string header = $"[DroneJoystickInput] Устройство: {device.displayName} " +
                        $"layout = {device.layout}" +
                        (usingHidFallback ? " [HID fallback]" : "");
        if (IsReady)
        {
            Debug.Log(header + " — пульт готов. Оси: " + ActivePaths());
            return;
        }
        Debug.LogWarning(header + " — пульт не готов: " + MappingProblem());
    }

    private string ActivePaths() => $"T '{activeThrottlePath}', Y '{activeYawPath}', " +
                                    $"P '{activePitchPath}', R '{activeRollPath}'";

    private string MappingProblem()
    {
        var missing = new List<string>();
        if (!HasAxis(activeThrottlePath)) missing.Add($"throttle '{activeThrottlePath}'");
        if (!HasAxis(activeYawPath)) missing.Add($"yaw '{activeYawPath}'");
        if (!HasAxis(activePitchPath)) missing.Add($"pitch '{activePitchPath}'");
        if (!HasAxis(activeRollPath)) missing.Add($"roll '{activeRollPath}'");
        if (missing.Count > 0)
            return "не найдены оси: " + string.Join(", ", missing) +
                   ". Проверьте маппинг или включите HID fallback";
        if (!MappingApproved) return "не включён Mapping Verified (в редакторе)";
        if (Mathf.Abs(throttleRange.y - throttleRange.x) <= .001f) return "пустой Throttle Range";
        return "неизвестная причина";
    }

    private void FixedUpdate()
    {
        if (Device == null)
        {
            TryBindExistingDevice();
            ClearValues();
            return;
        }

        if (debugLogRawValues)
        {
            LogRawValues();
        }

        RefreshBindings();
        if (!IsReady) { ClearValues(); return; }

        // A different device or bond set invalidates the throttle travel seen so far.
        if (Device != lastDevice || !string.Equals(lastThrottlePath, activeThrottlePath,
                System.StringComparison.Ordinal))
        {
            lastDevice = Device;
            lastThrottlePath = activeThrottlePath;
            ResetThrottleCalibration();
            yawPathInUse = activeYawPath;
            yawSilence = 0f;
            yawFromRaw = false;
            rawYawOffset = -1;
            rawBaseline = null;
            baselineAge = 0f;
            RawScanPeak = 0f;
            RawScanOffset = -1;
            RawScanActive = false;
        }

        // Some transmitters report -1..1, some 0..1, some with an offset. Normalise the gas
        // against the travel actually observed, so "stick down" is always zero thrust.
        float raw = ReadAxis(activeThrottlePath, false, false);
        ThrottleRaw = raw;
        throttleMin = Mathf.Min(throttleMin, raw);
        throttleMax = Mathf.Max(throttleMax, raw);
        float low = ThrottleCalibrated ? throttleMin : throttleRange.x;
        float high = ThrottleCalibrated ? throttleMax : throttleRange.y;
        Throttle = high - low > .001f ? Mathf.Clamp01((raw - low) / (high - low)) : 0f;
        if (invertThrottle) Throttle = 1f - Throttle;
        float rawYaw = ReadRawAndroidYaw();
        UpdateYawBinding(Time.fixedDeltaTime, rawYaw);
        float yawValue = yawFromRaw ? rawYaw : ReadAxis(YawPathInUse, false, false);
        if (Mathf.Abs(yawValue) < deadzone) yawValue = 0f;
        Yaw = invertYaw ? -yawValue : yawValue;
        Pitch = ReadAxis(activePitchPath, PitchInverted, applyDeadzone: true);
        Roll = ReadAxis(activeRollPath, invertRoll, applyDeadzone: true);

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

