using System;
using System.Collections.Generic;
using System.IO;
using ExoFlight.Flight;
using ExoFlight.Missions.Mars;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ExoFlight.EditorTools
{
    public static class MarsAutopilotChecks
    {
        static DroneMissionAutopilot pilot;
        static readonly List<string> log = new List<string>();
        static string lastStatus;
        static float lastProgress, began, peakSpeed, peakTilt, loadedThrottle, emptyThrottle;
        static double deadline;
        static int collisions;
        static MarsFlightContactProbe probe;
        static string peakTiltLocation;
        static string reportPath;
        static float originalVolume, peakDistanceError;
        public static bool Running { get; private set; }
        public static string Result { get; private set; } = "Not run";
        [MenuItem("Tools/ExoFlight/Test Motor Autopilot (Play Mode)")]
        public static void Run() => Begin("mars-autopilot-verification-");
        [MenuItem("Tools/ExoFlight/Test Through Cave Mission (Play Mode)")]
        public static void RunCaveExit() => Begin("mars-through-cave-flight-");
        static void Begin(string prefix)
        {
            if (!Application.isPlaying || Running) throw new InvalidOperationException("Enter Play Mode first.");
            pilot = Object.FindFirstObjectByType<DroneMissionAutopilot>();
            if (pilot == null) throw new InvalidOperationException("Open the Mars presentation scene.");
            log.Clear(); lastStatus = null; peakSpeed = peakTilt = loadedThrottle = emptyThrottle = 0f;
            collisions = 0;
            peakDistanceError = 0f;
            originalVolume = AudioListener.volume;
            AudioListener.volume = 0f;
            reportPath = Path.GetFullPath(Path.Combine(Application.dataPath, "../../docs/" + prefix + DateTime.Now.ToString("yyyy-MM-dd") + ".txt"));
            peakTiltLocation = "";
            probe = pilot.gameObject.AddComponent<MarsFlightContactProbe>();
            began = lastProgress = Time.time;
            deadline = EditorApplication.timeSinceStartup + 900;
            Time.timeScale = 4f;
            pilot.BeginMission();
            Running = true; Result = "Running";
            EditorApplication.update += Watch;
        }
        static void Watch()
        {
            if (!Application.isPlaying || pilot == null) { End("Interrupted"); return; }
            var mission = pilot.mission;
            var drone = mission.drone;
            peakSpeed = Mathf.Max(peakSpeed, drone.Body.linearVelocity.magnitude);
            float tilt = Vector3.Angle(drone.transform.up, Vector3.up);
            if (tilt > peakTilt) { peakTilt = tilt; peakTiltLocation = pilot.Status + " at " + drone.Body.position; }
            if (drone.PayloadMass > 0) loadedThrottle = Mathf.Max(loadedThrottle, drone.Input.Throttle);
            else emptyThrottle = Mathf.Max(emptyThrottle, drone.Input.Throttle);
            if (pilot.Status != lastStatus)
            {
                lastStatus = pilot.Status; lastProgress = Time.time;
                log.Add($"{Time.time - began:F1}s | {pilot.Status} | gate {mission.NextZoneIndex}/9 | {drone.Body.position} | {drone.Body.linearVelocity.magnitude:F2} m/s");
            }
            // Real AudioClips keep their real duration at accelerated simulation speed.
            if (mission.voice.IsBusy) lastProgress = Time.time;
            var display = mission.display;
            if (mission.cargoMission.State == ExoFlight.Cargo.CargoMissionState.Transporting
                && display.Visible && display.metricUnit.text == "МЕТРОВ" && !display.InZone(mission.cargoMission.deliveryZone)
                && float.TryParse(display.metric.text, out float shown))
            {
                float expected = Vector3.Distance(drone.Body.position, mission.cargoMission.deliveryZone.transform.position);
                peakDistanceError = Mathf.Max(peakDistanceError, Mathf.Abs(shown - expected));
                if (peakDistanceError > 2f) { End("FAIL: UI distance is not to the delivery pad"); return; }
            }
            if (drone.Body.isKinematic || drone.GetComponent<ExoFlight.Cargo.CargoMissionAssist>().enabled)
            { End("FAIL: physical control ownership"); return; }
            if (mission.MissionComplete && !mission.voice.IsBusy)
            { End(mission.NextZoneIndex == 9 && mission.cargoMission.cargo.IsDelivered && !drone.Armed ? "PASS" : "FAIL: incomplete mission"); return; }
            if (mission.Stage == MarsMissionStage.Failed) End("FAIL: mission failed");
            else if (Time.time - lastProgress > 100f || EditorApplication.timeSinceStartup > deadline)
                End("FAIL: stalled at " + pilot.Status + ", target=" + pilot.Target + ", position=" + drone.Body.position);
        }
        static void End(string result)
        {
            Running = false;
            EditorApplication.update -= Watch;
            if (probe != null) { collisions = probe.impacts; Object.Destroy(probe); }
            if (result == "PASS" && collisions > 0) result = "FAIL: route impacts";
            Time.timeScale = 1f;
            AudioListener.volume = originalVolume;
            Result = result + $"\nMotor-input autopilot, no route teleports or servo forces. Simulation {Time.time - began:F1}s."
                + $"\nPeak speed {peakSpeed:F2} m/s, peak tilt {peakTilt:F2} deg ({peakTiltLocation}). Peak throttle empty {emptyThrottle:F3}, loaded {loadedThrottle:F3}. Impacts above 0.7 m/s: {collisions}."
                + $"\nMaximum displayed delivery-distance error: {peakDistanceError:F2} m (rounded UI, sampled between frames)."
                + "\n" + string.Join("\n", log);
            File.WriteAllText(reportPath, Result);
            if (pilot != null) pilot.Stop(true);
            Debug.Log("[Mars autopilot test] " + result);
        }
    }
    public sealed class MarsFlightContactProbe : MonoBehaviour
    {
        public int impacts;
        void OnCollisionEnter(Collision collision)
        {
            if (collision.relativeVelocity.magnitude > .7f && GetComponent<DroneFlightController>().Armed)
            { impacts++; Debug.LogWarning("[Flight probe] " + collision.gameObject.name + " at " + collision.relativeVelocity.magnitude + " m/s"); }
        }
    }
}
