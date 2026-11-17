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
        static string savedReader;
        static float since;
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
            if (InputSystem.devices.Any(d => d is Gamepad || d is Joystick || d is XRHMD))
                throw new InvalidOperationException("Disconnect physical RC/HMD devices for isolated virtual-device tests.");
            pilot = Object.FindFirstObjectByType<DroneMissionAutopilot>();
            drone = pilot.mission.drone;
            pilot.Stop(true);
            drone.GetComponent<ExoFlight.Triggers.PlayerCheckpoint>().Respawn();
            reader = drone.GetComponent<DroneJoystickInput>();
            router = drone.GetComponent<DroneInputRouter>();
            vr = drone.GetComponent<DroneVrView>();
            savedReader = EditorJsonUtility.ToJson(reader);
            var so = new SerializedObject(reader);
            so.FindProperty("throttlePath").stringValue = "leftStick/y";
            so.FindProperty("yawPath").stringValue = "leftStick/x";
            so.FindProperty("pitchPath").stringValue = "rightStick/y";
            so.FindProperty("rollPath").stringValue = "rightStick/x";
            so.FindProperty("deviceNameFilter").stringValue = "";
            var aux = so.FindProperty("auxPaths"); aux.arraySize = 3;
            aux.GetArrayElementAtIndex(0).stringValue = "rightTrigger";
            aux.GetArrayElementAtIndex(1).stringValue = "leftTrigger";
            aux.GetArrayElementAtIndex(2).stringValue = "buttonSouth";
            so.ApplyModifiedPropertiesWithoutUndo();
            reader.mappingVerified = true;
            reader.throttleRange = new Vector2(-1f, 1f);
            gamepad = InputSystem.AddDevice<Gamepad>("PresentationTestRC");
            router.UseAutomaticSource();
            SetPad(new Vector2(0f, -1f), Vector2.zero, 0f);
            lines.Clear(); phase = 0; since = Time.unscaledTime;
            Running = true; Result = "Running";
            pump = drone.gameObject.AddComponent<MarsInputCheckPump>();
        }
        static void Assert(bool value, string text)
        {
            if (!value) throw new Exception(text);
            lines.Add("PASS " + text);
        }
        static void SetPad(Vector2 left, Vector2 right, float arm)
        {
            using (StateEvent.From(gamepad, out var evt))
            {
                gamepad.leftStick.WriteValueIntoEvent(left, evt);
                gamepad.rightStick.WriteValueIntoEvent(right, evt);
                gamepad.rightTrigger.WriteValueIntoEvent(arm, evt);
                InputSystem.QueueEvent(evt);
            }
            InputSystem.Update();
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
                    case 0:
                        Assert(reader.IsReady && router.Current == DroneInputRouter.Source.Joystick, "Verified transmitter selected");
                        Assert(!drone.GetComponent<KeyboardDroneInput>().enabled && drone.GetComponent<JoystickDroneInput>().enabled, "Only RC writes manual flight input");
                        Assert(reader.Throttle < .001f, "Signed raw throttle -1 maps to zero (actual " + reader.Throttle
                            + ", stick " + gamepad.leftStick.ReadValue() + ")");
                        SetPad(new Vector2(0f, 1f), Vector2.zero, 1f); break;
                    case 1:
                        Assert(reader.Throttle > .999f && !drone.Armed, "Throttle +1 maps to full; high-throttle arming rejected");
                        SetPad(new Vector2(0f, -1f), Vector2.zero, 0f); break;
                    case 2:
                        SetPad(new Vector2(0f, -1f), Vector2.zero, 1f); break;
                    case 3:
                        Assert(drone.Armed, "Low-then-high AUX arms at zero throttle");
                        SetPad(Vector2.zero, new Vector2(.5f, -.5f), 1f); break;
                    case 4:
                        Assert(Mathf.Abs(reader.Throttle - .5f) < .01f, "Raw throttle midpoint maps to 50 percent");
                        Assert(drone.Input.Roll > .2f && drone.Input.Pitch < -.2f, "Both control sticks reach the flight controller");
                        InputSystem.RemoveDevice(gamepad); gamepad = null; break;
                    case 5:
                        Assert(reader.Device == null && reader.Throttle == 0f && reader.Roll == 0f && reader.GetAux(0) == 0f, "Disconnect clears retained axes and AUX");
                        Assert(!drone.Armed && router.Current == DroneInputRouter.Source.Keyboard, "Disconnect disarms and returns to keyboard");
                        pilot.HoldPosition(); break;
                    case 6:
                        Assert(router.Current == DroneInputRouter.Source.Autopilot && !drone.GetComponent<KeyboardDroneInput>().enabled
                            && !drone.GetComponent<JoystickDroneInput>().enabled && drone.Armed, "Autopilot exclusively owns the motors");
                        pilot.Stop(true);
                        Assert(!drone.Armed && router.Current == DroneInputRouter.Source.Keyboard, "Emergency stop disarms and releases input ownership");
                        hmd = InputSystem.AddDevice<XRHMD>("PresentationTestHMD");
                        using (StateEvent.From(hmd, out var evt))
                        {
                            hmd.isTracked.WriteValueIntoEvent(1f, evt);
                            hmd.trackingState.WriteValueIntoEvent(3, evt);
                            hmd.centerEyePosition.WriteValueIntoEvent(headPosition, evt);
                            hmd.centerEyeRotation.WriteValueIntoEvent(headRotation, evt);
                            InputSystem.QueueEvent(evt);
                        }
                        InputSystem.Update();
                        break;
                    case 7:
                        Assert(vr.TrackedActive && vr.poseDriver.enabled, "Tracked HMD activates the pose driver");
                        Assert(Vector3.Distance(vr.poseDriver.transform.localPosition, headPosition) < .001f, "HMD head translation is preserved");
                        Assert(Quaternion.Angle(vr.poseDriver.transform.localRotation, headRotation) < .1f, "HMD head rotation is preserved");
                        drone.GetComponent<DroneCameraView>().SetThirdPerson(true);
                        Assert(!drone.GetComponent<DroneCameraView>().ThirdPerson, "Desktop third-person mode cannot overwrite VR view");
                        drone.Body.rotation = Quaternion.Euler(15f, 40f, -18f);
                        break;
                    case 8:
                        Assert(Vector3.Angle(vr.viewOrigin.up, Vector3.up) < .01f, "VR origin keeps a level horizon during body tilt");
                        Assert(Quaternion.Angle(vr.poseDriver.transform.localRotation, headRotation) < .1f, "Body rotation does not erase the tracked head pose");
                        InputSystem.RemoveDevice(hmd); hmd = null;
                        break;
                    case 9:
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
            gamepad = null; hmd = null;
            if (reader != null) EditorJsonUtility.FromJsonOverwrite(savedReader, reader);
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
