using System;
using System.Collections.Generic;
using System.IO;
using ExoFlight.Flight;
using ExoFlight.Missions.Mars;
using ExoFlight.Triggers;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Object = UnityEngine.Object;

namespace ExoFlight.EditorTools
{
    /// <summary>Real keyboard events -> Update -> motor physics -> camera. No direct ReadKeyboard calls.</summary>
    public static class MarsManualControlChecks
    {
        static DroneFlightController drone;
        static KeyboardDroneInput input;
        static DroneMissionAutopilot pilot;
        static DroneVrView vr;
        static DroneCameraView camera;
        static Keyboard keys;
        static MarsManualCheckPump pump;
        static Vector3 home;
        static float since, began, savedThrottle;
        static int phase;
        static bool oldThird;
        static DroneFlightMode oldMode;
        static readonly List<string> report = new List<string>();
        public static bool Running { get; private set; }
        public static string Result { get; private set; }
        [MenuItem("Tools/ExoFlight/Test Mars Manual Controls (Play Mode)")]
        public static void Run()
        {
            if (!Application.isPlaying || Running) throw new InvalidOperationException("Enter Play Mode first.");
            pilot = Object.FindFirstObjectByType<DroneMissionAutopilot>();
            drone = pilot.mission.drone; input = drone.GetComponent<KeyboardDroneInput>();
            vr = drone.GetComponent<DroneVrView>(); camera = drone.GetComponent<DroneCameraView>();
            oldMode = drone.mode; oldThird = camera.ThirdPerson;
            pilot.Stop(true); drone.GetComponent<DroneInputRouter>().UseAutomaticSource();
            if (!input.enabled) throw new InvalidOperationException("Disconnect the transmitter for this keyboard test.");
            camera.SetThirdPerson(false);
            drone.GetComponent<PlayerCheckpoint>().Respawn();
            home = drone.transform.position + Vector3.up * 40f;
            drone.SetMode(DroneFlightMode.Angle);
            keys = InputSystem.AddDevice<Keyboard>("Mars manual input test");
            report.Clear(); phase = 0; since = began = Time.unscaledTime;
            Running = true; Result = "Running";
            InputSystem.onBeforeUpdate += Feed;
            pump = drone.gameObject.AddComponent<MarsManualCheckPump>();
            EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView")).Focus();
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
        static void Feed()
        {
            if (!Running || keys == null || InputState.currentUpdateType != InputUpdateType.Dynamic) return;
            Key[] pressed;
            switch (phase)
            {
                case 0: case 16: pressed = new[] { Key.Space }; break;
                case 1: pressed = new[] { Key.W }; break;
                case 3: pressed = new[] { Key.UpArrow }; break;
                case 5: pressed = new[] { Key.DownArrow }; break;
                case 7: pressed = new[] { Key.LeftArrow }; break;
                case 9: case 15: pressed = new[] { Key.RightArrow }; break;
                case 11: pressed = new[] { Key.D }; break;
                case 12: pressed = new[] { Key.A }; break;
                case 17: pressed = new[] { Key.R }; break;
                default: pressed = Array.Empty<Key>(); break;
            }
            InputSystem.QueueStateEvent(keys, new KeyboardState(pressed));
        }
        static void Check(bool condition, string text)
        {
            report.Add((condition ? "PASS " : "FAIL ") + text);
            if (!condition) throw new Exception(text);
        }
        static void Next() { phase++; since = Time.unscaledTime; }
        static void HoverFixture()
        {
            // Isolate each directional response in empty air; subsequent movement uses normal FixedUpdate.
            drone.ResetFlight(); input.ResetInput();
            drone.Body.position = home; drone.Body.rotation = Quaternion.identity;
            drone.Body.linearVelocity = drone.Body.angularVelocity = Vector3.zero;
            drone.SetMode(DroneFlightMode.Angle); drone.SetArmed(true);
            input.ResumeManualControl(drone.HoverThrottle);
            Physics.SyncTransforms();
        }
        public static void Tick()
        {
            if (!Running) return;
            try
            {
                if (!Application.isFocused) throw new Exception("Game view is not focused; actual Update input is correctly blocked.");
                float elapsed = Time.unscaledTime - since;
                if (Time.unscaledTime - began > 40f) throw new Exception("Timeout in phase " + phase);
                if (phase == 0 && elapsed > .2f)
                { Check(drone.Armed, "Space arms through the real keyboard Update"); Next(); }
                else if (phase == 1 && input.Throttle >= drone.HoverThrottle + .12f)
                { savedThrottle = input.Throttle; Check(true, "W increases collective throttle"); Next(); }
                else if (phase == 2 && elapsed > .5f)
                { Check(Mathf.Abs(input.Throttle - savedThrottle) < .01f, "Throttle stays where the stick left it"); HoverFixture(); Next(); }
                else if ((phase == 3 || phase == 5 || phase == 7 || phase == 9) && elapsed > .8f)
                {
                    bool pitch = phase <= 5;
                    float sign = phase == 3 || phase == 9 ? 1f : -1f;
                    float command = pitch ? drone.Input.Pitch : drone.Input.Roll;
                    float response = -(drone.Body.rotation * (pitch ? Vector3.forward : Vector3.right)).y;
                    Check(command * sign > .8f && response * sign > .1f, "Arrow changes the actual aircraft attitude: phase " + phase);
                    Check(Quaternion.Angle(vr.viewOrigin.rotation, drone.transform.rotation) < .1f,
                        "Manual FPV visibly follows body pitch/roll: phase " + phase);
                    Next();
                }
                else if ((phase == 4 || phase == 6 || phase == 8 || phase == 10) && elapsed > 2f)
                {
                    Check(Mathf.Abs(drone.Input.Pitch) + Mathf.Abs(drone.Input.Roll) < .001f, "Released arrow returns its stick to center: phase " + phase);
                    Check(Vector3.Angle(drone.transform.up, Vector3.up) < 3f, "Angle mode levels the body: phase " + phase);
                    HoverFixture(); Next();
                }
                else if ((phase == 11 || phase == 12) && elapsed > .65f)
                {
                    float sign = phase == 11 ? 1f : -1f;
                    Check(drone.Input.Yaw * sign > .8f && drone.Body.angularVelocity.y * sign > .15f,
                        "A/D turns aircraft heading: phase " + phase);
                    HoverFixture(); Next();
                    if (phase == 13) drone.Body.linearVelocity = Vector3.forward * 3f;
                }
                else if (phase == 13 && elapsed > .2f)
                {
                    Check(drone.Body.linearVelocity.z > 2.9f, "Neutral sticks do not erase translational inertia");
                    HoverFixture(); pilot.HoldPosition(); Next();
                }
                else if (phase == 14 && elapsed > .4f)
                {
                    Check(pilot.Active && !input.enabled && vr.HorizonStabilized, "Position hold owns input and stabilizes the desktop camera");
                    savedThrottle = drone.Input.Throttle; Next();
                }
                else if (phase == 15 && elapsed > .7f)
                {
                    Check(!pilot.Active && input.enabled && drone.Input.Roll > .8f, "Arrow automatically takes over from autopilot");
                    Check(drone.Armed && Mathf.Abs(input.Throttle - savedThrottle) < .06f, "Takeover keeps motors armed and retains collective");
                    Check((drone.Body.rotation * Vector3.right).y < -.1f && !vr.HorizonStabilized, "Manual aircraft and FPV camera respond after takeover");
                    pilot.HoldPosition(); Next();
                }
                else if (phase == 16 && elapsed > .3f)
                { Check(!pilot.Active && !drone.Armed && input.enabled, "Space stops autopilot without rearming in the same frame"); Next(); }
                else if (phase == 17 && elapsed > .2f)
                {
                    Check(!drone.Armed && input.Throttle == 0f
                        && Vector3.Distance(drone.Body.position, drone.GetComponent<PlayerCheckpoint>().Position) < .3f,
                        "R resets the player and clears retained commands");
                    CheckNavigation(); End("PASS");
                }
            }
            catch (Exception ex) { End("FAIL: " + ex.Message); }
        }
        static void CheckNavigation()
        {
            var guide = pilot.mission.GetComponent<MarsRouteGuidance>();
            Check(guide != null && guide.markers.Length == 3, "Three spatial route markers are installed");
            Check(guide.markers[0].root.GetComponentsInChildren<Collider>(true).Length == 0, "Visible checkpoint has no collision or gameplay trigger");
            drone.Body.position = pilot.outbound[2].position; guide.Refresh();
            Check(guide.CurrentIndex > 2 && guide.VisibleCount == guide.previewCount, "Passing a navigation point highlights the next one");
            for (int i = 3; i < pilot.outbound.Length; i++) { drone.Body.position = pilot.outbound[i].position; guide.Refresh(); }
            Check(pilot.mission.NextZoneIndex == 0 && Vector3.Distance(guide.CurrentTarget, pilot.mission.zones[0].transform.position) < 3f,
                "Navigation waits at an unaccepted story gate; it cannot complete triggers itself");
        }
        static void End(string status)
        {
            Running = false;
            InputSystem.onBeforeUpdate -= Feed;
            if (keys != null) InputSystem.RemoveDevice(keys);
            keys = null;
            if (pump != null) Object.Destroy(pump);
            pilot.Stop(true); drone.SetMode(oldMode); camera.SetThirdPerson(oldThird);
            drone.GetComponent<PlayerCheckpoint>().Respawn();
            Result = status + "\nReal keyboard events, Update/FixedUpdate, physical body and FPV camera. Per-axis fixtures reset pose only before each case.\n"
                + string.Join("\n", report);
            File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "../../docs/mars-manual-navigation-verification-2026-11-17.txt")), Result);
            Debug.Log("[Mars manual checks] " + status);
        }
    }
    [DefaultExecutionOrder(600)]
    public sealed class MarsManualCheckPump : MonoBehaviour
    {
        void LateUpdate() => MarsManualControlChecks.Tick();
    }
}
