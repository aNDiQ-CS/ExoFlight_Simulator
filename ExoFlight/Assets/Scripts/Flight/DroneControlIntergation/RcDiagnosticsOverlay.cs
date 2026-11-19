using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

namespace ExoFlight.Flight
{
    /// <summary>
    /// Крупная панель диагностики пульта прямо в шлеме: один USB-порт занят передатчиком,
    /// поэтому logcat недоступен. Панель сама появляется после загрузки сцены, висит перед
    /// камерой и обновляется 4 раза в секунду. F3 скрывает/показывает.
    /// </summary>
    [DefaultExecutionOrder(500)]
    public sealed class RcDiagnosticsOverlay : MonoBehaviour
    {
        const float RefreshSeconds = .25f;
        const int MaxAxes = 10;

        static RcDiagnosticsOverlay instance;

        Canvas canvas;
        Text output;
        DroneJoystickInput reader;
        JoystickDroneInput flight;
        DroneInputRouter router;
        DroneFlightController drone;
        float nextRefresh;
        float nextLookup;
        bool visible = true;
        int eventCount;
        double lastEventTime = -1d;
        readonly StringBuilder sb = new StringBuilder(1024);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (instance != null) return;
            var host = new GameObject("RC diagnostics");
            DontDestroyOnLoad(host);
            instance = host.AddComponent<RcDiagnosticsOverlay>();
        }

