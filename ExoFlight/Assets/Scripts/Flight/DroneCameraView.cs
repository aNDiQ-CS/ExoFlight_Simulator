using UnityEngine;
using UnityEngine.InputSystem;

namespace ExoFlight.Flight
{
    /// <summary>Camera mode independent of flight assists. V works during manual flight too.</summary>
    [DefaultExecutionOrder(200)]
    [DisallowMultipleComponent, RequireComponent(typeof(KeyboardDroneInput))]
    public sealed class DroneCameraView : MonoBehaviour
    {
        public Vector3 thirdPersonOffset = new Vector3(0f, 2.8f, -5.8f);
        [Range(40f, 100f)] public float thirdPersonFov = 65f;
        [SerializeField] bool thirdPerson;
        public bool ThirdPerson => thirdPerson;
        KeyboardDroneInput input;
        Camera view;
        Vector3 fpvPosition;

        void Awake()
        {
            input = GetComponent<KeyboardDroneInput>();
            view = input.fpvCamera;
            if (view != null) fpvPosition = view.transform.localPosition;
        }

        void OnEnable() { if (input != null) input.CameraControlledExternally = true; }
        void Update()
        {
            if (Application.isFocused && Keyboard.current != null) ReadKeyboard(Keyboard.current);
        }
        public void ReadKeyboard(Keyboard keyboard)
        {
            if (keyboard.vKey.wasPressedThisFrame) SetThirdPerson(!thirdPerson);
        }
        public void SetThirdPerson(bool value)
        {
            thirdPerson = value;
            ApplyView();
        }
        void LateUpdate() => ApplyView();

        void ApplyView()
        {
            if (view == null) return;
            if (!thirdPerson)
            {
                view.transform.localPosition = fpvPosition;
                view.transform.localRotation = Quaternion.Euler(-input.cameraTilt, 0f, 0f);
                view.fieldOfView = input.fieldOfView;
                return;
            }
            // Keep the horizon level; follow heading without inheriting pitch and roll.
            Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            if (forward.sqrMagnitude < .001f) forward = Vector3.forward;
            Quaternion heading = Quaternion.LookRotation(forward.normalized, Vector3.up);
            Vector3 focus = transform.position + Vector3.down * .3f;
            view.transform.position = transform.position + heading * thirdPersonOffset;
            view.transform.rotation = Quaternion.LookRotation(focus - view.transform.position, Vector3.up);
            view.fieldOfView = thirdPersonFov;
        }

        void OnDisable()
        {
            if (input != null) input.CameraControlledExternally = false;
            if (view != null)
            {
                view.transform.localPosition = fpvPosition;
                view.transform.localRotation = Quaternion.Euler(-input.cameraTilt, 0f, 0f);
                view.fieldOfView = input.fieldOfView;
            }
        }
    }
}
