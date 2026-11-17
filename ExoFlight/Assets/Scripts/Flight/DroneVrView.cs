using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;

namespace ExoFlight.Flight
{
    /// <summary>A gimballed seat follows the aircraft; only the HMD owns the camera's local pose.</summary>
    [DefaultExecutionOrder(150), DisallowMultipleComponent]
    public sealed class DroneVrView : MonoBehaviour
    {
        public Transform viewOrigin;
        public TrackedPoseDriver poseDriver;
        public Vector3 seatOffset = new Vector3(0f, .46f, 2.3f);
        DroneInputRouter router;
        float levelBlend;
        public bool HorizonStabilized => TrackedActive || (router != null && router.Current == DroneInputRouter.Source.Autopilot);
        public bool TrackedActive => UnityEngine.XR.XRSettings.isDeviceActive || HasTrackedHmd();
        static bool HasTrackedHmd()
        {
            foreach (var d in InputSystem.devices)
                if (d is XRHMD hmd && hmd.enabled && hmd.isTracked.isPressed) return true;
            return false;
        }
        void Awake()
        {
            router = GetComponent<DroneInputRouter>();
            levelBlend = HorizonStabilized ? 1f : 0f;
            if (poseDriver == null) return;
            // Embedded actions are enabled/disabled with the pose driver, including a late HMD connection.
            poseDriver.positionInput = new InputActionProperty(new InputAction("Head position", InputActionType.Value,
                "<XRHMD>/centerEyePosition", expectedControlType: "Vector3"));
            poseDriver.rotationInput = new InputActionProperty(new InputAction("Head rotation", InputActionType.Value,
                "<XRHMD>/centerEyeRotation", expectedControlType: "Quaternion"));
            poseDriver.trackingStateInput = new InputActionProperty(new InputAction("Head tracking", InputActionType.Value,
                "<XRHMD>/trackingState", expectedControlType: "Integer"));
            poseDriver.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;
            poseDriver.updateType = TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;
            poseDriver.enabled = TrackedActive;
        }
        void Update()
        {
            if (poseDriver != null && poseDriver.enabled != TrackedActive) poseDriver.enabled = TrackedActive;
        }
        void LateUpdate()
        {
            if (viewOrigin == null) return;
            Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            if (forward.sqrMagnitude < .001f) forward = Vector3.forward;
            Quaternion yaw = Quaternion.LookRotation(forward.normalized, Vector3.up);
            // Manual desktop FPV must expose pitch/roll. Keep the comfort gimbal in VR
            // and autopilot only; blend when handing the aircraft back to the pilot.
            levelBlend = TrackedActive ? 1f : Mathf.MoveTowards(levelBlend, HorizonStabilized ? 1f : 0f,
                Time.unscaledDeltaTime / .2f);
            Quaternion mount = Quaternion.Slerp(transform.rotation, yaw, levelBlend);
            viewOrigin.SetPositionAndRotation(transform.position + mount * seatOffset, mount);
        }
        void OnDisable() { if (poseDriver != null) poseDriver.enabled = false; }
    }
}
