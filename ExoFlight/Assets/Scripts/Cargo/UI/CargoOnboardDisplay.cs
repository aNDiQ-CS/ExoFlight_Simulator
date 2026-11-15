using UnityEngine;
using UnityEngine.UI;

namespace ExoFlight.Cargo.UI
{
    /// <summary>Compact spatial task panel. Observes cargo state, never pilots or changes physics.</summary>
    [DefaultExecutionOrder(300)]
    public sealed class CargoOnboardDisplay : MonoBehaviour
    {
        public CargoDeliveryMission mission;
        public Transform assembly;
        public Canvas displayCanvas;
        public CanvasGroup visibility;
        public Text title, instruction, metric, metricUnit, phaseNumber;
        public Text[] stages;
        public Image progressFill, statusLight;
        public Color activeColor = new Color(.88f, .69f, .38f);
        public Color carryingColor = new Color(.46f, .74f, .67f);
        [Min(.01f)] public float deploySeconds = .35f;
        [Min(.01f)] public float hideSeconds = .2f;
        [SerializeField] bool taskEngaged;
        public bool TaskEngaged => taskEngaged;
        public bool Visible => assembly != null && assembly.gameObject.activeInHierarchy;
        Vector3 deployedPosition;
        float deployment;

        void Awake()
        {
            deployedPosition = assembly.localPosition;
            Hide();
        }

        void OnEnable()
        {
            if (mission == null) return;
            mission.restarted.AddListener(ResetDisplay);
            mission.completed.AddListener(FinishTask);
        }

        void OnDisable()
        {
            if (mission != null)
            {
                mission.restarted.RemoveListener(ResetDisplay);
                mission.completed.RemoveListener(FinishTask);
            }
            ResetDisplay();
        }

        void LateUpdate() => RefreshPresentation(Time.deltaTime);

        public void RefreshPresentation(float dt)
        {
            if (!isActiveAndEnabled || mission == null || !mission.isActiveAndEnabled
                || mission.carrier == null || !mission.carrier.isActiveAndEnabled)
            {
                ResetDisplay();
                return;
            }
            if (mission.State == CargoMissionState.Completed || mission.State == CargoMissionState.Failed)
            {
                taskEngaged = false;
                Animate(false, dt);
                return;
            }
            // AwaitingPickup alone is not an accepted task. First enter its actual trigger volume.
            if (!taskEngaged && (InZone(mission.pickupZone)
                || mission.State == CargoMissionState.Transporting || mission.State == CargoMissionState.AwaitingLanding))
                taskEngaged = true;
            if (!taskEngaged) { Hide(); return; }
            Animate(true, dt);
            UpdateContents();
        }

        void Animate(bool show, float dt)
        {
            float duration = Mathf.Max(.01f, show ? deploySeconds : hideSeconds);
            deployment = Mathf.MoveTowards(deployment, show ? 1f : 0f, Mathf.Max(0f, dt) / duration);
            if (deployment <= 0f) { Hide(); return; }
            assembly.gameObject.SetActive(true);
            float eased = deployment * deployment * (3f - 2f * deployment);
            if (visibility != null) visibility.alpha = eased;
            assembly.localPosition = deployedPosition + Vector3.down * (.065f * (1f - eased));
        }

        public bool InZone(CargoHoverZone zone)
        {
            if (zone == null || !zone.isActiveAndEnabled || !zone.Volume.enabled) return false;
            var body = mission.carrier.GetComponent<Rigidbody>();
            Vector3 point = zone.transform.InverseTransformPoint(body.position) - zone.Volume.center;
            Vector3 half = zone.Volume.size * .5f;
            return Mathf.Abs(point.x) <= half.x && Mathf.Abs(point.y) <= half.y && Mathf.Abs(point.z) <= half.z;
        }

        void UpdateContents()
        {
            bool pickup = mission.State == CargoMissionState.AwaitingPickup;
            bool landing = mission.State == CargoMissionState.AwaitingLanding;
            bool deliveryArea = !pickup && InZone(mission.deliveryZone);
            int step = pickup ? 0 : deliveryArea || landing ? 2 : 1;
            Color color = pickup ? activeColor : carryingColor;
            float progress = Mathf.Clamp01(mission.HoldProgress / Mathf.Max(.1f, mission.holdSeconds));
            title.text = pickup ? (progress > 0f ? "Захват груза" : "Подбор груза")
                : landing ? "Груз отпущен" : deliveryArea ? "Доставка груза" : "Груз на борту";
            instruction.text = landing ? "Проверяем приземление на площадке"
                : pickup && !InZone(mission.pickupZone) ? "Вернитесь в область подбора"
                : pickup || deliveryArea ? (progress > 0f ? "Удерживайте позицию" : "Зависните над площадкой")
                : "Следуйте к площадке доставки";
            if (!landing && (pickup || deliveryArea))
            {
                metric.text = Mathf.Max(0f, mission.holdSeconds - mission.HoldProgress).ToString("F1");
                metricUnit.text = "СЕКУНД";
            }
            else if (landing) { metric.text = "03"; metricUnit.text = "ПОСАДКА"; progress = 1f; }
            else
            {
                Vector3 delta = mission.deliveryZone.Volume.bounds.center - mission.carrier.transform.position;
                metric.text = Vector3.ProjectOnPlane(delta, Vector3.up).magnitude.ToString("F0");
                metricUnit.text = "МЕТРОВ";
            }
            phaseNumber.text = $"0{step + 1} / 03";
            for (int i = 0; i < stages.Length; i++)
                stages[i].color = i == step ? color : new Color(.42f, .47f, .48f);
            statusLight.color = color;
            metric.color = color;
            progressFill.color = color;
            progressFill.rectTransform.anchorMax = new Vector2(progress, 1f);
        }

        public void ResetDisplay()
        {
            taskEngaged = false;
            Hide();
        }

        void FinishTask() => taskEngaged = false;

        void Hide()
        {
            deployment = 0f;
            if (visibility != null) visibility.alpha = 0f;
            if (assembly != null) assembly.gameObject.SetActive(false);
        }
    }
}
