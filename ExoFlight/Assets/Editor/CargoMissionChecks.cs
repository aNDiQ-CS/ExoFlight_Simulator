using System;
using System.Collections.Generic;
using System.IO;
using ExoFlight.Cargo;
using ExoFlight.Flight;
using ExoFlight.Triggers;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace ExoFlight.Editor
{
    public static class CargoMissionChecks
    {
        [MenuItem("Tools/ExoFlight/Run Cargo Mission Checks (Play Mode)")]
        public static void Run()
        {
            if (!Application.isPlaying) throw new InvalidOperationException("Enter Play Mode first.");
            var report = new List<string> { "Unity " + Application.unityVersion + " | " + DateTime.UtcNow.ToString("O") };
            var scene = SceneManager.CreateScene("Cargo physics checks", new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            var physics = scene.GetPhysicsScene();
            var owned = new List<GameObject>();
            DroneFlightProfile profile = null;
            FlightEnvironment environment = null;
            try
            {
                var droneObject = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/PlayerDrone.prefab"));
                owned.Add(droneObject);
                SceneManager.MoveGameObjectToScene(droneObject, scene);
                droneObject.GetComponent<KeyboardDroneInput>().enabled = false;
                droneObject.GetComponentInChildren<Camera>().enabled = false;
                droneObject.GetComponentInChildren<AudioListener>().enabled = false;
                var drone = droneObject.GetComponent<DroneFlightController>();
                profile = Object.Instantiate(drone.profile);
                environment = Object.Instantiate(drone.environment);
                environment.windProfile = null;
                environment.windVelocity = Vector3.zero;
                drone.profile = profile;
                drone.environment = environment;
                drone.ConfigureBody();
                drone.Body.interpolation = RigidbodyInterpolation.None;
                var carrier = droneObject.AddComponent<DroneCargoCarrier>();
                var cargoObject = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(CargoDemoBuilder.PrefabPath));
                owned.Add(cargoObject);
                SceneManager.MoveGameObjectToScene(cargoObject, scene);
                var cargo = cargoObject.GetComponent<WalkerCargo>();
                cargo.environment = environment;
                cargo.ConfigureBody();
                cargo.Body.interpolation = RigidbodyInterpolation.None;
                float halfHeight = cargo.GetComponent<BoxCollider>().size.y * .5f;
                Vector3 cargoHome = Vector3.up * (halfHeight + .05f);
                float testHoverY = cargoHome.y + halfHeight + .78f;
                cargo.Body.position = cargoHome;
                cargo.Body.rotation = Quaternion.identity;
                Physics.SyncTransforms();
                cargo.RememberHome();
                var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
                owned.Add(ground);
                SceneManager.MoveGameObjectToScene(ground, scene);
                ground.transform.position = new Vector3(0f, -.25f, 0f);
                ground.transform.localScale = new Vector3(100f, .5f, 100f);
                var pickup = MakeZone("Pickup", Vector3.zero, scene, owned);
                var delivery = MakeZone("Delivery", Vector3.right * 8f, scene, owned);
                var missionObject = new GameObject("Mission checks");
                owned.Add(missionObject);
                SceneManager.MoveGameObjectToScene(missionObject, scene);
                var mission = missionObject.AddComponent<CargoDeliveryMission>();
                mission.carrier = carrier;
                mission.cargo = cargo;
                mission.pickupZone = pickup;
                mission.deliveryZone = delivery;
                mission.SendMessage("Start");
                int pickups = 0, releases = 0, completions = 0;
                mission.pickedUp.AddListener(() => pickups++);
                mission.released.AddListener(() => releases++);
                mission.completed.AddListener(() => completions++);
                Action<bool, string> check = (condition, name) => {
                    report.Add((condition ? "PASS " : "FAIL ") + name);
                    if (!condition) throw new InvalidOperationException(name);
                };
                Action<Vector3> moveDrone = position => {
                    drone.Body.position = position;
                    drone.Body.rotation = Quaternion.identity;
                    drone.Body.linearVelocity = drone.Body.angularVelocity = Vector3.zero;
                    drone.ResetFlight();
                    drone.SetArmed(true);
                    Physics.SyncTransforms();
                    physics.Simulate(.01f);
                };
                Action<float> hold = seconds => {
                    for (int i = 0; i < Mathf.CeilToInt(seconds / .01f); i++)
                    {
                        physics.Simulate(.01f);
                        carrier.RefreshPayload();
                        mission.Tick(.01f);
                    }
                };
                Action<float, float> fly = (seconds, throttle) => {
                    drone.SetInput(new FlightControlInput(throttle, 0f, 0f, 0f));
                    for (int i = 0; i < Mathf.RoundToInt(seconds / .01f); i++)
                    {
                        carrier.RefreshPayload();
                        drone.StepPhysics(.01f);
                        cargo.StepPhysics(.01f);
                        physics.Simulate(.01f);
                    }
                };
                Action resetFlying = () => {
                    drone.ResetFlight();
                    drone.Body.position = new Vector3(0f, 100f, 0f);
                    drone.Body.rotation = Quaternion.identity;
                    drone.Body.linearVelocity = drone.Body.angularVelocity = Vector3.zero;
                    cargo.Body.position = carrier.DockedPosition(cargo);
                    cargo.Body.rotation = Quaternion.identity;
                    cargo.Body.linearVelocity = cargo.Body.angularVelocity = Vector3.zero;
                    Physics.SyncTransforms();
                    drone.SetArmed(true);
                    carrier.RefreshPayload();
                };

                moveDrone(new Vector3(8f, testHoverY, 0f));
                hold(3.2f);
                check(mission.State == CargoMissionState.AwaitingPickup && releases == 0 && completions == 0,
                    "Visiting delivery before pickup cannot skip the sequence");
                moveDrone(new Vector3(0f, testHoverY, 0f));
                drone.SetArmed(false);
                hold(.5f);
                check(mission.HoldProgress == 0f, "Disarmed drone cannot pick up cargo");
                drone.SetArmed(true);
                hold(1.4f);
                check(mission.HoldProgress > 1f && carrier.CurrentCargo == null, "Pickup requires a continuous dwell, not trigger entry");
                moveDrone(new Vector3(7f, testHoverY, 0f));
                hold(.02f);
                check(mission.HoldProgress == 0f, "Leaving pickup clears the partial dwell");
                moveDrone(new Vector3(0f, testHoverY, 0f));
                drone.Body.linearVelocity = Vector3.forward * 2f;
                mission.Tick(4f);
                check(mission.HoldProgress == 0f && carrier.CurrentCargo == null, "Fast fly-through does not count as hovering");
                drone.Body.linearVelocity = Vector3.zero;
                drone.Body.rotation = Quaternion.Euler(0f, 0f, 45f);
                mission.Tick(4f);
                check(mission.HoldProgress == 0f, "Excessive tilt blocks pickup");
                moveDrone(new Vector3(0f, testHoverY, 0f));
                drone.Body.angularVelocity = Vector3.up;
                mission.Tick(4f);
                check(mission.HoldProgress == 0f, "Spinning in place blocks pickup");
                drone.Body.angularVelocity = Vector3.zero;
                float emptyHover = drone.HoverThrottle;
                Vector3 oldCargoPosition = cargo.Body.position;
                hold(1.49f);
                check(carrier.CurrentCargo == null && mission.HoldProgress > 1.45f,
                    "Cargo is not captured before the 1.5-second dwell has elapsed");
                hold(.03f);
                check(mission.State == CargoMissionState.Transporting && carrier.CurrentCargo == cargo && pickups == 1,
                    "A 1.5-second dwell attaches exactly the assigned cargo once");
                check(Vector3.Distance(cargo.WorldLiftingPoint, carrier.WorldMount) < .01f && !cargo.Body.isKinematic
                    && cargo.transform.parent != drone.transform && cargo.Body.position.y > oldCargoPosition.y,
                    "Cargo docks directly against the drone without a cable; both bodies remain dynamic");
                check(Mathf.Abs(drone.Body.mass - profile.mass) < .001f && Mathf.Abs(drone.FlightMass - 3f) < .001f,
                    "Payload mass is counted once; drone Rigidbody mass is not inflated");
                float loadedHover = drone.HoverThrottle;
                check(loadedHover > emptyHover + .1f && loadedHover < 1f, "Half-kilogram load increases hover throttle with unchanged motors");
                report.Add($"MEASURE hover empty={emptyHover:P2}, loaded={loadedHover:P2}; total mass={drone.FlightMass:F2} kg");
                hold(3.2f);
                check(pickups == 1 && releases == 0 && !carrier.TryAttach(cargo), "Pickup cannot repeat or attach a second copy");

                // Real force/joint simulation, isolated from the mission's dwell clock.
                resetFlying();
                drone.SetArmed(false);
                fly(.5f, 0f);
                check(Mathf.Abs(drone.Body.linearVelocity.y + 1.855f) < .04f
                    && Mathf.Abs(cargo.Body.linearVelocity.y + 1.855f) < .04f,
                    "Attached bodies free-fall together at Mars gravity without double gravity");
                check(Vector3.Distance(cargo.Body.position, carrier.DockedPosition(cargo)) < .02f,
                    "Rigid attachment keeps the relative offset during physical simulation");
                resetFlying();
                fly(2f, loadedHover);
                drone.Body.linearVelocity = cargo.Body.linearVelocity = Vector3.zero;
                float height = drone.Body.position.y;
                fly(2f, loadedHover);
                check(Mathf.Abs(drone.Body.position.y - height) < .06f && Mathf.Abs(cargo.Body.linearVelocity.y) < .05f,
                    "Loaded hover balances the actual weight of both bodies in PhysX");
                resetFlying();
                fly(1f, emptyHover);
                check(drone.Body.linearVelocity.y < -.5f, "Empty-drone hover throttle cannot support the loaded drone");
                resetFlying();
                drone.SetArmed(false);
                drone.Body.AddForce(Vector3.right * 3f);
                fly(.01f, 0f);
                float loadedComVx = (drone.Body.mass * drone.Body.linearVelocity.x
                    + cargo.Mass * cargo.Body.linearVelocity.x) / drone.FlightMass;
                check(Mathf.Abs(loadedComVx - .01f) < .001f, "Known 3 N force accelerates the 3 kg assembly by F/m");
                resetFlying();
                drone.SetArmed(false);
                drone.Body.AddTorque(Vector3.right);
                fly(.01f, 0f);
                float loadedRotation = drone.Body.angularVelocity.x;
                carrier.Release();
                resetFlying();
                drone.SetArmed(false);
                drone.Body.AddTorque(Vector3.right);
                fly(.01f, 0f);
                float emptyRotation = drone.Body.angularVelocity.x;
                check(loadedRotation > 0f && loadedRotation < emptyRotation * .8f,
                    "Cargo below the drone increases rotational inertia for the same applied torque");
                report.Add($"MEASURE roll rate after 1 Nm for 0.01 s: empty={emptyRotation:F5}, loaded={loadedRotation:F5} rad/s");
                resetFlying();
                check(carrier.TryAttach(cargo), "Released cargo can be attached again before delivery");
                drone.Body.linearVelocity = new Vector3(2f, .3f, 0f);
                cargo.Body.linearVelocity = new Vector3(2f, .3f, 0f);
                cargo.Body.angularVelocity = Vector3.up * .1f;
                Vector3 releaseVelocity = cargo.Body.linearVelocity;
                Vector3 releaseSpin = cargo.Body.angularVelocity;
                carrier.Release();
                check(cargo.Body.linearVelocity == releaseVelocity && cargo.Body.angularVelocity == releaseSpin
                    && drone.PayloadMass == 0f && Mathf.Abs(drone.HoverThrottle - emptyHover) < .0001f,
                    "Release preserves momentum and immediately removes payload metadata");
                float beforeY = cargo.Body.linearVelocity.y;
                fly(.2f, loadedHover);
                check(cargo.Body.linearVelocity.y < beforeY - .7f, "Released cargo falls under Mars gravity, not Earth gravity");
                resetFlying();
                cargo.massKg = 2f;
                cargo.ConfigureBody();
                check(carrier.TryAttach(cargo) && drone.HoverThrottle > 1f, "Overweight cargo is allowed but telemetry reports insufficient lift");
                fly(2f, 1f);
                check(drone.Body.linearVelocity.y < -1f, "Overweight drone descends at full throttle; no hidden lift multiplier");
                carrier.Release();
                cargo.massKg = .5f;
                cargo.ConfigureBody();
                cargo.Body.interpolation = RigidbodyInterpolation.None;

                resetFlying();
                cargo.Body.position += Vector3.right * 2f;
                Physics.SyncTransforms();
                check(carrier.TryAttach(cargo) && Mathf.Abs(drone.FlightCenterOfMass.x) < .001f,
                    "A pickup two metres off-centre is accepted and docks the load centrally");
                fly(3f, drone.HoverThrottle);
                report.Add($"MEASURE docked: rate={drone.Body.angularVelocity.magnitude:F4}, tilt={Vector3.Angle(drone.Body.rotation * Vector3.up, Vector3.up):F3}, COM={drone.FlightCenterOfMass}, saturated={drone.MotorsSaturated}");
                check(drone.Body.angularVelocity.magnitude < .15f
                    && Vector3.Angle(drone.Body.rotation * Vector3.up, Vector3.up) < 5f
                    && Mathf.Abs(drone.FlightCenterOfMass.x) < .001f,
                    "Directly mounted cargo stays stable with the original motor limits");
                carrier.Release();
                resetFlying();
                environment.windVelocity = Vector3.right * 10f;
                fly(.2f, 0f);
                check(cargo.Body.linearVelocity.x > .04f, "Free cargo responds to the same atmospheric wind as the drone");
                environment.windVelocity = Vector3.zero;
                resetFlying();
                carrier.TryAttach(cargo);
                carrier.enabled = false;
                check(cargo.Carrier == null && carrier.CurrentCargo == null && drone.PayloadMass == 0f,
                    "Disabling the carrier releases cargo and clears mass metadata");
                carrier.enabled = true;

                // Full sequence through real sensor callbacks and a physical landing.
                mission.ResetMission();
                moveDrone(new Vector3(0f, testHoverY, 0f));
                hold(3.1f);
                check(carrier.CurrentCargo == cargo, "Mission can restart after an interrupted transport");
                Vector3 delta = new Vector3(8f, testHoverY, 0f) - drone.Body.position;
                drone.Body.position += delta;
                cargo.Body.position += delta;
                drone.Body.linearVelocity = drone.Body.angularVelocity = Vector3.zero;
                cargo.Body.linearVelocity = cargo.Body.angularVelocity = Vector3.zero;
                Physics.SyncTransforms();
                hold(1.4f);
                check(mission.HoldProgress > 1f && carrier.CurrentCargo == cargo, "Delivery also requires its own continuous dwell");
                // Leave together, preserving the rigid grab's relative transform.
                drone.Body.position += Vector3.forward * 4f;
                cargo.Body.position += Vector3.forward * 4f;
                Physics.SyncTransforms();
                hold(.02f);
                check(mission.HoldProgress == 0f && releases == 0, "Leaving delivery cancels its countdown without dropping cargo");
                drone.Body.position -= Vector3.forward * 4f;
                cargo.Body.position -= Vector3.forward * 4f;
                Physics.SyncTransforms();
                hold(3.1f);
                check(mission.State == CargoMissionState.AwaitingLanding && carrier.CurrentCargo == null && releases == 1,
                    "Delivery dwell releases once and waits for actual landing");
                check(completions == 0 && !cargo.IsDelivered, "Releasing in the air does not complete the mission prematurely");
                for (int i = 0; i < 250; i++)
                {
                    cargo.StepPhysics(.01f);
                    physics.Simulate(.01f);
                    mission.Tick(.01f);
                }
                check(mission.State == CargoMissionState.Completed && completions == 1 && cargo.IsDelivered
                    && cargo.IsDeployed && cargo.HasGroundContact, "Physical landing completes the mission and invokes walker deployment hook");
                hold(4f);
                check(completions == 1 && !cargo.CanBePickedUp(carrier), "Completed mission does not repeat or pick up delivered cargo");
                droneObject.GetComponent<PlayerCheckpoint>().Respawn();
                check(mission.State == CargoMissionState.AwaitingPickup && !cargo.IsDelivered && !cargo.IsDeployed
                    && cargo.Body.linearVelocity == Vector3.zero && drone.PayloadMass == 0f,
                    "Player checkpoint reset restarts the mission and restores cargo state");
                moveDrone(new Vector3(0f, testHoverY, 0f));
                hold(3.1f);
                droneObject.GetComponent<PlayerCheckpoint>().Respawn();
                check(carrier.CurrentCargo == null && drone.PayloadMass == 0f
                    && Vector3.Distance(cargo.Body.position, cargoHome) < .01f,
                    "Respawn while carrying removes the joint and returns cargo home");
                report.Add("ALL PASSED");
            }
            catch (Exception error)
            {
                report.Add("FAILED: " + error);
                Debug.LogError("[Cargo checks] " + error);
            }
            finally
            {
                // Mission first, then carrier, then cargo: unsubscribe before object teardown.
                for (int i = owned.Count - 1; i >= 0; i--)
                    if (owned[i] != null && owned[i].GetComponent<CargoDeliveryMission>() != null)
                        Object.DestroyImmediate(owned[i]);
                if (owned.Count > 0 && owned[0] != null) Object.DestroyImmediate(owned[0]);
                foreach (var go in owned) if (go != null) Object.DestroyImmediate(go);
                if (profile != null) Object.DestroyImmediate(profile);
                if (environment != null) Object.DestroyImmediate(environment);
                SceneManager.UnloadSceneAsync(scene);
                File.WriteAllLines("Temp/cargo-mission-checks.txt", report);
                Debug.Log("[Cargo checks] " + string.Join("\n", report));
            }
        }

        static CargoHoverZone MakeZone(string name, Vector3 position, Scene scene, List<GameObject> owned)
        {
            var go = new GameObject(name);
            owned.Add(go);
            SceneManager.MoveGameObjectToScene(go, scene);
            go.transform.position = position;
            var volume = go.AddComponent<BoxCollider>();
            volume.isTrigger = true;
            volume.center = Vector3.up * 3.1f;
            volume.size = new Vector3(6f, 3f, 6f);
            return go.AddComponent<CargoHoverZone>();
        }
    }
}