        void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }
            instance = this;
        }

        void OnEnable() => InputSystem.onEvent += OnEvent;

        void OnDisable() => InputSystem.onEvent -= OnEvent;

        void OnEvent(InputEventPtr evt, InputDevice device)
        {
            if (reader == null || device == null || device != reader.Device) return;
            eventCount++;
            lastEventTime = Time.unscaledTimeAsDouble;
        }

        void LateUpdate()
        {
            if (output == null)
            {
                if (Time.unscaledTime < nextLookup) return;
                nextLookup = Time.unscaledTime + .5f;
                Build();
                if (output == null) return;
            }

            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.f3Key.wasPressedThisFrame) visible = !visible;
            if (canvas.gameObject.activeSelf != visible) canvas.gameObject.SetActive(visible);
            if (!visible || Time.unscaledTime < nextRefresh) return;

            nextRefresh = Time.unscaledTime + RefreshSeconds;
            output.text = Compose();
        }

        void Build()
        {
            var camera = Camera.main;
            if (camera == null)
            {
                foreach (var candidate in Camera.allCameras)
                    if (candidate != null && candidate.isActiveAndEnabled) { camera = candidate; break; }
            }
            if (camera == null) return;

            var root = new GameObject("RC diagnostics canvas", typeof(RectTransform));
            root.transform.SetParent(camera.transform, false);
            root.transform.localPosition = new Vector3(0f, -.12f, .9f);
            root.transform.localRotation = Quaternion.identity;
            root.transform.localScale = Vector3.one * .0007f; // 1200 px -> 0.84 m
            var rootRect = root.GetComponent<RectTransform>();
            rootRect.sizeDelta = new Vector2(1200f, 820f);
            rootRect.pivot = new Vector2(.5f, .5f);

            canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 500;

            var background = new GameObject("background", typeof(RectTransform));
            background.transform.SetParent(root.transform, false);
            var backgroundRect = background.GetComponent<RectTransform>();
            backgroundRect.anchorMin = Vector2.zero;
            backgroundRect.anchorMax = Vector2.one;
            backgroundRect.offsetMin = backgroundRect.offsetMax = Vector2.zero;
            var image = background.AddComponent<Image>();
            image.color = new Color(0f, 0f, 0f, .82f);
            image.raycastTarget = false;

            var label = new GameObject("text", typeof(RectTransform));
            label.transform.SetParent(root.transform, false);
            var labelRect = label.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(28f, 22f);
            labelRect.offsetMax = new Vector2(-28f, -22f);

            output = label.AddComponent<Text>();
            output.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            output.fontSize = 36;
            output.lineSpacing = 1.05f;
            output.color = Color.white;
            output.alignment = TextAnchor.UpperLeft;
            output.horizontalOverflow = HorizontalWrapMode.Overflow;
            output.verticalOverflow = VerticalWrapMode.Overflow;
            output.raycastTarget = false;
        }

        void Lookup()
        {
            if (reader == null) reader = FindAnyObjectByType<DroneJoystickInput>();
            if (flight == null) flight = FindAnyObjectByType<JoystickDroneInput>();
            if (router == null) router = FindAnyObjectByType<DroneInputRouter>();
            if (drone == null) drone = FindAnyObjectByType<DroneFlightController>();
        }

        string Compose()
        {
            Lookup();
            sb.Clear();
            sb.Append("ПУЛЬТ   экран:").Append(Application.isFocused ? "да" : "нет").AppendLine();

            if (reader == null)
            {
                sb.AppendLine("DroneJoystickInput НЕ НАЙДЕН В СЦЕНЕ");
                return sb.ToString();
            }

            sb.Append("устройство: ")
              .Append(reader.Device != null ? reader.Device.displayName : "НЕ НАЙДЕНО")
              .AppendLine();
            sb.Append("готов: ").Append(reader.IsReady ? "ДА" : "НЕТ")
              .Append("   события:").Append(eventCount)
              .Append(lastEventTime < 0d ? "  (тишина)" : "  " + (Time.unscaledTimeAsDouble - lastEventTime).ToString("F1") + "с назад")
              .AppendLine();
            if (!reader.IsReady) sb.Append("причина: ").Append(reader.ProblemSummary).AppendLine();

            sb.Append("газ: ").Append(reader.ThrottleRaw.ToString("F2"))
              .Append(" -> ").Append(reader.Throttle.ToString("F2"))
              .Append(reader.ThrottleCalibrated ? " (калибр.)" : "").AppendLine();
            sb.Append("стики: T=").Append(reader.Throttle.ToString("F2"))
              .Append("  P=").Append(reader.Pitch.ToString("F2"))
              .Append("  Y=").Append(reader.Yaw.ToString("F2"))
              .Append("  R=").Append(reader.Roll.ToString("F2")).AppendLine();
            sb.Append("рыскание: ").Append(reader.YawPathInUse)
              .Append("   газ: ").Append(reader.ActivePathSummary).AppendLine();
            sb.Append("сырое состояние: ").Append(reader.RawScanActive
                    ? "сдвиг " + reader.RawScanPeak.ToString("F2") + "  offset " + reader.RawScanOffset
                    : "нет (не Android)")
              .AppendLine();
            sb.Append("источник: ").Append(router != null ? router.Current.ToString() : "?")
              .Append("   моторы: ").Append(drone != null && drone.Armed ? "ВКЛ" : "ВЫКЛ")
              .Append("   flight T=").Append(flight != null ? flight.Throttle.ToString("F2") : "-")
              .AppendLine();

            sb.AppendLine("--- оси устройства ---");
            if (reader.Device != null) AppendAxes(sb, reader.Device);
            return sb.ToString();
        }

        static void AppendAxes(StringBuilder builder, InputDevice device)
        {
            int used = 0;
            string prefix = device.path + "/";
            foreach (var control in device.allControls)
            {
                if (!(control is AxisControl axis) || axis.synthetic) continue;
                if (used == MaxAxes) { builder.Append("..."); break; }
                if (used > 0 && used % 3 == 0) builder.AppendLine();
                else if (used > 0) builder.Append("   ");
                string name = axis.path.StartsWith(prefix, System.StringComparison.Ordinal)
                    ? axis.path.Substring(prefix.Length) : axis.name;
                builder.Append(name).Append('=').Append(axis.ReadValue().ToString("F2"));
                used++;
            }
            builder.AppendLine();
        }
    }
}
