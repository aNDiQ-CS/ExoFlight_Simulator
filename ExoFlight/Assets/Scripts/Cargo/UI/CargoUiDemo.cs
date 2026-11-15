using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace ExoFlight.Cargo.UI
{
    public enum CargoUiMode { FixedWorld = 1, BillboardWorld = 2, CameraNotice = 3, ScreenFeedback = 4 }

    /// <summary>Presentation only: observes the existing mission and never controls pickup or flight.</summary>
    [DefaultExecutionOrder(300)]
    public sealed class CargoUiDemo : MonoBehaviour
    {
        public CargoDeliveryMission mission;
        public Camera playerCamera;
        public CargoUiCard pickupCard;
        public CargoUiCard deliveryCard;
        public CargoUiCard cameraCard;
        public CargoScreenFeedback screenFeedback;
        public Text modeCaption;
        [SerializeField] CargoUiMode mode = CargoUiMode.FixedWorld;
        [Min(0f)] public float approachMargin = 2f;
        [Min(.2f)] public float notificationSeconds = 3f;
        public Color pickupColor = new Color(.22f, .88f, .95f);
        public Color deliveryColor = new Color(.55f, .95f, .55f);
        public CargoUiMode Mode => mode;
        public bool CameraNoticeVisible => cameraCard != null && cameraCard.gameObject.activeSelf;

        Quaternion pickupRotation, deliveryRotation;
        Canvas pickupCanvas, deliveryCanvas;
        CargoMissionState previousState;
        float noticeUntil, flashUntil;
        string noticeTitle, noticeMessage, noticeDetail;
        Color noticeColor;

        void Awake()
        {
            pickupRotation = pickupCard.transform.rotation;
            deliveryRotation = deliveryCard.transform.rotation;
            pickupCanvas = pickupCard.GetComponent<Canvas>();
            deliveryCanvas = deliveryCard.GetComponent<Canvas>();
        }

        void OnEnable()
        {
            if (mission == null) return;
            mission.pickedUp.AddListener(OnPickup);
            mission.released.AddListener(OnRelease);
            mission.completed.AddListener(OnComplete);
            mission.restarted.AddListener(OnRestart);
            previousState = mission.State;
        }

        void OnDisable()
        {
            if (mission != null)
            {
                mission.pickedUp.RemoveListener(OnPickup);
                mission.released.RemoveListener(OnRelease);
                mission.completed.RemoveListener(OnComplete);
                mission.restarted.RemoveListener(OnRestart);
            }
            if (pickupCard != null) pickupCard.gameObject.SetActive(false);
            if (deliveryCard != null) deliveryCard.gameObject.SetActive(false);
            if (cameraCard != null) cameraCard.gameObject.SetActive(false);
            if (screenFeedback != null) screenFeedback.gameObject.SetActive(false);
            noticeUntil = flashUntil = 0f;
        }

        void Update()
        {
            if (Application.isFocused && Keyboard.current != null) ReadKeyboard(Keyboard.current);
        }

        public void ReadKeyboard(Keyboard keyboard)
        {
            if (keyboard.digit1Key.wasPressedThisFrame) SetMode(CargoUiMode.FixedWorld);
            if (keyboard.digit2Key.wasPressedThisFrame) SetMode(CargoUiMode.BillboardWorld);
            if (keyboard.digit3Key.wasPressedThisFrame) SetMode(CargoUiMode.CameraNotice);
            if (keyboard.digit4Key.wasPressedThisFrame) SetMode(CargoUiMode.ScreenFeedback);
        }

        public void SetMode(CargoUiMode value)
        {
            mode = value;
            RefreshPresentation();
        }

        void LateUpdate() => RefreshPresentation();

        public void RefreshPresentation()
        {
            if (mission == null || playerCamera == null) return;
            if (mission.State != previousState && mission.State == CargoMissionState.Failed)
                Notify("ПЕРЕВОЗКА ПРЕРВАНА", "Груз потерян", "Доставка не завершена", new Color(1f, .24f, .16f));
            previousState = mission.State;
            bool world = mode == CargoUiMode.FixedWorld || mode == CargoUiMode.BillboardWorld;
            pickupCard.gameObject.SetActive(world);
            deliveryCard.gameObject.SetActive(world);
            if (world)
            {
                // Transparent world canvases need a consistent order when their screen areas overlap.
                bool pickupCloser = (pickupCard.transform.position - playerCamera.transform.position).sqrMagnitude
                    < (deliveryCard.transform.position - playerCamera.transform.position).sqrMagnitude;
                pickupCanvas.sortingOrder = pickupCloser ? 1 : 0;
                deliveryCanvas.sortingOrder = pickupCloser ? 0 : 1;
                UpdateZoneCard(pickupCard, true);
                UpdateZoneCard(deliveryCard, false);
                if (mode == CargoUiMode.BillboardWorld)
                {
                    FaceCamera(pickupCard.transform);
                    FaceCamera(deliveryCard.transform);
                }
                else
                {
                    pickupCard.transform.rotation = pickupRotation;
                    deliveryCard.transform.rotation = deliveryRotation;
                }
            }

            bool notice = Time.unscaledTime < noticeUntil;
            bool activePickup = mission.State == CargoMissionState.AwaitingPickup;
            var activeZone = activePickup ? mission.pickupZone : mission.deliveryZone;
            bool approaching = (activePickup || mission.State == CargoMissionState.Transporting)
                && IsApproaching(activeZone);
            bool showCamera = mode == CargoUiMode.CameraNotice && (approaching || notice
                || mission.State == CargoMissionState.AwaitingLanding);
            // In mode 4 only short event confirmations accompany the edge effect.
            showCamera |= mode == CargoUiMode.ScreenFeedback && notice;
            cameraCard.gameObject.SetActive(showCamera);
            if (showCamera)
            {
                if (notice) cameraCard.Show(noticeTitle, noticeMessage, noticeDetail, 1f, noticeColor);
                else UpdateZoneCard(cameraCard, activePickup);
            }

            bool feedback = mode == CargoUiMode.ScreenFeedback;
            screenFeedback.gameObject.SetActive(feedback);
            Color tint = activePickup ? pickupColor : deliveryColor;
            float alpha = mission.HoldProgress > 0f
                ? .24f + .2f * Progress + .035f * Mathf.Sin(Time.unscaledTime * 6f) : 0f;
            if (Time.unscaledTime < flashUntil)
            {
                tint = noticeColor;
                alpha = Mathf.Max(alpha, .7f * Mathf.Clamp01((flashUntil - Time.unscaledTime) / 2.4f));
            }
            tint.a = feedback ? alpha : 0f;
            screenFeedback.color = tint;
            modeCaption.text = mode == CargoUiMode.FixedWorld ? "01   /   WORLD SPACE"
                : mode == CargoUiMode.BillboardWorld ? "02   /   BILLBOARD"
                : mode == CargoUiMode.CameraNotice ? "03   /   CAMERA UI" : "04   /   SCREEN EFFECT";
        }

        float Progress => Mathf.Clamp01(mission.HoldProgress / Mathf.Max(.1f, mission.holdSeconds));

        bool IsApproaching(CargoHoverZone zone)
        {
            Vector3 local = zone.transform.InverseTransformPoint(mission.carrier.Drone.Body.position) - zone.Volume.center;
            Vector3 half = zone.Volume.size * .5f + Vector3.one * approachMargin;
            return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z;
        }

        void FaceCamera(Transform panel)
        {
            Vector3 direction = panel.position - playerCamera.transform.position;
            if (direction.sqrMagnitude > .001f) panel.rotation = Quaternion.LookRotation(direction, playerCamera.transform.up);
        }

        void UpdateZoneCard(CargoUiCard card, bool pickup)
        {
            bool active = pickup ? mission.State == CargoMissionState.AwaitingPickup : mission.State == CargoMissionState.Transporting;
            bool done = pickup ? mission.State != CargoMissionState.AwaitingPickup && mission.State != CargoMissionState.Failed
                : mission.State == CargoMissionState.Completed;
            string status = pickup ? "Подбор груза" : "Доставка груза";
            string detail = active ? $"Удерживайте позицию · {mission.holdSeconds:F1} с" : "Сначала заберите груз";
            if (active && mission.HoldProgress > 0f)
            {
                status = pickup ? "Захват груза" : "Отсоединение груза";
                detail = $"{mission.HoldProgress:F1} / {mission.holdSeconds:F1} с";
            }
            if (done) { status = pickup ? "Груз закреплён" : "Груз доставлен"; detail = pickup ? "Следующая точка · DELIVERY" : "Миссия выполнена"; }
            if (!pickup && mission.State == CargoMissionState.AwaitingLanding) { status = "Груз отпущен"; detail = "Ожидание приземления"; }
            if (mission.State == CargoMissionState.Failed) { status = "Перевозка прервана"; detail = "Доставка не завершена"; }
            card.Show(pickup ? "01  /  PICKUP" : "02  /  DELIVERY", status, detail,
                done ? 1f : active ? Progress : 0f, pickup ? pickupColor : deliveryColor);
        }

        void Notify(string title, string message, string detail, Color color)
        {
            noticeTitle = title;
            noticeMessage = message;
            noticeDetail = detail;
            noticeColor = color;
            noticeUntil = Time.unscaledTime + notificationSeconds;
            flashUntil = Time.unscaledTime + 2.4f;
        }

        void OnPickup() => Notify("01  /  PICKUP", "Груз закреплён", $"Масса груза · {mission.cargo.Mass:F1} кг", pickupColor);
        void OnRelease() => Notify("02  /  DELIVERY", "Груз отпущен", "Ожидание приземления", deliveryColor);
        void OnComplete() => Notify("МИССИЯ ВЫПОЛНЕНА", "Груз доставлен", "Приземление подтверждено", deliveryColor);
        void OnRestart()
        {
            noticeUntil = flashUntil = 0f;
            previousState = mission.State;
            RefreshPresentation();
        }
    }
}
