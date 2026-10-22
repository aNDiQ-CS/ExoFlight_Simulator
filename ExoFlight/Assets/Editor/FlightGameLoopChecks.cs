using System;
using System.Collections.Generic;
using System.IO;
using ExoFlight.Flight;
using ExoFlight.Triggers;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace ExoFlight.Editor
{
    /// <summary>Feeds Input System events across real frames; never calls ReadKeyboard or StepPhysics.</summary>
    public static class FlightGameLoopChecks
    {
        static DroneFlightController drone;
        static KeyboardDroneInput input;
        static Keyboard keyboard;
        static readonly List<string> report = new List<string>();
        static int stage;
        static double started, stageStarted;
        static float climbStart, retainedThrottle;
        static DroneFlightMode originalMode;

        [MenuItem("Tools/ExoFlight/Run Game Loop Checks (Play Mode)")]
        public static void Run()
        {
            if (!Application.isPlaying || keyboard != null)
                throw new InvalidOperationException("Enter Play Mode; only one run may be active.");
            drone = UnityEngine.Object.FindAnyObjectByType<DroneFlightController>();
            if (drone == null || !drone.enabled) throw new InvalidOperationException("No active flight drone.");
            input = drone.GetComponent<KeyboardDroneInput>();
            if (!input.enabled) throw new InvalidOperationException("Keyboard input is disabled.");
            report.Clear();
            report.Add("Real Update/FixedUpdate | Unity " + Application.unityVersion + " | " + DateTime.UtcNow.ToString("O"));
            originalMode = drone.mode;
            drone.ResetFlight();
            input.ResetInput();
            drone.SetMode(DroneFlightMode.Acro);
            // Start on the practice ground. Starting in free fall would first require
            // cancelling the accumulated descent velocity before height can increase.
            drone.Body.position = new Vector3(-30, .4f, -30);
            drone.Body.rotation = Quaternion.identity;
            drone.Body.linearVelocity = Vector3.zero;
            drone.Body.angularVelocity = Vector3.zero;
            Physics.SyncTransforms();
            EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView")).Focus();
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            keyboard = InputSystem.AddDevice<Keyboard>();
            stage = 0;
            started = stageStarted = EditorApplication.timeSinceStartup;
            InputSystem.onBeforeUpdate += Feed;
            EditorApplication.update += Tick;
            AssemblyReloadEvents.beforeAssemblyReload += Cancel;
        }

        static void Feed()
        {
            if (keyboard == null || InputState.currentUpdateType != InputUpdateType.Dynamic) return;
            Key[] keys;
            switch (stage)
            {
                case 0: case 7: keys = new[] { Key.Space }; break;
                case 1: case 8: keys = new[] { Key.W }; break;
                case 3: keys = new[] { Key.UpArrow }; break;
                case 5: keys = new[] { Key.F }; break;
                case 6: keys = new[] { Key.R }; break;
                default: keys = Array.Empty<Key>(); break;
            }
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
        }

        static void Tick()
        {
            try
            {
                if (!Application.isPlaying || drone == null) throw new Exception("Play Mode stopped.");
                if (!Application.isFocused) throw new Exception("Game view lost focus; repeat with Game focused.");
                double now = EditorApplication.timeSinceStartup;
                double elapsed = now - stageStarted;
                if (now - started > 25) throw new Exception("Timed out at stage " + stage);
                switch (stage)
                {
                    case 0:
                        if (drone.Armed) Pass("Space arms through the real Update callback");
                        break;
                    case 1:
                        if (input.Throttle >= Mathf.Lerp(drone.HoverThrottle, 1f, .5f))
                        {
                            climbStart = drone.Body.position.y;
                            retainedThrottle = input.Throttle;
                            Pass("Holding W raises throttle through Input System events");
                        }
                        break;
                    case 2:
                        if (elapsed > 1)
                        {
                            Require(drone.Body.position.y > climbStart + .1f, "Regular FixedUpdate/PhysX produces climb");
                            Require(Mathf.Abs(input.Throttle - retainedThrottle) < .04f, "Key release retains throttle");
                            Pass("Flight runs through the scene game loop");
                        }
                        break;
                    case 3:
                        if (elapsed > .4)
                        {
                            Require(drone.Input.Pitch > .1f && (drone.Body.rotation * Vector3.forward).y < -.01f,
                                "Arrow key pitches the actual scene drone");
                            Pass("Arrow input reaches motor physics");
                        }
                        break;
                    case 4:
                        if (elapsed > .6)
                        {
                            Require(drone.Input.Pitch == 0, "Pitch stick returns to center after release");
                            Pass("Released arrow centers through Update");
                        }
                        break;
                    case 5:
                        if (drone.mode == DroneFlightMode.Angle) Pass("F changes mode through Update");
                        break;
                    case 6:
                        if (!drone.Armed && input.Throttle == 0)
                        {
                            Require(Vector3.Distance(drone.Body.position, drone.GetComponent<PlayerCheckpoint>().Position) < .15f,
                                "R restores the scene checkpoint");
                            Pass("R resets the actual player");
                        }
                        break;
                    case 7:
                        if (drone.Armed) Pass("Drone can rearm after reset");
                        break;
                    case 8:
                        if (input.Throttle > .05f)
                        {
                            Cursor.lockState = CursorLockMode.None;
                            Pass("Cursor released with nonzero throttle");
                        }
                        break;
                    case 9:
                        if (elapsed > .2)
                        {
                            Require(!drone.Armed && input.Throttle == 0, "Unlocking cursor clears controls and disarms");
                            report.Add("ALL PASSED");
                            Finish();
                        }
                        break;
                }
            }
            catch (Exception exception)
            {
                report.Add("FAILED: " + exception.Message);
                Finish();
            }
        }

        static void Require(bool condition, string name)
        {
            if (!condition) throw new Exception(name);
            report.Add("PASS " + name);
        }

        static void Pass(string name)
        {
            report.Add("PASS " + name);
            stage++;
            stageStarted = EditorApplication.timeSinceStartup;
        }

        static void Cancel()
        {
            report.Add("CANCELLED by script reload");
            Finish();
        }

        static void Finish()
        {
            InputSystem.onBeforeUpdate -= Feed;
            EditorApplication.update -= Tick;
            AssemblyReloadEvents.beforeAssemblyReload -= Cancel;
            if (keyboard != null) InputSystem.RemoveDevice(keyboard);
            keyboard = null;
            if (drone != null)
            {
                input.ResetInput();
                drone.ResetFlight();
                drone.SetMode(originalMode);
                drone.GetComponent<PlayerCheckpoint>().Respawn();
            }
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            File.WriteAllLines("Temp/flight-game-loop-checks.txt", report);
            Debug.Log("[Flight game loop checks] " + string.Join("\n", report));
        }
    }
}
