using System;
using System.Collections.Generic;
using System.IO;
using ExoFlight.Cargo;
using ExoFlight.Flight;
using ExoFlight.Triggers;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace ExoFlight.Editor
{
    /// <summary>Starts with a synthetic P press, then observes the scene's ordinary Update/FixedUpdate.</summary>
    [InitializeOnLoad]
    public static class CargoAssistChecks
    {
        static CargoMissionAssist assist;
        static CargoDeliveryMission mission;
        static DroneFlightController drone;
        static KeyboardDroneInput input;
        static readonly List<string> report = new List<string>();
        static double started;
        static CargoMissionState lastState;
        static bool originalInputEnabled;
        static Vector3 cameraPosition;
        static DroneFlightMode originalMode;
        static DroneCameraView cameraView;
        static int phase;
        static float holdStarted;

        static CargoAssistChecks() => EditorApplication.playModeStateChanged += state => {
            if (state == PlayModeStateChange.ExitingPlayMode && assist != null) Finish("Play Mode stopped before completion.");
        };

        [MenuItem("Tools/ExoFlight/Run Assisted Cargo Demo Check (Play Mode)")]
        public static void Run()
        {
            if (!Application.isPlaying || assist != null) throw new InvalidOperationException("Enter Play Mode; only one check can run.");
            assist = UnityEngine.Object.FindAnyObjectByType<CargoMissionAssist>();
            if (assist == null) throw new InvalidOperationException("Install the assist in CargoMissionTest.");
            mission = assist.mission;
            drone = assist.GetComponent<DroneFlightController>();
            input = assist.GetComponent<KeyboardDroneInput>();
            cameraView = assist.GetComponent<DroneCameraView>();
            phase = 0;
            originalInputEnabled = input.enabled;
            originalMode = drone.mode;
            cameraPosition = input.fpvCamera.transform.localPosition;
            // The automated scene run needs no OS focus. Restore the live keyboard adapter at the end.
            // Disabling it happens before arming; the assist and all physical/mission components stay enabled.
            input.enabled = false;
            report.Clear();
            report.Add("Real scene Update/FixedUpdate | Unity " + Application.unityVersion + " | " + DateTime.UtcNow.ToString("O"));
            var originalSettings = InputSystem.settings;
            var settings = UnityEngine.Object.Instantiate(originalSettings);
            Keyboard keyboard = null;
            try
            {
                settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
                settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
                InputSystem.settings = settings;
                keyboard = InputSystem.AddDevice<Keyboard>();
                InputSystem.EnableDevice(keyboard);
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.V));
                InputSystem.Update();
                cameraView.ReadKeyboard(keyboard);
                Require(cameraView.ThirdPerson && !assist.Active,
                    "V enables third-person view independently of any flight assist");
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.V));
                InputSystem.Update();
                cameraView.ReadKeyboard(keyboard);
                Require(cameraView.ThirdPerson, "Holding V does not repeatedly switch cameras");
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                InputSystem.Update();
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.V));
                InputSystem.Update();
                cameraView.ReadKeyboard(keyboard);
                Require(!cameraView.ThirdPerson && Vector3.Distance(input.fpvCamera.transform.localPosition, cameraPosition) < .001f,
                    "A second V restores the FPV camera during manual play");
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.H));
                InputSystem.Update();
                assist.ReadKeyboard(keyboard);
                Require(assist.HoldingPosition && input.ExternalControlActive, "H enables position hold without starting a mission route");
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Space));
                InputSystem.Update();
                input.ReadKeyboard(keyboard, .01f);
                assist.StepAssist(.01f);
                Require(!assist.Active && !drone.Armed && !input.ExternalControlActive,
                    "Space immediately disarms and cancels position hold");
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.P));
                InputSystem.Update();
                assist.ReadKeyboard(keyboard);
                Require(assist.Active && drone.Armed && input.ExternalControlActive, "P starts the demo and arms without Space or W");
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.P));
                InputSystem.Update();
                assist.ReadKeyboard(keyboard);
                Require(assist.Active, "Holding P does not toggle the assist repeatedly");
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W, Key.UpArrow, Key.F));
                InputSystem.Update();
                input.ReadKeyboard(keyboard, .1f);
                Require(drone.Input.Pitch == 0f && drone.mode == DroneFlightMode.Angle,
                    "Manual sticks cannot fight the active test pilot");
            }
            catch (Exception error) { Finish(error.Message); return; }
            finally
            {
                if (keyboard != null) InputSystem.RemoveDevice(keyboard);
                InputSystem.settings = originalSettings;
                UnityEngine.Object.DestroyImmediate(settings);
            }
            lastState = mission.State;
            started = EditorApplication.timeSinceStartup;
            EditorApplication.update += Observe;
            Debug.Log("[Assist checks] Started actual scene flight; waiting for pickup, flight and delivery.");
        }

        static void Observe()
        {
            try
            {
                if (!Application.isPlaying || assist == null) throw new Exception("Scene stopped.");
                if (!assist.Active) throw new Exception("Assist stopped before delivery.");
                if (mission.State == CargoMissionState.Failed) throw new Exception("Mission failed during assisted flight.");
                if (EditorApplication.timeSinceStartup - started > 90) throw new Exception("Timed out at " + assist.Stage);
                if (phase == 1)
                {
                    if (Time.fixedTime - holdStarted < 5f) return;
                    Require(assist.HoldingPosition && Vector3.Distance(drone.Body.position, assist.Target) < .2f
                        && drone.Body.linearVelocity.magnitude < .2f,
                        "H brakes the moving drone and holds position in the regular physics loop");
                    Require(mission.State == CargoMissionState.Transporting && mission.carrier.CurrentCargo == mission.cargo,
                        "Manual hover two metres from the cube completes pickup in the enlarged area");
                    Require(Vector3.Distance(mission.cargo.WorldLiftingPoint, mission.carrier.WorldMount) < .02f
                        && mission.carrier.GetComponentsInChildren<LineRenderer>().Length == 0,
                        "Large cube stays directly docked under the drone with no line or rope");
                    assist.StopAssist();
                    Require(cameraView.ThirdPerson, "Leaving H preserves the camera mode chosen by the player");
                    cameraView.SetThirdPerson(false);
                    assist.GetComponent<PlayerCheckpoint>().Respawn();
                    report.Add("ALL PASSED");
                    Finish(null);
                    return;
                }
                if (mission.State != lastState)
                {
                    lastState = mission.State;
                    if (lastState == CargoMissionState.Transporting)
                        Require(mission.carrier.CurrentCargo == mission.cargo && drone.PayloadMass > 0f,
                            "Real dwell and trigger callbacks attach the cube during assisted flight");
                    if (lastState == CargoMissionState.AwaitingLanding)
                        Require(mission.carrier.CurrentCargo == null && drone.PayloadMass == 0f,
                            "Real delivery dwell releases the physical cube");
                }
                if (mission.State != CargoMissionState.Completed) return;
                Require(mission.cargo.IsDelivered && mission.cargo.HasGroundContact,
                    "Actual autonomous scene flight finishes with the cube landed on the delivery pad");
                Require(!drone.Body.isKinematic && !mission.cargo.Body.isKinematic,
                    "Drone and cargo remain dynamic throughout the demonstration");
                Require(Vector3.Distance(input.fpvCamera.transform.localPosition, cameraPosition) > 1f,
                    "Assist camera shows the drone and load from outside");
                report.Add($"MEASURE full demonstration duration: {EditorApplication.timeSinceStartup - started:F1} s");
                assist.StopAssist();
                Require(!input.ExternalControlActive && drone.mode == originalMode
                    && Mathf.Abs(input.Throttle - drone.Input.Throttle) < .001f
                    && Vector3.Distance(input.fpvCamera.transform.localPosition, cameraPosition) < .001f,
                    "Leaving assist restores manual mode, retained throttle and FPV camera");
                assist.BeginDemo();
                Require(assist.Active && mission.State == CargoMissionState.AwaitingPickup && !mission.cargo.IsDelivered,
                    "Starting another demonstration resets the mission and cargo");
                assist.GetComponent<PlayerCheckpoint>().Respawn();
                Require(!assist.Active && !input.ExternalControlActive && !drone.Armed,
                    "Checkpoint reset cancels the assist and returns manual control");
                // Arrange a novice approach inside the enlarged area, then let normal physics run.
                // The target is deliberately two metres away from the cube's centre.
                drone.Body.position = mission.pickupZone.transform.TransformPoint(mission.pickupZone.Volume.center)
                    + Vector3.right * 2f;
                drone.Body.rotation = Quaternion.Euler(8f, 25f, 8f);
                drone.Body.linearVelocity = new Vector3(.8f, -.1f, .2f);
                Physics.SyncTransforms();
                assist.BeginHold();
                cameraView.SetThirdPerson(true);
                phase = 1;
                holdStarted = Time.fixedTime;
            }
            catch (Exception error) { Finish(error.Message); }
        }

        static void Require(bool condition, string name)
        {
            report.Add((condition ? "PASS " : "FAIL ") + name);
            if (!condition) throw new InvalidOperationException(name);
        }

        static void Finish(string error)
        {
            EditorApplication.update -= Observe;
            if (error != null) report.Add("FAILED: " + error);
            if (assist != null) assist.StopAssist(true);
            if (input != null) input.enabled = originalInputEnabled;
            File.WriteAllLines("Temp/cargo-assist-checks.txt", report);
            Debug.Log("[Assist checks] " + string.Join("\n", report));
            assist = null;
        }
    }
}
