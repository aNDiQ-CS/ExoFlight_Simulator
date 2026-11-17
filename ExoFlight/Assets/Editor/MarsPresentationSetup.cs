using System;
using System.Linq;
using ExoFlight.Cargo;
using ExoFlight.Flight;
using ExoFlight.Missions.Mars;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem.XR;
using Unity.XR.CoreUtils;
using Object = UnityEngine.Object;

namespace ExoFlight.EditorTools
{
    public static class MarsPresentationSetup
    {
        [MenuItem("ExoFlight/Марс: подключить модель и автопилот")]
        public static void Install()
        {
            if (Application.isPlaying) throw new InvalidOperationException("Stop Play Mode first.");
            var mission = Object.FindFirstObjectByType<MarsMissionDirector>();
            if (mission == null) throw new InvalidOperationException("Open MarsMission first.");
            var drone = mission.drone;
            var old = drone.GetComponent<CargoMissionAssist>();
            var pilot = drone.GetComponent<DroneMissionAutopilot>();
            if (pilot == null) pilot = drone.gameObject.AddComponent<DroneMissionAutopilot>();
            pilot.mission = mission;
            pilot.outbound = old.routeBeforePickup;
            pilot.loaded = old.routeAfterPickup;
            pilot.landing = old.routeLanding;
            old.enabled = false;
            if (drone.transform.Find("Ares 07 - player visual") == null)
            {
                var archive = new GameObject("Archived primitive drone visual").transform;
                archive.SetParent(drone.transform, false);
                foreach (var child in drone.transform.Cast<Transform>().ToArray())
                    if (child.name == "Body" || child.name == "Nose" || child.name.StartsWith("Arm ")
                        || child.name.StartsWith("Motor ") || child.name.StartsWith("Rotor "))
                        child.SetParent(archive, false);
                archive.gameObject.SetActive(false);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MarsLandscape/Ares 07 FPV Quadcopter.prefab");
                var visual = (GameObject)PrefabUtility.InstantiatePrefab(prefab, drone.transform);
                PrefabUtility.UnpackPrefabInstance(visual, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                visual.name = "Ares 07 - player visual";
                const float scale = 10.2f;
                visual.transform.localScale = Vector3.one * scale;
                visual.transform.localRotation = Quaternion.identity;
                string[] motors = { "Front right motor", "Rear right motor", "Rear left motor", "Front left motor" };
                for (int i = 0; i < 4; i++)
                {
                    Transform motor = visual.GetComponentsInChildren<Transform>().Single(t => t.name == motors[i]);
                    motor.localPosition = drone.profile.RotorPosition(i) / scale;
                    drone.rotorVisuals[i] = motor.Find("Propeller");
                }
                visual.transform.localPosition = Vector3.down * drone.rotorVisuals[0].localPosition.y * scale;
                foreach (var collider in visual.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(collider);
                foreach (var body in visual.GetComponentsInChildren<Rigidbody>()) Object.DestroyImmediate(body);
            }
            var input = drone.GetComponent<KeyboardDroneInput>();
            var vr = drone.GetComponent<DroneVrView>();
            if (vr == null)
            {
                vr = drone.gameObject.AddComponent<DroneVrView>();
                vr.seatOffset = input.fpvCamera.transform.localPosition;
                var origin = new GameObject("VR View Origin - level horizon");
                origin.transform.SetParent(drone.transform, false);
                origin.transform.localPosition = vr.seatOffset;
                var offset = new GameObject("Camera Offset");
                offset.transform.SetParent(origin.transform, false);
                input.fpvCamera.transform.SetParent(offset.transform, false);
                input.fpvCamera.transform.localPosition = Vector3.zero;
                input.fpvCamera.transform.localRotation = Quaternion.Euler(-input.cameraTilt, 0f, 0f);
                var xr = origin.AddComponent<XROrigin>();
                xr.Camera = input.fpvCamera;
                xr.CameraFloorOffsetObject = offset;
                xr.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Device;
                xr.CameraYOffset = 0f;
                vr.viewOrigin = origin.transform;
                vr.poseDriver = input.fpvCamera.gameObject.AddComponent<TrackedPoseDriver>();
                vr.poseDriver.enabled = false;
            }
            // The reader defaults to unverified. Re-running setup must preserve a user's later calibration.
            // The panel shares the gimballed instrument mount, never the tracked camera transform.
            if (mission.display != null && mission.display.transform.parent != vr.viewOrigin)
                mission.display.transform.SetParent(vr.viewOrigin, true);
            MarsRouteGuidanceSetup.Install(mission);
            EditorUtility.SetDirty(drone);
            EditorUtility.SetDirty(pilot);
            EditorSceneManager.MarkSceneDirty(drone.gameObject.scene);
            EditorSceneManager.SaveScene(drone.gameObject.scene);
            Debug.Log("[Mars presentation] Ares, motor-input autopilot and tracked VR camera installed.");
        }
    }
}
