using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ExoFlight.Cargo;
using ExoFlight.Flight;
using ExoFlight.Missions.Mars;
using ExoFlight.Triggers;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ExoFlight.EditorTools
{
    /// <summary>Integration checks use actual trigger callbacks, cargo joint, physics landing and mission events.</summary>
    public static class MarsMissionChecks
    {
        static MarsMissionDirector mission;
        static readonly List<string> results = new List<string>();
        static int phase, zone;
        static double deadline;
        static float since, bareHover;
        static Vector3 home;
        static bool oldKeyboard, oldRouter;
        public static bool Running { get; private set; }
        public static string Result { get; private set; }
        static bool continuous;
        static string lastRoute;
        static float lastProgress, peakSpeed;
        static readonly List<string> flightLog = new List<string>();

        [MenuItem("Tools/ExoFlight/Run Continuous Mars Flight (Play Mode)")]
        public static void RunContinuous()
        {
            if (!EditorApplication.isPlaying || Running) throw new InvalidOperationException("Enter Play Mode; finish other checks first.");
            if (Object.FindAnyObjectByType<DroneMissionAutopilot>() != null)
            {
                MarsAutopilotChecks.Run();
                return;
            }
            mission = Object.FindAnyObjectByType<MarsMissionDirector>();
            if (mission == null) throw new InvalidOperationException("Open MarsMission.");
            var input = mission.drone.GetComponent<KeyboardDroneInput>();
            var router = mission.drone.GetComponent<DroneInputRouter>();
            oldKeyboard = input.enabled; input.enabled = false;
            oldRouter = router != null && router.enabled;
            if (router != null) router.enabled = false;
            continuous = Running = true; Result = "Continuous flight running";
            flightLog.Clear(); lastRoute = null; peakSpeed = 0f;
            deadline = EditorApplication.timeSinceStartup + 480d;
            Time.timeScale = 3f;
            mission.assist.BeginDemo();
            lastProgress = Time.time;
            EditorApplication.update += WatchFlight;
        }

        static void WatchFlight()
        {
            if (!continuous) return;
            if (!EditorApplication.isPlaying || mission == null) { EndFlight("Interrupted"); return; }
            peakSpeed = Mathf.Max(peakSpeed, mission.drone.Body.linearVelocity.magnitude);
            if (lastRoute != mission.assist.Stage)
            {
                lastRoute = mission.assist.Stage; lastProgress = Time.time;
                flightLog.Add($"{Time.time:F1}s | {lastRoute} | {mission.Stage} | {mission.drone.Body.position}");
            }
            if (mission.MissionComplete && !mission.voice.IsBusy)
            {
                EndFlight(mission.NextZoneIndex == 9 && mission.cargoMission.cargo.IsDelivered ? "PASS" : "FAIL: incomplete cargo/route");
                return;
            }
            if (mission.Stage == MarsMissionStage.Failed || (!mission.assist.Active && !mission.MissionComplete && Time.time - lastProgress > 5f))
                EndFlight("FAIL: flight stopped at " + mission.Stage);
            else if (Time.time - lastProgress > 60f || EditorApplication.timeSinceStartup > deadline)
                EndFlight("FAIL: no route progress, target=" + mission.assist.Target + ", position=" + mission.drone.Body.position);
        }

        static void EndFlight(string status)
        {
            continuous = Running = false;
            EditorApplication.update -= WatchFlight;
            Time.timeScale = 1f;
            if (mission != null)
            {
                mission.assist.StopAssist(true);
                mission.drone.GetComponent<KeyboardDroneInput>().enabled = oldKeyboard;
                var router = mission.drone.GetComponent<DroneInputRouter>();
                if (router != null) router.enabled = oldRouter;
            }
            Result = status + " | Continuous rigidbody flight, no teleports between stages. Peak speed " + peakSpeed.ToString("F2") + " m/s\n" + string.Join("\n", flightLog);
            string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../../docs"));
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "mars-route-flight-verification-2026-11-16.txt"), Result);
            Debug.Log("[Mars continuous check] " + status);
        }

        [MenuItem("Tools/ExoFlight/Run Mars Mission Checks (Play Mode)")]
        public static void Run()
        {
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("Enter Play Mode in MarsMission first.");
            mission = Object.FindAnyObjectByType<MarsMissionDirector>();
            if (mission == null) throw new InvalidOperationException("Open MarsMission.");
            results.Clear(); Result = "Running"; Running = true;
            phase = 0; zone = 1;
            deadline = EditorApplication.timeSinceStartup + 120d;
            var keyboard = mission.drone.GetComponent<KeyboardDroneInput>();
            var router = mission.drone.GetComponent<DroneInputRouter>();
            oldKeyboard = keyboard.enabled; keyboard.enabled = false;
            oldRouter = router != null && router.enabled;
            if (router != null) router.enabled = false;
            mission.assist.StopAssist(true);
            mission.drone.GetComponent<PlayerCheckpoint>().Respawn();
            home = mission.cargoMission.cargo.Body.position;
            bareHover = mission.drone.HoverThrottle;
            Check("71 unique voice IDs and filenames", mission.voice.library.cues.Count == 71
                && mission.voice.library.cues.Select(c => c.id).Distinct().Count() == 71
                && mission.voice.library.cues.Select(c => c.fileName).Distinct().Count() == 71);
            Check("All 9 route zones ordered and wired", mission.zones.Length == 9 && mission.zones.Select((z,i) => z.order == i && z.mission == mission && z.Volume.isTrigger).All(x=>x));
            Check("Real rover payload: 10.6 kg, inherited WalkerCargo", mission.cargoMission.cargo is RoverCargo
                && mission.cargoMission.cargo is WalkerCargo && Mathf.Approximately(mission.cargoMission.cargo.Body.mass, 10.6f));
            Check("Rover visible with collider and one dynamic body", mission.cargoMission.cargo.GetComponentsInChildren<Renderer>().Length > 0
                && mission.cargoMission.cargo.GetComponentsInChildren<Rigidbody>().Length == 1 && !mission.cargoMission.cargo.Body.isKinematic);
            Check("UI hidden before task, world space", !mission.display.Visible && mission.display.displayCanvas.renderMode == RenderMode.WorldSpace);
            Check("Delivery gated before route", !mission.cargoMission.deliveryAllowed);
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }

        static void Tick()
        {
            if (!Running) return;
            try
            {
                if (!EditorApplication.isPlaying || mission == null) { Finish("Interrupted"); return; }
                if (EditorApplication.timeSinceStartup > deadline) throw new Exception("Timed out in phase " + phase + ", stage=" + mission.Stage + ", cargo=" + mission.cargoMission.State);
                switch (phase)
                {
                    case 0:
                        if (!mission.BriefingFinished || mission.voice.IsBusy) return;
                        Check("Missing audio did not block briefing", mission.Stage == MarsMissionStage.Outbound && mission.voice.HasSpoken("S0-05"));
                        Move(mission.zones[8].transform.position);
                        since = Time.time; phase = 1; break;
                    case 1:
                        if (Time.time - since < 2.6f || mission.voice.IsBusy) return;
                        Check("Early final-zone entry rejected", mission.NextZoneIndex == 0 && !mission.cargoMission.deliveryAllowed);
                        Move(mission.zones[0].transform.position); phase = 2; break;
                    case 2:
                        if (mission.NextZoneIndex != 1 || mission.voice.IsBusy) return;
                        Check("First real capsule overlap advanced route", mission.Stage == MarsMissionStage.Approach);
                        Move(mission.cargoMission.pickupZone.Volume.bounds.center);
                        phase = 3; break;
                    case 3:
                        if (mission.cargoMission.State != CargoMissionState.Transporting || mission.voice.IsBusy) return;
                        Check("1.5 second physical hover picked up rover", mission.cargoMission.carrier.CurrentCargo == mission.cargoMission.cargo);
                        Check("Rigid attachment without rope", mission.cargoMission.cargo.GetComponent<ConfigurableJoint>() != null
                            && mission.cargoMission.cargo.GetComponentsInChildren<LineRenderer>().Length == 0);
                        Check("Cargo mass counted once", Mathf.Abs(mission.drone.FlightMass - 18.6f) < .01f && Mathf.Abs(mission.drone.Body.mass - 8f) < .01f);
                        Check("Payload increases hover demand, sufficient thrust", mission.drone.HoverThrottle > bareHover + .2f && mission.drone.HoverThrottle < 1f);
                        Check("Accepted spatial UI active during transport", mission.display.Visible);
                        var corners = new Vector3[4];
                        ((RectTransform)mission.display.displayCanvas.transform).GetWorldCorners(corners);
                        var camera = mission.drone.GetComponent<KeyboardDroneInput>().fpvCamera;
                        var projected = corners.Select(camera.WorldToViewportPoint).ToArray();
                        Check("Entire task panel fits in desktop camera", projected.All(p => p.z > 0f && p.x > 0f && p.x < 1f && p.y > 0f && p.y < 1f));
                        float area = (projected.Max(p => p.x) - projected.Min(p => p.x)) * (projected.Max(p => p.y) - projected.Min(p => p.y));
                        Check("Panel occupies less than 6 percent of viewport", area < .06f);
                        Check("Pickup voice follows attachment", mission.voice.HasSpoken("S3-01"));
                        Move(mission.drone.Body.position + Vector3.up * 3f);
                        mission.cargoMission.carrier.Release();
                        since = Time.time; phase = 30; break;
                    case 30:
                        if (Time.time - since < 2f || !mission.cargoMission.cargo.HasGroundContact || mission.voice.IsBusy) return;
                        Check("Lost load returns to pickup and announces loss", mission.cargoMission.State == CargoMissionState.AwaitingPickup && mission.voice.HasSpoken("S3-09"));
                        Vector3 pickupDelta = mission.cargoMission.pickupZone.transform.position - mission.cargoMission.cargo.Body.position;
                        Check("Recovery zone follows fallen rover", Vector3.ProjectOnPlane(pickupDelta, Vector3.up).magnitude < .15f && Mathf.Abs(pickupDelta.y) < 1f);
                        Move(mission.cargoMission.pickupZone.Volume.bounds.center);
                        phase = 31; break;
                    case 31:
                        if (mission.cargoMission.State != CargoMissionState.Transporting || mission.voice.IsBusy) return;
                        Check("Dropped rover can be physically picked up again", mission.cargoMission.carrier.CurrentCargo == mission.cargoMission.cargo);
                        Move(mission.cargoMission.deliveryZone.Volume.bounds.center);
                        since = Time.time; phase = 4; break;
                    case 4:
                        if (Time.time - since < 2.5f || mission.voice.IsBusy) return;
                        Check("Cannot deliver before canyon and cave", mission.cargoMission.State == CargoMissionState.Transporting);
                        Move(mission.zones[zone].transform.position);
                        phase = 5; break;
                    case 5:
                        if (mission.NextZoneIndex != zone + 1 || mission.voice.IsBusy) return;
                        Check("Route gate " + (zone + 1) + " fired once", mission.NextZoneIndex == zone + 1);
                        if (zone == 4) Check("Headlight on inside cave", mission.headlight.enabled);
                        if (++zone < mission.zones.Length) { Move(mission.zones[zone].transform.position); break; }
                        Check("Return through same cave leads to landing", mission.Stage == MarsMissionStage.Landing && mission.cargoMission.deliveryAllowed && !mission.headlight.enabled);
                        Move(mission.cargoMission.deliveryZone.Volume.bounds.center);
                        phase = 6; break;
                    case 6:
                        if (mission.cargoMission.State != CargoMissionState.Completed) return;
                        Check("Rover released, landed and deployed on actual pad", mission.cargoMission.cargo.HasGroundContact
                            && mission.cargoMission.cargo.IsDelivered && ((RoverCargo)mission.cargoMission.cargo).IsDeployed);
                        Check("Mass removed after release", mission.cargoMission.carrier.CurrentCargo == null && mission.drone.PayloadMass == 0f);
                        Move(mission.assist.routeLanding.position);
                        mission.assist.StopAssist(true);
                        phase = 7; break;
                    case 7:
                        if (!mission.MissionComplete || mission.voice.IsBusy) return;
                        Check("Final voice waits for real landing and motors off", !mission.drone.Armed && mission.voice.HasSpoken("S6-07") && mission.voice.HasSpoken("S6-08"));
                        Check("Spatial panel hidden after mission", !mission.display.Visible);
                        mission.drone.GetComponent<PlayerCheckpoint>().Respawn();
                        since = Time.time; phase = 8; break;
                    case 8:
                        if (Time.time - since < .15f) return;
                        Check("R resets route, cargo, voice and UI", mission.NextZoneIndex == 0 && mission.cargoMission.State == CargoMissionState.AwaitingPickup
                            && !mission.cargoMission.cargo.IsDelivered && Vector3.Distance(mission.cargoMission.cargo.Body.position, home) < .2f
                            && !mission.display.Visible && !mission.voice.HasSpoken("S6-08"));
                        Finish("PASS"); break;
                }
            }
            catch (Exception e) { results.Add("FAIL: " + e.Message); Finish("FAIL"); }
        }

        static void Move(Vector3 point)
        {
            var body = mission.drone.Body;
            var carrier = mission.cargoMission.carrier;
            Vector3 delta = point - body.position;
            if (carrier.CurrentCargo != null)
            {
                var cargo = carrier.CurrentCargo.Body;
                cargo.position += delta;
                cargo.transform.position = cargo.position;
                cargo.linearVelocity = cargo.angularVelocity = Vector3.zero;
            }
            mission.assist.StopAssist();
            body.position = point; body.transform.position = point;
            body.linearVelocity = body.angularVelocity = Vector3.zero;
            Physics.SyncTransforms();
            mission.drone.SetInput(default);
            mission.drone.SetArmed(true);
            mission.assist.BeginHold();
        }

        static void Check(string label, bool condition)
        {
            if (!condition) throw new Exception(label);
            results.Add("PASS: " + label);
        }

        static void Finish(string result)
        {
            Running = false;
            EditorApplication.update -= Tick;
            Result = result + " | " + results.Count + " checks | " + string.Join("\n", results);
            if (mission != null)
            {
                mission.assist.StopAssist(true);
                mission.drone.GetComponent<KeyboardDroneInput>().enabled = oldKeyboard;
                var router = mission.drone.GetComponent<DroneInputRouter>();
                if (router != null) router.enabled = oldRouter;
            }
            string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../../docs"));
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "mars-mission-verification-2026-11-16.txt"), Result);
            Debug.Log("[Mars checks] " + Result);
        }
    }
}
