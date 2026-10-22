using System;
using System.Collections.Generic;
using System.IO;
using ExoFlight.Flight;
using ExoFlight.Triggers;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace ExoFlight.Editor
{
    /// <summary>Real PhysX checks in an isolated physics scene, plus keyboard event checks.</summary>
    public static class FlightDemoChecks
    {
        [MenuItem("Tools/ExoFlight/Run Flight Checks (Play Mode)")]
        public static void Run()
        {
            if (!Application.isPlaying) throw new InvalidOperationException("Enter Play Mode first.");
            var report = new List<string> { "Unity " + Application.unityVersion + " | " + DateTime.UtcNow.ToString("O") };
            var scene = SceneManager.CreateScene("Flight Physics Checks",
                new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            GameObject root = null;
            DroneFlightProfile profile = null;
            FlightEnvironment environment = null;
            Keyboard keyboard = null;
            InputSettings originalInputSettings = InputSystem.settings;
            InputSettings checkInputSettings = null;
            try
            {
                root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/PlayerDrone.prefab"));
                SceneManager.MoveGameObjectToScene(root, scene);
                var drone = root.GetComponent<DroneFlightController>();
                var input = root.GetComponent<KeyboardDroneInput>();
                root.GetComponentInChildren<Camera>().enabled = false;
                root.GetComponentInChildren<AudioListener>().enabled = false;
                profile = Object.Instantiate(drone.profile);
                environment = Object.Instantiate(drone.environment);
                drone.profile = profile;
                drone.environment = environment;
                drone.ConfigureBody();
                drone.Body.interpolation = RigidbodyInterpolation.None;
                var physics = scene.GetPhysicsScene();

                Action reset = () => {
                    drone.ResetFlight();
                    input.ResetInput();
                    drone.SetMode(DroneFlightMode.Acro);
                    drone.Body.position = new Vector3(0, 100, 0);
                    drone.Body.rotation = Quaternion.identity;
                    drone.Body.linearVelocity = Vector3.zero;
                    drone.Body.angularVelocity = Vector3.zero;
                    drone.Body.WakeUp();
                    Physics.SyncTransforms();
                    drone.SetArmed(true);
                };
                Action<float, FlightControlInput, float> advance = (seconds, controls, dt) => {
                    drone.SetInput(controls);
                    for (int j = 0; j < Mathf.RoundToInt(seconds / dt); j++)
                    {
                        drone.StepPhysics(dt);
                        physics.Simulate(dt);
                    }
                };
                Action<bool, string> check = (condition, name) => {
                    report.Add((condition ? "PASS " : "FAIL ") + name);
                    if (!condition) throw new InvalidOperationException(name);
                };

                bool quad = DroneFlightController.MotorCount == 4 && drone.rotorVisuals.Length == 4
                    && root.transform.Find("Rotor 5") == null && root.transform.Find("Rotor 6") == null
                    && root.transform.Find("Motor 5") == null && root.transform.Find("Motor 6") == null;
                for (int j = 0; j < 4; j++)
                    quad &= drone.rotorVisuals[j] != null
                        && Vector3.Distance(drone.rotorVisuals[j].localPosition,
                            profile.RotorPosition(j) + Vector3.up * .09f) < .001f;
                check(quad, "Prefab has exactly four rotor assemblies matching force application points");
                Vector3 frontRight = profile.RotorPosition(0);
                check(frontRight.x > 0 && frontRight.z > 0
                    && (profile.RotorPosition(0) + profile.RotorPosition(2)).magnitude < .001f
                    && (profile.RotorPosition(1) + profile.RotorPosition(3)).magnitude < .001f
                    && profile.RotorSpin(0) == profile.RotorSpin(2)
                    && profile.RotorSpin(1) == profile.RotorSpin(3)
                    && profile.RotorSpin(0) != profile.RotorSpin(1),
                    "X quad has opposite positions and alternating spin directions");

                reset();
                drone.SetArmed(false);
                advance(1f, default, .01f);
                check(Mathf.Abs(drone.Body.linearVelocity.y + 3.71f) < .03f,
                    "Disarmed drone gives Mars free-fall acceleration");

                reset();
                advance(2f, new FlightControlInput(drone.HoverThrottle, 0, 0, 0), .01f);
                drone.Body.linearVelocity = Vector3.zero;
                float hoverHeight = drone.Body.position.y;
                advance(3f, new FlightControlInput(drone.HoverThrottle, 0, 0, 0), .01f);
                check(Mathf.Abs(drone.Body.position.y - hoverHeight) < .03f
                    && drone.Body.angularVelocity.magnitude < .01f,
                    "Calculated hover throttle balances weight without hidden altitude assist");
                check(Mathf.Abs(drone.TotalThrust - profile.mass * 3.71f) < .02f,
                    "Four rotor forces sum to mg in level hover");

                reset();
                advance(2f, new FlightControlInput(.95f, 0, 0, 0), .01f);
                check(drone.Body.position.y > 101f && drone.Body.linearVelocity.y > 1.5f,
                    "Collective throttle above quad hover demand produces climb");
                drone.SetArmed(false);
                advance(.5f, default, .01f);
                check(drone.TotalThrust < .001f, "Disarm spins down all rotors");

                reset();
                advance(.8f, new FlightControlInput(.5f, .4f, 0, 0), .01f);
                check(drone.Body.angularVelocity.x > .1f && (drone.Body.rotation * Vector3.forward).y < -.05f,
                    "Positive pitch lowers the nose");
                check(drone.Body.linearVelocity.z > .1f, "Forward acceleration comes from tilted rotor thrust");

                reset();
                advance(.8f, new FlightControlInput(.5f, 0, 0, .4f), .01f);
                check(drone.Body.angularVelocity.z < -.1f && (drone.Body.rotation * Vector3.up).x > .05f,
                    "Positive roll banks right");
                reset();
                advance(1.5f, new FlightControlInput(.5f, 0, .4f, 0), .01f);
                check(drone.Body.angularVelocity.y > .05f, "Positive yaw turns right using rotor reaction torque");

                reset();
                drone.Body.rotation = Quaternion.Euler(25, 0, 0);
                advance(2f, new FlightControlInput(.5f, 0, 0, 0), .01f);
                check(Vector3.Angle(drone.Body.rotation * Vector3.up, Vector3.up) > 23f,
                    "Acro neutral holds tilted attitude rather than leveling");
                drone.SetMode(DroneFlightMode.Angle);
                advance(4f, new FlightControlInput(.5f, 0, 0, 0), .01f);
                check(Vector3.Angle(drone.Body.rotation * Vector3.up, Vector3.up) < 2f,
                    "Angle neutral recovers level attitude");

                reset();
                advance(.8f, new FlightControlInput(.5f, .4f, 0, 0), .01f);
                advance(3f, new FlightControlInput(.5f, 0, 0, 0), .01f);
                check(drone.Body.angularVelocity.magnitude < .08f,
                    "Acro rate controller stops rotation when sticks recenter");

                reset();
                drone.Body.linearVelocity = Vector3.forward * 5f;
                advance(.1f, default, .01f);
                check(drone.Body.linearVelocity.z > 4.9f, "Released controls retain translational momentum");

                reset();
                advance(1f, new FlightControlInput(.65f, .15f, 0, -.1f), .01f);
                Vector3 coarseVelocity = drone.Body.linearVelocity;
                Quaternion coarseRotation = drone.Body.rotation;
                reset();
                advance(1f, new FlightControlInput(.65f, .15f, 0, -.1f), .005f);
                check(Vector3.Distance(coarseVelocity, drone.Body.linearVelocity) < .15f
                    && Quaternion.Angle(coarseRotation, drone.Body.rotation) < 1.5f,
                    "100 Hz and 200 Hz physics agree within tolerances");

                // Conservation is an independent physics criterion, not just a control-sign test.
                Vector3 gravity = environment.gravity;
                float density = environment.airDensity;
                environment.gravity = Vector3.zero;
                environment.airDensity = 0f;
                reset();
                drone.SetArmed(false);
                drone.Body.angularVelocity = new Vector3(1, 2, 3);
                Vector3 initialMomentum = WorldMomentum(drone);
                float initialEnergy = RotationEnergy(drone);
                advance(1f, default, .01f);
                float momentumError = (WorldMomentum(drone) - initialMomentum).magnitude / initialMomentum.magnitude;
                float energyError = Mathf.Abs(RotationEnergy(drone) / initialEnergy - 1f);
                report.Add($"MEASURE free rotation: momentum error {momentumError:P3}, energy error {energyError:P3} over 1 s at 100 Hz");
                check(momentumError < .02f, "Torque-free asymmetric body conserves angular momentum within 2%");
                check(energyError < .02f, "Torque-free body conserves rotation energy within 2%");
                environment.gravity = gravity;
                environment.airDensity = density;

                reset();
                advance(1f, default, .01f);
                check(drone.TotalThrust > 0 && drone.TotalThrust < profile.mass * gravity.magnitude,
                    "Armed idle spins rotors without lifting a level drone");
                advance(.8f, new FlightControlInput(0, .4f, 0, 0), .01f);
                check(drone.Body.angularVelocity.x > .1f && drone.Input.Throttle == 0,
                    "Airmode retains pitch authority at zero stick throttle");
                profile.airMode = false;
                reset();
                advance(1f, new FlightControlInput(.01f, 1, 0, 0), .01f);
                float idleThrust = profile.ThrustAtRpm(profile.idleRpm, density);
                float expectedLowThrust = DroneFlightController.MotorCount * Mathf.Lerp(idleThrust, profile.ThrustAtRpm(profile.maximumRpm, density), .01f);
                check(drone.TotalThrust <= expectedLowThrust + .001f,
                    "Disabling Airmode prevents collective boost at low throttle");
                profile.airMode = true;
                reset();
                advance(.8f, new FlightControlInput(1, 1, 1, 1), .01f);
                bool bounded = drone.MotorsSaturated;
                for (int j = 0; j < DroneFlightController.MotorCount; j++)
                    bounded &= drone.GetMotorRpm(j) >= 0 && drone.GetMotorRpm(j) <= profile.maximumRpm + .01f
                        && drone.GetMotorThrust(j) <= profile.ThrustAtRpm(profile.maximumRpm, density) + .001f;
                check(bounded, "Combined full-stick demand saturates without exceeding motor limits");
                advance(5f, new FlightControlInput(.5f, 0, 0, 0), .01f);
                check(drone.Body.angularVelocity.magnitude < .12f,
                    "Rate controller recovers after sustained actuator saturation");

                reset();
                advance(1f, new FlightControlInput(.5f, 0, 0, 0), .01f);
                float normalThrust = drone.TotalThrust;
                float normalHover = drone.HoverThrottle;
                environment.airDensity = density * .5f;
                advance(1f, new FlightControlInput(.5f, 0, 0, 0), .01f);
                report.Add($"MEASURE density: thrust {normalThrust:F6} -> {drone.TotalThrust:F6}, hover {normalHover:F6} -> {drone.HoverThrottle:F6}, rate {drone.Body.angularVelocity}");
                check(Mathf.Abs(drone.TotalThrust / normalThrust - .5f) < .005f && drone.HoverThrottle > normalHover,
                    "Lower atmospheric density reduces thrust and raises hover demand");
                environment.airDensity = 0f;
                advance(1f, new FlightControlInput(1, 0, 0, 0), .01f);
                check(drone.TotalThrust == 0, "Rotors produce no aerodynamic thrust in vacuum");
                environment.airDensity = density;
                environment.gravity = Vector3.zero;
                environment.windVelocity = Vector3.right * 10f;
                reset();
                drone.SetArmed(false);
                advance(.1f, default, .01f);
                check(drone.Body.linearVelocity.x > 0, "Wind accelerates an unpowered body downwind");
                reset();
                drone.SetArmed(false);
                drone.Body.linearVelocity = environment.windVelocity;
                advance(.1f, default, .01f);
                check(Vector3.Distance(drone.Body.linearVelocity, environment.windVelocity) < .0001f,
                    "Body drag is zero when travelling with the air mass");
                environment.gravity = gravity;
                environment.windVelocity = Vector3.zero;

                var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
                SceneManager.MoveGameObjectToScene(floor, scene);
                floor.transform.position = new Vector3(0, -.5f, 0);
                floor.transform.localScale = new Vector3(20, 1, 20);
                reset();
                drone.SetArmed(false);
                drone.Body.position = Vector3.up * 1.5f;
                Physics.SyncTransforms();
                advance(3f, default, .01f);
                check(drone.Body.position.y > .1f && Mathf.Abs(drone.Body.linearVelocity.y) < .05f,
                    "Drone lands on a solid collider without falling through it");
                drone.SetArmed(true);
                advance(2f, new FlightControlInput(.9f, 0, 0, 0), .01f);
                check(drone.Body.position.y > 1f, "Armed drone takes off again after landing");
                Object.DestroyImmediate(floor);

                var keys = new KeyboardStickState();
                keys.Step(1, Vector3.one, 1, .25f, 2.5f, 6f, false);
                var retained = keys.Step(0, Vector3.zero, 1, .25f, 2.5f, 6f, false);
                check(Mathf.Abs(retained.Throttle - .25f) < .0001f
                    && retained.Pitch == 0 && retained.Roll == 0 && retained.Yaw == 0,
                    "Gas stays in place; the other three axes spring to center");
                var fast = new KeyboardStickState();
                var slow = new KeyboardStickState();
                for (int j = 0; j < 120; j++) fast.Step(1, Vector3.one, 1f / 120f, .25f, 2.5f, 6f, false);
                for (int j = 0; j < 30; j++) slow.Step(1, Vector3.one, 1f / 30f, .25f, 2.5f, 6f, false);
                check(Mathf.Abs(fast.Throttle - slow.Throttle) < .00001f && fast.Axes == slow.Axes,
                    "Virtual sticks behave identically at 30 and 120 FPS");
                keys.Reset();
                var diagonal = keys.Step(0, Vector3.one, 1, .25f, 2.5f, 6f, false);
                check(diagonal.Pitch == 1f && diagonal.Yaw == 1f && diagonal.Roll == 1f,
                    "Independent axes are not normalized on diagonal input");

                reset();
                drone.SetArmed(false);
                // A background MCP call otherwise selects an Editor input update, where
                // wasPressedThisFrame has different frame bookkeeping. Use temporary
                // in-memory settings for this synchronous test; never edit the project asset.
                checkInputSettings = Object.Instantiate(originalInputSettings);
                checkInputSettings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
                checkInputSettings.editorInputBehaviorInPlayMode =
                    InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
                InputSystem.settings = checkInputSettings;
                keyboard = InputSystem.AddDevice<Keyboard>();
                // This synthetic device is only for the isolated, synchronous checks.
                // Unity can create it disabled while its Game view is unfocused.
                InputSystem.EnableDevice(keyboard);
                Action<Key[]> press = pressed => {
                    InputSystem.QueueStateEvent(keyboard, new KeyboardState(pressed));
                    InputSystem.Update();
                    input.ReadKeyboard(keyboard, .1f);
                };
                press(new[] { Key.Space });
                check(drone.Armed, "Space arms at zero throttle");
                press(new[] { Key.W, Key.D, Key.UpArrow, Key.RightArrow });
                check(drone.Input.Throttle > 0 && drone.Input.Pitch > 0 && drone.Input.Yaw > 0 && drone.Input.Roll > 0,
                    "Actual W/D/Up/Right keyboard events map to the four channels");
                float throttle = drone.Input.Throttle;
                press(Array.Empty<Key>());
                check(Mathf.Approximately(drone.Input.Throttle, throttle) && drone.Input.Pitch == 0,
                    "Actual key release retains gas and centers pitch");
                // Arming protection must apply to the controller too, not just the keyboard adapter.
                drone.SetArmed(false);
                drone.SetInput(new FlightControlInput(.5f, 0, 0, 0));
                drone.SetArmed(true);
                check(!drone.Armed, "Arming is rejected with a raised throttle");
                drone.SetInput(default);
                drone.SetArmed(true);
                press(new[] { Key.F });
                check(drone.mode == DroneFlightMode.Angle, "F switches flight mode");
                press(Array.Empty<Key>());
                press(new[] { Key.A, Key.DownArrow, Key.LeftArrow });
                check(drone.Input.Pitch < 0 && drone.Input.Yaw < 0 && drone.Input.Roll < 0,
                    "Opposite keys reverse pitch/yaw/roll");
                press(Array.Empty<Key>());
                root.SendMessage("OnApplicationFocus", false);
                check(!drone.Armed && input.Throttle == 0 && drone.Input.Throttle == 0,
                    "Losing focus disarms and clears retained keyboard input");

                for (int j = 0; j < 12; j++) press(new[] { Key.W });
                press(Array.Empty<Key>());
                press(new[] { Key.Space });
                check(drone.Armed && input.Throttle == 0 && drone.Input.Throttle == 0,
                    "Space arms immediately after raised keyboard throttle and clears old gas");
                press(new[] { Key.Space });
                check(drone.Armed, "Holding Space does not repeatedly toggle motors");
                press(Array.Empty<Key>());
                press(new[] { Key.Space });
                bool repeatedToggle = !drone.Armed;
                for (int j = 0; j < 10; j++)
                {
                    for (int k = 0; k < 3; k++) press(new[] { Key.W });
                    press(new[] { Key.Space, Key.W });
                    repeatedToggle &= drone.Armed && input.Throttle <= .026f;
                    press(Array.Empty<Key>());
                    press(new[] { Key.Space });
                    repeatedToggle &= !drone.Armed && input.Throttle == 0;
                }
                check(repeatedToggle, "Ten Space on/off cycles work even when W was held before arming");

                var checkpoint = root.GetComponent<PlayerCheckpoint>();
                root.transform.SetPositionAndRotation(new Vector3(3, 7, 9), Quaternion.Euler(0, 30, 0));
                checkpoint.Save(root.transform);
                drone.Body.position = new Vector3(0, 100, 0);
                drone.Body.linearVelocity = Vector3.one * 4f;
                drone.Body.angularVelocity = Vector3.one;
                press(Array.Empty<Key>());
                press(new[] { Key.R });
                check(Vector3.Distance(drone.Body.position, checkpoint.Position) < .001f
                    && drone.Body.linearVelocity == Vector3.zero && drone.Body.angularVelocity == Vector3.zero
                    && drone.TotalThrust == 0 && !drone.Armed && input.Throttle == 0,
                    "R restores checkpoint and clears velocity, motor state and sticks");
                var sensor = root.GetComponentInChildren<PlayerTriggerSensor>();
                check(sensor.Player == checkpoint && sensor.GetComponent<CapsuleCollider>().isTrigger
                    && sensor.GetComponent<Rigidbody>() == null
                    && sensor.GetComponent<CapsuleCollider>().attachedRigidbody == drone.Body,
                    "Trigger capsule uses the drone body without a competing child Rigidbody");
                report.Add("ALL PASSED");
            }
            catch (Exception exception)
            {
                report.Add("FAILED: " + exception);
                Debug.LogError("[Flight checks] " + exception);
            }
            finally
            {
                if (keyboard != null) InputSystem.RemoveDevice(keyboard);
                if (checkInputSettings != null)
                {
                    InputSystem.settings = originalInputSettings;
                    Object.DestroyImmediate(checkInputSettings);
                }
                if (root != null) Object.DestroyImmediate(root);
                if (profile != null) Object.DestroyImmediate(profile);
                if (environment != null) Object.DestroyImmediate(environment);
                SceneManager.UnloadSceneAsync(scene);
                File.WriteAllLines("Temp/flight-checks.txt", report);
                Debug.Log("[Flight checks] " + string.Join("\n", report));
            }
        }

        static Vector3 WorldMomentum(DroneFlightController drone)
        {
            Vector3 omega = Quaternion.Inverse(drone.Body.rotation) * drone.Body.angularVelocity;
            return drone.Body.rotation * (Vector3.Scale(drone.Body.inertiaTensor, omega) + drone.RotorAngularMomentum);
        }

        static float RotationEnergy(DroneFlightController drone)
        {
            Vector3 omega = Quaternion.Inverse(drone.Body.rotation) * drone.Body.angularVelocity;
            return .5f * Vector3.Dot(omega, Vector3.Scale(drone.Body.inertiaTensor, omega));
        }
    }
}
