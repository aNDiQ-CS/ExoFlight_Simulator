using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ExoFlight.Flight;
using ExoFlight.Missions.Mars;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.XR;
using TouchController = UnityEngine.XR.OpenXR.Features.Interactions.OculusTouchControllerProfile.OculusTouchController;
using ExoFlight.Triggers;
using Object = UnityEngine.Object;

namespace ExoFlight.EditorTools
{
    /// <summary>Virtual input devices exercise the real reader, routing and tracked-pose components.</summary>
    public static class MarsInputChecks
    {
        static DroneFlightController drone;
        static DroneJoystickInput reader;
        static DroneInputRouter router;
        static DroneMissionAutopilot pilot;
        static DroneVrView vr;
        static Gamepad gamepad;
        static XRHMD hmd;
        static MarsInputCheckPump pump;
        static TouchController left, right;
        static PlayerCheckpoint checkpoint;
        static int restarts;
        static float since;
        static float takeoffTime, takeoffHeight;
        static int phase;
        static readonly List<string> lines = new List<string>();
        public static bool Running { get; private set; }
        public static string Result { get; private set; } = "Not run";
        static readonly Vector3 headPosition = new Vector3(.12f, .2f, -.08f);
        static readonly Quaternion headRotation = Quaternion.Euler(7f, 32f, -4f);
        [MenuItem("Tools/ExoFlight/Test Mars Input and VR (Play Mode)")]
        public static void Run()
        {
            if (!Application.isPlaying || Running) throw new InvalidOperationException("Enter Play Mode first.");
            if (InputSystem.devices.Any(d => d is Gamepad || d is Joystick || d is XRHMD || d is XRController))
                throw new InvalidOperationException("Disconnect physical RC/HMD devices for isolated virtual-device tests.");
            EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView")).Focus();
            pilot = Object.FindFirstObjectByType<DroneMissionAutopilot>();
            drone = pilot.mission.drone;
            reader = drone.GetComponent<DroneJoystickInput>();
            router = drone.GetComponent<DroneInputRouter>();
            vr = drone.GetComponent<DroneVrView>();
            checkpoint = drone.GetComponent<PlayerCheckpoint>();
            checkpoint.Respawned += CountRestart;
            restarts = 0;
            lines.Clear(); phase = -1; since = Time.unscaledTime;
            Running = true; Result = "Running";
            pump = drone.gameObject.AddComponent<MarsInputCheckPump>();
        }
        static void CountRestart() => restarts++;
        static void Assert(bool value, string text)
        {
            if (!value) throw new Exception(text);
            lines.Add("PASS " + text);
        }
        static void SetPad(Vector2 left, Vector2 right)
        {
            using (StateEvent.From(gamepad, out var evt))
            {
                gamepad.leftStick.WriteValueIntoEvent(left, evt);
                gamepad.rightStick.WriteValueIntoEvent(right, evt);
                InputSystem.QueueEvent(evt);
            }
        }
        static void SetButtons(TouchController hand, bool primary = false, bool secondary = false, bool stick = false)
        {
            using (StateEvent.From(hand, out var evt))
            {
                hand.primaryButton.WriteValueIntoEvent(primary ? 1f : 0f, evt);
                hand.secondaryButton.WriteValueIntoEvent(secondary ? 1f : 0f, evt);
                hand.thumbstickClicked.WriteValueIntoEvent(stick ? 1f : 0f, evt);
                InputSystem.QueueEvent(evt);
            }
        }
        public static void Tick()
        {
            if (!Running) return;
            if (!Application.isPlaying) { End("Interrupted"); return; }
            if (Time.unscaledTime - since < .3f) return;
            since = Time.unscaledTime;
            try
            {
                switch (phase++)
                {
                    case -1:
                        // Let Game View finish its focus reset before connecting virtual hardware.
                        gamepad = InputSystem.AddDevice<Gamepad>("PresentationTestRC");
                        left = InputSystem.AddDevice<TouchController>();
                        right = InputSystem.AddDevice<TouchController>();
                        InputSystem.SetDeviceUsage(left, UnityEngine.InputSystem.CommonUsages.LeftHand);
                        InputSystem.SetDeviceUsage(right, UnityEngine.InputSystem.CommonUsages.RightHand);
                        SetPad(Vector2.zero, Vector2.right);
                        break;
                    case 0:
                        Assert(!pilot.autoStart && !pilot.Active && drone.mode == DroneFlightMode.Acro && restarts == 0, "Fresh mission starts in manual Acro");
                        Assert(reader.IsReady && router.Current == DroneInputRouter.Source.Joystick, "Verified transmitter selected");
                        Assert(!drone.GetComponent<KeyboardDroneInput>().enabled && drone.GetComponent<JoystickDroneInput>().enabled, "Only RC writes manual flight input");
                        Assert(reader.Throttle > .999f && !drone.Armed, $"Connecting at full throttle does not arm (throttle {reader.Throttle:F2}, armed {drone.Armed})");
                        SetPad(Vector2.zero, Vector2.left);
                        SetButtons(left, primary: true, secondary: true, stick: true);
                        SetButtons(right, stick: true);
                        break;
                    case 1:
                        Assert(reader.Throttle < .001f && drone.Armed, "Low throttle arms using the scene mapping without AUX");
                        Assert(!pilot.Active && restarts == 0, "Thumbstick clicks and left-hand X/Y leave manual flight alone");
                        takeoffHeight = drone.Body.position.y;
                        takeoffTime = Time.time;
                        SetPad(Vector2.zero, new Vector2(.65f, 0f));
                        break;
                    case 2:
                        if (Time.time - takeoffTime < 1f) { phase--; break; }
                        Assert(drone.Body.position.y > takeoffHeight + 1f && !pilot.Active && drone.mode == DroneFlightMode.Acro,
                            $"Manual RC takeoff in Acro rises {drone.Body.position.y - takeoffHeight:F2} m");
                        SetPad(new Vector2(.45f, -.5f), new Vector2(0f, .5f)); break;
                    case 3:
                        Assert(Mathf.Abs(drone.Input.Throttle - .5f) < .01f && drone.Input.Yaw > .2f &&
                            drone.Input.Pitch > .2f && drone.Input.Roll > .2f, "TX12 scene mapping sends all four axes to manual Acro");
                        SetButtons(right, secondary: true); break;
                    case 4:
                        Assert(restarts == 1 && !pilot.Active && drone.mode == DroneFlightMode.Acro &&
                            router.Current == DroneInputRouter.Source.Joystick, "Right B restarts manual flight once in Acro");
                        Assert(Vector3.Distance(drone.Body.position, checkpoint.Position) < 1f && pilot.mission.NextZoneIndex == 0,
                            "B returns aircraft and mission to the launch point");
                        SetButtons(right); break;
                    case 5:
                        SetButtons(right, primary: true); break;
                    case 6:
                        Assert(pilot.Active && !pilot.Holding && restarts == 2 && router.Current == DroneInputRouter.Source.Autopilot,
                            "Right A explicitly starts the full mission once");
                        SetButtons(right, secondary: true);
                        SetPad(Vector2.zero, Vector2.left); break;
                    case 7:
                        Assert(restarts == 3 && !pilot.Active && drone.mode == DroneFlightMode.Acro && drone.Armed &&
                            router.Current == DroneInputRouter.Source.Joystick, "B during autopilot restores manual Acro and the transmitter");
                        SetPad(new Vector2(-.45f, .5f), new Vector2(.65f, -.5f));
                        hmd = InputSystem.AddDevice<XRHMD>("PresentationTestHMD");
                        using (StateEvent.From(hmd, out var evt))
                        {
                            hmd.isTracked.WriteValueIntoEvent(1f, evt);
                            hmd.trackingState.WriteValueIntoEvent(3, evt);
                            hmd.centerEyePosition.WriteValueIntoEvent(headPosition, evt);
                            hmd.centerEyeRotation.WriteValueIntoEvent(headRotation, evt);
                            InputSystem.QueueEvent(evt);
                        }
                        break;
                    case 8:
                        Assert(drone.Input.Throttle > .55f && drone.Input.Yaw < -.2f && drone.Input.Pitch < -.2f &&
                            drone.Input.Roll < -.2f && restarts == 3, $"RC after restart: throttle {drone.Input.Throttle:F2}, yaw {drone.Input.Yaw:F2}, pitch {drone.Input.Pitch:F2}, roll {drone.Input.Roll:F2}, restarts {restarts}");
                        Assert(vr.TrackedActive && vr.poseDriver.enabled, "Tracked HMD activates the pose driver");
                        Assert(Vector3.Distance(vr.poseDriver.transform.localPosition, headPosition) < .001f, "HMD head translation is preserved");
                        Assert(Quaternion.Angle(vr.poseDriver.transform.localRotation, headRotation) < .1f, "HMD head rotation is preserved");
                        drone.GetComponent<DroneCameraView>().SetThirdPerson(true);
                        Assert(!drone.GetComponent<DroneCameraView>().ThirdPerson, "Desktop third-person mode cannot overwrite VR view");
                        drone.Body.rotation = Quaternion.Euler(15f, 40f, -18f);
                        break;
                    case 9:
                        var eye = vr.poseDriver.transform;
                        Assert(Vector3.Distance(vr.viewOrigin.position, eye.position) < .005f, "Head movement leaves the camera at the FPV seat");
                        var tilt = Quaternion.Euler(-drone.GetComponent<KeyboardDroneInput>().cameraTilt, 0f, 0f);
                        Assert(Quaternion.Angle(vr.viewOrigin.rotation * tilt, eye.rotation) < .1f, "Head rotation leaves the view aligned to the drone camera");
                        InputSystem.RemoveDevice(hmd); hmd = null;
                        InputSystem.RemoveDevice(gamepad); gamepad = null;
                        break;
                    case 10:
                        Assert(reader.Device == null && reader.Throttle == 0f && !drone.Armed && router.Current == DroneInputRouter.Source.Keyboard,
                            "Disconnect clears commands, disarms and releases the transmitter");
                        Assert(!vr.TrackedActive && !vr.poseDriver.enabled, "HMD removal restores desktop camera ownership");
                        Assert(Vector3.Distance(vr.poseDriver.transform.localPosition, Vector3.zero) < .001f, "Desktop first-person camera returns to its seat");
                        End("PASS"); break;
                }
            }
            catch (Exception ex) { End("FAIL: " + ex.Message); }
        }
        static void End(string status)
        {
            Running = false;
            if (pump != null) Object.Destroy(pump);
            if (gamepad != null) InputSystem.RemoveDevice(gamepad);
            if (hmd != null) InputSystem.RemoveDevice(hmd);
            if (left != null) InputSystem.RemoveDevice(left);
            if (right != null) InputSystem.RemoveDevice(right);
            checkpoint.Respawned -= CountRestart;
            gamepad = null; hmd = null;
            if (pilot != null) pilot.Stop(true);
            if (router != null) router.UseAutomaticSource();
            if (drone != null) drone.GetComponent<ExoFlight.Triggers.PlayerCheckpoint>().Respawn();
            Result = status + "\nVirtual devices only; real TX12 calibration and HMD comfort not tested.\n" + string.Join("\n", lines);
            File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "../../docs/mars-input-vr-verification-" + DateTime.Now.ToString("yyyy-MM-dd") + ".txt")), Result);
            Debug.Log("[Mars input checks] " + status);
        }
    }
    [DefaultExecutionOrder(500)]
    public sealed class MarsInputCheckPump : MonoBehaviour
    {
        void LateUpdate() => MarsInputChecks.Tick();
    }
}
