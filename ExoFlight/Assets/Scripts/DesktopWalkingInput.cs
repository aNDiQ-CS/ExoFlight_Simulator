using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement;

namespace ExoFlight
{
    /// <summary>Keyboard/mouse fallback for the existing XR locomotion rig.</summary>
    [DefaultExecutionOrder(-220)] // Before XRI locomotion providers (-210).
    [RequireComponent(typeof(XROrigin), typeof(CharacterController))]
    public sealed class DesktopWalkingInput : MonoBehaviour, IXRInputValueReader<Vector2>
    {
        [SerializeField, Min(0.5f)] float eyeHeight = 1.7f;
        [SerializeField, Range(0.01f, 1f)] float mouseSensitivity = 0.12f;

        readonly List<XRDisplaySubsystem> displays = new List<XRDisplaySubsystem>();
        XROrigin origin;
        CharacterController character;
        ContinuousMoveProvider move;
        TrackedPoseDriver headTracking;
        IXRInputValueReader<Vector2> previousInput;
        Vector3 savedHeadPosition;
        Quaternion savedHeadRotation;
        Vector3 spawnPosition;
        bool savedTrackingEnabled;
        bool desktopMode;
        float pitch;

        void Awake()
        {
            origin = GetComponent<XROrigin>();
            character = GetComponent<CharacterController>();
            move = GetComponentInChildren<ContinuousMoveProvider>(true);
            if (origin.Camera == null || move == null)
            {
                Debug.LogError("Desktop walking requires an XR camera and a Continuous Move Provider.", this);
                enabled = false;
                return;
            }

            headTracking = origin.Camera.GetComponent<TrackedPoseDriver>();
            spawnPosition = transform.position;
        }

        void Update()
        {
            SubsystemManager.GetSubsystems(displays);
            bool xrRunning = displays.Exists(display => display.running);
            SetDesktopMode(!xrRunning);

            // Recover if the player leaves the test platform, in either input mode.
            if (transform.position.y < spawnPosition.y - 20f)
            {
                bool wasEnabled = character.enabled;
                character.enabled = false;
                transform.position = spawnPosition;
                character.enabled = wasEnabled;
            }

            if (!desktopMode)
                return;

            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            bool lockedThisFrame = false;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
                ReleaseCursor();
            else if (Application.isFocused && mouse != null && mouse.leftButton.wasPressedThisFrame)
            {
                lockedThisFrame = Cursor.lockState != CursorLockMode.Locked;
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }

            if (!lockedThisFrame && Application.isFocused && Cursor.lockState == CursorLockMode.Locked && mouse != null)
            {
                // Mouse delta already measures displacement this frame; no deltaTime multiplier.
                Vector2 delta = mouse.delta.ReadValue() * mouseSensitivity;
                transform.Rotate(0f, delta.x, 0f, Space.Self);
                pitch = Mathf.Clamp(pitch - delta.y, -85f, 85f);
            }

            // XROrigin may update its camera offset while waiting for a device.
            // Set the desktop eye pose in origin space so its height stays stable.
            origin.Camera.transform.SetPositionAndRotation(
                transform.TransformPoint(Vector3.up * eyeHeight),
                transform.rotation * Quaternion.Euler(pitch, 0f, 0f));
        }

        void SetDesktopMode(bool enabledForDesktop)
        {
            if (desktopMode == enabledForDesktop)
                return;

            desktopMode = enabledForDesktop;
            if (desktopMode)
            {
                savedHeadPosition = origin.Camera.transform.localPosition;
                savedHeadRotation = origin.Camera.transform.localRotation;
                savedTrackingEnabled = headTracking != null && headTracking.enabled;
                if (headTracking != null)
                    headTracking.enabled = false;
                previousInput = move.leftHandMoveInput.bypass;
                move.leftHandMoveInput.bypass = this;
                pitch = 0f;
            }
            else
            {
                if (ReferenceEquals(move.leftHandMoveInput.bypass, this))
                    move.leftHandMoveInput.bypass = previousInput;
                origin.Camera.transform.SetLocalPositionAndRotation(savedHeadPosition, savedHeadRotation);
                if (headTracking != null)
                    headTracking.enabled = savedTrackingEnabled;
                ReleaseCursor();
            }
        }

        public Vector2 ReadValue()
        {
            var keyboard = Keyboard.current;
            if (!desktopMode || !Application.isFocused || Cursor.lockState != CursorLockMode.Locked || keyboard == null)
                return Vector2.zero;

            float x = (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed ? 1f : 0f)
                - (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed ? 1f : 0f);
            float y = (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed ? 1f : 0f)
                - (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed ? 1f : 0f);
            return Vector2.ClampMagnitude(new Vector2(x, y), 1f);
        }

        public bool TryReadValue(out Vector2 value)
        {
            value = ReadValue();
            return desktopMode;
        }

        void OnApplicationFocus(bool focused)
        {
            if (!focused && desktopMode)
                ReleaseCursor();
        }

        void OnDisable()
        {
            if (desktopMode)
                SetDesktopMode(false);
        }

        static void ReleaseCursor()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

    }
}
