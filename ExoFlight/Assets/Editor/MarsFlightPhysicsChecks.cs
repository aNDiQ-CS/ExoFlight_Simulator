using System;
using System.Collections.Generic;
using System.IO;
using ExoFlight.Cargo;
using ExoFlight.Flight;
using ExoFlight.Missions.Mars;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace ExoFlight.EditorTools
{
    public static class MarsFlightPhysicsChecks
    {
        public static string Result { get; private set; }
        [MenuItem("Tools/ExoFlight/Test Mars Flight Physics (Play Mode)")]
        public static void Run()
        {
            if (!Application.isPlaying) throw new InvalidOperationException("Enter Play Mode first.");
            var original = Object.FindFirstObjectByType<MarsMissionDirector>();
            var scene = SceneManager.CreateScene("Isolated Mars flight checks", new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            var report = new List<string>();
            var profile = Object.Instantiate(original.drone.profile);
            var air = Object.Instantiate(original.drone.environment);
            air.windProfile = null; air.windVelocity = Vector3.zero;
            GameObject root = null, loadObject = null;
            string result = "PASS";
            try
            {
                root = new GameObject("Physics test quad");
                SceneManager.MoveGameObjectToScene(root, scene);
                root.AddComponent<BoxCollider>().size = new Vector3(3.62f, .6f, 3.62f);
                var d = root.AddComponent<DroneFlightController>();
                d.profile = profile; d.environment = air; d.windAtRotors = true; d.ConfigureBody();
                d.Body.interpolation = RigidbodyInterpolation.None;
                var carrier = root.AddComponent<DroneCargoCarrier>();
                carrier.mountPoint = original.cargoMission.carrier.mountPoint;
                TransportableCargo cargo = null;
                var physics = scene.GetPhysicsScene();
                Action<bool, string> check = (ok, text) => { report.Add((ok ? "PASS " : "FAIL ") + text); if (!ok) throw new Exception(text); };
                Action reset = () =>
                {
                    d.ResetFlight(); d.SetMode(DroneFlightMode.Angle);
                    d.Body.position = new Vector3(0f, 100f, 0f); d.Body.rotation = Quaternion.identity;
                    d.Body.linearVelocity = d.Body.angularVelocity = Vector3.zero;
                    if (cargo != null)
                    {
                        cargo.Body.position = carrier.DockedPosition(cargo); cargo.Body.rotation = Quaternion.identity;
                        cargo.Body.linearVelocity = cargo.Body.angularVelocity = Vector3.zero;
                        carrier.RefreshPayload();
                    }
                    Physics.SyncTransforms(); d.SetArmed(true);
                };
                float peak = 0f;
                Action<float, FlightControlInput> advance = (seconds, command) =>
                {
                    d.SetInput(command); peak = 0f;
                    for (int i = 0; i < Mathf.RoundToInt(seconds / .01f); i++)
                    {
                        carrier.RefreshPayload();
                        if (cargo != null) cargo.StepPhysics(.01f);
                        d.StepPhysics(.01f); physics.Simulate(.01f);
                        peak = Mathf.Max(peak, Vector3.Angle(d.Body.rotation * Vector3.up, Vector3.up));
                    }
                };
                reset(); d.SetArmed(false); advance(1f, default);
                check(Mathf.Abs(d.Body.linearVelocity.y + 3.71f) < .03f, "Free fall matches Mars gravity 3.71 m/s2");
                reset();
                advance(2f, new FlightControlInput(d.HoverThrottle, 0f, 0f, 0f));
                d.Body.linearVelocity = Vector3.zero;
                float height = d.Body.position.y;
                advance(3f, new FlightControlInput(d.HoverThrottle, 0f, 0f, 0f));
                check(Mathf.Abs(d.Body.position.y - height) < .03f, "Unloaded computed hover balances weight without extra forces");
                check(Mathf.Abs(d.TotalThrust - 8f * 3.71f) < .03f, "Four rotors produce 29.68 N in unloaded hover");
                foreach (var command in new[] { new Vector2(.5f, 0f), new Vector2(-.5f, 0f), new Vector2(0f, .5f), new Vector2(0f, -.5f) })
                {
                    reset(); advance(2.5f, new FlightControlInput(d.HoverThrottle, command.x, 0f, command.y));
                    Vector3 forward = d.Body.rotation * Vector3.forward, right = d.Body.rotation * Vector3.right;
                    float measured = command.x != 0f ? -Mathf.Asin(forward.y) * Mathf.Rad2Deg : -Mathf.Asin(right.y) * Mathf.Rad2Deg;
                    float expected = (command.x != 0f ? command.x : command.y) * profile.maximumTilt;
                    report.Add($"MEASURE command {command}: angle {measured:F2}, peak {peak:F2} deg");
                    check(Mathf.Abs(measured - expected) < 3f, "Pitch/roll follows commanded angle: " + command);
                    advance(3f, new FlightControlInput(d.HoverThrottle, 0f, 0f, 0f));
                    check(Vector3.Angle(d.Body.rotation * Vector3.up, Vector3.up) < 2f, "Angle mode levels after stick release: " + command);
                }
                reset(); d.SetMode(DroneFlightMode.Acro);
                advance(2f, new FlightControlInput(d.HoverThrottle, 0f, .4f, 0f));
                check(d.Body.angularVelocity.y > .2f, "Positive yaw turns right via differential motor torque");
                reset(); advance(1f, new FlightControlInput(d.HoverThrottle, 0f, 0f, 0f));
                d.Body.linearVelocity = Vector3.forward * 5f;
                advance(.2f, new FlightControlInput(d.HoverThrottle, 0f, 0f, 0f));
                check(d.Body.linearVelocity.z > 4.9f, "Level attitude retains momentum in thin air; no hidden velocity braking");

                reset(); float bareHover = d.HoverThrottle;
                loadObject = new GameObject("Physics test rover");
                SceneManager.MoveGameObjectToScene(loadObject, scene);
                loadObject.transform.position = d.Body.position + Vector3.down * 1.5f;
                loadObject.AddComponent<BoxCollider>().size = original.cargoMission.cargo.GetComponent<BoxCollider>().size;
                cargo = loadObject.AddComponent<TransportableCargo>();
                cargo.environment = air; cargo.massKg = original.cargoMission.cargo.Mass;
                cargo.liftingPoint = original.cargoMission.cargo.liftingPoint;
                cargo.dragArea = original.cargoMission.cargo.dragArea;
                cargo.ConfigureBody();
                check(carrier.TryAttach(cargo), "Rover attached with the real rigid joint");
                check(Mathf.Abs(d.FlightMass - 18.6f) < .01f && d.Body.mass == 8f, "Payload remains a separate body; mass is not counted twice");
                check(d.HoverThrottle > bareHover + .3f, "Cargo increases required collective throttle");
                advance(2f, new FlightControlInput(d.HoverThrottle, 0f, 0f, 0f));
                d.Body.linearVelocity = cargo.Body.linearVelocity = Vector3.zero;
                height = d.Body.position.y;
                advance(3f, new FlightControlInput(d.HoverThrottle, 0f, 0f, 0f));
                report.Add($"MEASURE hover empty {bareHover:F4}, loaded {d.HoverThrottle:F4}, loaded thrust {d.TotalThrust:F3} N");
                check(Mathf.Abs(d.Body.position.y - height) < .05f && Mathf.Abs(d.TotalThrust - 18.6f * 3.71f) < .05f,
                    "Loaded hover balances combined weight with motor thrust");
                reset(); advance(4f, new FlightControlInput(d.HoverThrottle, .5f, 0f, 0f));
                float loadedAngle = -Mathf.Asin((d.Body.rotation * Vector3.forward).y) * Mathf.Rad2Deg;
                report.Add($"MEASURE loaded pitch {loadedAngle:F2}, peak {peak:F2} deg for 20 deg demand");
                check(Mathf.Abs(loadedAngle - 20f) < 3f, "Loaded aircraft reaches commanded pitch");
                advance(4f, new FlightControlInput(d.HoverThrottle, 0f, 0f, 0f));
                check(Vector3.Angle(d.Body.rotation * Vector3.up, Vector3.up) < 2f
                    && d.Body.angularVelocity.magnitude < .15f, "Loaded aircraft settles after releasing the stick");
            }
            catch (Exception ex) { result = "FAIL: " + ex.Message; }
            finally
            {
                if (loadObject != null) Object.DestroyImmediate(loadObject);
                if (root != null) Object.DestroyImmediate(root);
                Object.DestroyImmediate(profile); Object.DestroyImmediate(air);
                SceneManager.UnloadSceneAsync(scene);
                Result = result + "\n" + string.Join("\n", report);
                File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "../../docs/mars-physics-verification-2026-11-17.txt")), Result);
            }
            Debug.Log("[Mars physics checks] " + result);
        }
    }
}
