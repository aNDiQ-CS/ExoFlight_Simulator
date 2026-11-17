using System;
using System.Collections.Generic;
using System.Linq;
using ExoFlight.Cargo;
using ExoFlight.Cargo.UI;
using ExoFlight.Flight;
using ExoFlight.Missions.Mars;
using ExoFlight.Triggers;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace ExoFlight.EditorTools
{
    public static class MarsMissionBuilder
    {
        public const string ScenePath = "Assets/Scenes/MarsMission.unity";
        const string MapPath = "Assets/MarsLandscape/Mars Landscape.unity";
        const string CargoScenePath = "Assets/Scenes/CargoUIDemo.unity";
        const string AssetsFolder = "Assets/MarsMission";

        [MenuItem("ExoFlight/Марс: открыть миссию с озвучкой")]
        public static void OpenOrBuild()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode first.");
            if (SceneManager.GetActiveScene().isDirty) throw new InvalidOperationException("Save the current scene first.");
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null)
            {
                EditorSceneManager.OpenScene(ScenePath);
                return;
            }
            if (!AssetDatabase.CopyAsset(MapPath, ScenePath)) throw new InvalidOperationException("Could not copy the Mars map.");
            Scene scene = EditorSceneManager.OpenScene(ScenePath);
            var preview = Object.FindAnyObjectByType<MarsFlightPreview>();
            var data = new SerializedObject(preview);
            var routeProperty = data.FindProperty("route");
            var mapRoute = Enumerable.Range(0, routeProperty.arraySize)
                .Select(i => routeProperty.GetArrayElementAtIndex(i).vector3Value).ToArray();
            var terrain = Terrain.activeTerrain;
            var spawn = GameObject.Find("Drone Spawn").transform;
            Vector3 start = spawn.position + Vector3.up * .7f;
            Quaternion startRotation = spawn.rotation;
            var storm = Object.FindAnyObjectByType<MarsStorm>();
            var sceneryRover = GameObject.Find("Perseverance | NASA JPL-Caltech");
            Vector3 pickupPosition = sceneryRover.transform.position;
            pickupPosition.y = PadHeight(terrain, pickupPosition, 5f);
            Vector3 deliveryPosition = new Vector3(550f, 0f, 580f);
            deliveryPosition.y = PadHeight(terrain, deliveryPosition, 10f);
            // The original map remains untouched. Only the mission copy replaces the scenery rover.
            sceneryRover.name = "Archived scenery rover";
            sceneryRover.SetActive(false);
            preview.enabled = false;
            foreach (var c in preview.GetComponentsInChildren<Camera>()) c.enabled = false;
            foreach (var a in preview.GetComponentsInChildren<AudioListener>()) a.enabled = false;

            Scene demo = EditorSceneManager.OpenScene(CargoScenePath, OpenSceneMode.Additive);
            var sourceDrone = demo.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<DroneFlightController>()).Single();
            SceneManager.SetActiveScene(scene);
            var droneObject = Object.Instantiate(sourceDrone.gameObject);
            droneObject.name = "Player Drone - Mars cargo mission";
            var drone = droneObject.GetComponent<DroneFlightController>();
            var input = drone.GetComponent<KeyboardDroneInput>();
            var assist = drone.GetComponent<CargoMissionAssist>();
            var display = drone.GetComponentInChildren<CargoOnboardDisplay>(true);
            EditorSceneManager.CloseScene(demo, true);
            drone.transform.SetPositionAndRotation(start, startRotation);

            var profile = CopyProfile(drone.profile);
            var environment = CopyEnvironment(drone.environment);
            drone.profile = profile;
            drone.environment = environment;
            drone.mode = DroneFlightMode.Angle;
            drone.windAtRotors = true;
            // Enlarge the existing four-rotor geometry consistently with the cargo profile.
            foreach (Transform child in drone.transform)
            {
                if (child.GetComponent<Camera>() || child.GetComponent<PlayerTriggerSensor>() || child.GetComponent<CargoOnboardDisplay>()) continue;
                child.localPosition *= 2f;
                child.localScale *= 2f;
            }
            foreach (var c in drone.GetComponents<BoxCollider>()) { c.center *= 2f; c.size *= 2f; }
            var sensor = drone.GetComponentInChildren<PlayerTriggerSensor>();
            sensor.transform.localScale = Vector3.one;
            var capsule = sensor.GetComponent<CapsuleCollider>();
            capsule.radius = 1.15f; capsule.height = 2.5f; capsule.center = Vector3.zero;
            input.fpvCamera.transform.localPosition = new Vector3(0f, .46f, 2.3f);
            input.cameraTilt = -8f;
            input.fieldOfView = 70f;
            input.fpvCamera.transform.localRotation = Quaternion.Euler(8f, 0f, 0f);
            input.fpvCamera.fieldOfView = 70f;
            input.fpvCamera.farClipPlane = 1800f;
            display.assembly.localPosition = input.fpvCamera.transform.localPosition + new Vector3(.55f, -.68f, 1.2f);
            drone.GetComponent<DroneCameraView>().thirdPersonOffset = new Vector3(0f, 4f, -8f);
            drone.ConfigureBody();

            var root = new GameObject("Mars Mission - route, cargo and voice");
            root.AddComponent<FlightSimulationClock>();
            var cargoMission = root.AddComponent<CargoDeliveryMission>();
            var director = root.AddComponent<MarsMissionDirector>();
            var voice = root.AddComponent<MarsVoicePlayer>();
            voice.library = MarsVoiceImporter.EnsureLibrary();
            director.drone = drone; director.cargoMission = cargoMission; director.voice = voice;
            director.storm = storm; director.display = display; director.assist = assist;
            cargoMission.carrier = drone.GetComponent<DroneCargoCarrier>();
            cargoMission.carrier.mountPoint = new Vector3(0f, -.38f, 0f);
            cargoMission.carrier.maximumReach = 8f;
            cargoMission.carrier.horizontalTolerance = 5f;
            cargoMission.holdSeconds = 1.5f;
            cargoMission.recoverLostCargo = true;
            cargoMission.pickupAllowed = cargoMission.deliveryAllowed = false;
            cargoMission.pickupZone = MakePad("Pickup - rover", root.transform, pickupPosition, 12f);
            cargoMission.deliveryZone = MakePad("Delivery - outside cave", root.transform, deliveryPosition, 20f);
            cargoMission.cargo = MakeRover(root.transform, pickupPosition, environment);
            assist.mission = cargoMission;
            assist.maximumSpeed = 9f;
            assist.useThirdPersonForDemo = false;
            display.mission = cargoMission;
            var contact = drone.gameObject.AddComponent<MarsDroneContact>();
            contact.mission = director;

            var landing = new GameObject("Landing - drone beside rover").transform;
            landing.SetParent(root.transform);
            landing.position = deliveryPosition + new Vector3(6.3f, .45f, 0f);
            assist.routeLanding = landing;
            var destination = new GameObject("Delivery ground reference").transform;
            destination.SetParent(root.transform); destination.position = deliveryPosition;
            director.landingPoint = destination;
            var lamp = new GameObject("Cave headlight").AddComponent<Light>();
            lamp.transform.SetParent(input.fpvCamera.transform, false);
            lamp.type = LightType.Spot; lamp.range = 55f; lamp.spotAngle = 95f;
            lamp.intensity = 15f; lamp.color = new Color(.95f, .9f, .8f); lamp.enabled = false;
            director.headlight = lamp;

            var zonesParent = new GameObject("Voice route triggers - ordered").transform;
            zonesParent.SetParent(root.transform);
            director.zones = new[]
            {
                Zone(0, "S2 - storm front on approach", mapRoute[40] + Vector3.up * 3f, new Vector3(55f, 40f, 45f), MarsMissionStage.Approach, false, new[]{"S2-01", "S2-02"}, zonesParent, director),
                Zone(1, "S4 - canyon approach", mapRoute[54] + Vector3.up * 3f, new Vector3(24f, 40f, 18f), MarsMissionStage.CanyonApproach, true, new[]{"S4-01"}, zonesParent, director),
                Zone(2, "S4 - canyon entrance", mapRoute[58] + Vector3.up * 3f, new Vector3(23f, 36f, 14f), MarsMissionStage.Canyon, true, new[]{"S4-02"}, zonesParent, director),
                Zone(3, "S4 - shelter recommendation", mapRoute[61] + Vector3.up * 2f, new Vector3(22f, 24f, 12f), MarsMissionStage.Canyon, true, new[]{"S4-04", "S4-05"}, zonesParent, director),
                Zone(4, "S5 - enter lava tube", mapRoute[65] + Vector3.up * 2f, new Vector3(23f, 24f, 16f), MarsMissionStage.Cave, true, new[]{"S5-01"}, zonesParent, director),
                Zone(5, "S5 - narrow tunnel", mapRoute[68] + Vector3.up * 2f, new Vector3(24f, 23f, 18f), MarsMissionStage.Cave, true, new[]{"S5-03"}, zonesParent, director),
                Zone(6, "S5 - chamber turnaround", mapRoute[73] + Vector3.up * 2f, new Vector3(30f, 22f, 18f), MarsMissionStage.CaveExit, true, new string[0], zonesParent, director),
                Zone(7, "S5 - light on return", mapRoute[66] + Vector3.up * 2f, new Vector3(24f, 22f, 16f), MarsMissionStage.CaveExit, true, new[]{"S5-06"}, zonesParent, director),
                Zone(8, "S6 - exit to landing area", mapRoute[62] + Vector3.up * 2f, new Vector3(24f, 24f, 12f), MarsMissionStage.Landing, true, new[]{"S6-01", "S6-02"}, zonesParent, director)
            };

            var path = new GameObject("Assisted route - P demonstration").transform;
            path.SetParent(root.transform);
            var before = new List<Vector3> { start };
            before.AddRange(mapRoute.Take(52).Select(p => p + Vector3.up * 3f));
            before.Add(pickupPosition + Vector3.up * 10f);
            before.Add(cargoMission.pickupZone.Volume.bounds.center);
            var after = new List<Vector3> { pickupPosition + Vector3.up * 10f };
            after.AddRange(mapRoute.Skip(52).Select((p, i) => p + Vector3.up * (i + 52 >= 64 ? 2f : 3f)));
            for (int i = 72; i >= 61; i--) after.Add(mapRoute[i] + Vector3.up * (i >= 64 ? 2f : 3f));
            float clearance = Mathf.Max(deliveryPosition.y + 15f, mapRoute[61].y + 15f);
            after.Add(new Vector3(mapRoute[61].x, clearance, mapRoute[61].z));
            after.Add(new Vector3(deliveryPosition.x, clearance, deliveryPosition.z));
            after.Add(cargoMission.deliveryZone.Volume.bounds.center);
            assist.routeBeforePickup = MakeWaypoints("Outbound", before, path);
            assist.routeAfterPickup = MakeWaypoints("With rover", after, path);
            director.navigationBeacon = MakeBeacon(root.transform);

            EditorSceneManager.MarkSceneDirty(scene);
            MarsCaveExitSetup.Install();
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = root;
            SceneView.lastActiveSceneView?.Frame(new Bounds(pickupPosition + Vector3.up * 3f, Vector3.one * 25f), false);
            Debug.Log("[Mars mission] Saved: 71 voice cues, 9 ordered route zones, rover cargo, existing spatial UI. Audio folder: " + MarsVoiceImporter.AudioFolder);
        }

        static DroneFlightProfile CopyProfile(DroneFlightProfile source)
        {
            string path = AssetsFolder + "/MarsCargoQuadcopter.asset";
            var profile = AssetDatabase.LoadAssetAtPath<DroneFlightProfile>(path);
            if (profile != null) return profile;
            profile = Object.Instantiate(source);
            profile.name = "Mars cargo quadcopter";
            profile.mass = 8f; profile.armLength = 1.5f; profile.propellerDiameter = 1.5f;
            profile.maximumRpm = 3600f; profile.idleRpm = 400f;
            profile.principalInertia = new Vector3(6f, 10f, 6f);
            profile.rotorInertia = .004f;
            profile.dragArea = new Vector3(1.4f, 2.8f, 1.4f);
            AssetDatabase.CreateAsset(profile, path);
            return profile;
        }

        static FlightEnvironment CopyEnvironment(FlightEnvironment source)
        {
            string path = AssetsFolder + "/MarsMissionEnvironment.asset";
            var environment = AssetDatabase.LoadAssetAtPath<FlightEnvironment>(path);
            if (environment != null) return environment;
            environment = Object.Instantiate(source);
            environment.name = "Mars mission atmosphere";
            environment.windProfile = null; environment.windVelocity = Vector3.zero;
            environment.airDensity = .016f; environment.gravity = new Vector3(0f, -3.71f, 0f);
            AssetDatabase.CreateAsset(environment, path);
            return environment;
        }

        static float PadHeight(Terrain terrain, Vector3 center, float radius)
        {
            float y = float.MinValue;
            for (int x = -1; x <= 1; x++) for (int z = -1; z <= 1; z++)
                y = Mathf.Max(y, terrain.SampleHeight(center + new Vector3(x * radius, 0f, z * radius)) + terrain.transform.position.y);
            return y + .4f;
        }

        static CargoHoverZone MakePad(string name, Transform parent, Vector3 floor, float width)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent); root.transform.position = floor;
            var volume = root.AddComponent<BoxCollider>();
            volume.isTrigger = true; volume.center = new Vector3(0f, 3.3f, 0f); volume.size = new Vector3(10f, 3.4f, 10f);
            var zone = root.AddComponent<CargoHoverZone>();
            zone.maximumSpeed = 1.5f; zone.maximumTilt = 35f;
            var deck = GameObject.CreatePrimitive(PrimitiveType.Cube);
            deck.name = "Landing deck"; deck.transform.SetParent(root.transform, false);
            deck.transform.localPosition = Vector3.down * .2f;
            deck.transform.localScale = new Vector3(width, .4f, width);
            deck.GetComponent<Renderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/MarsLandscape/Graphite hardware.mat");
            var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/MarsLandscape/FPV anodized orange.mat");
            for (int i = 0; i < 4; i++)
            {
                var bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
                bar.name = "Pad edge"; bar.transform.SetParent(root.transform, false);
                bar.transform.localPosition = new Vector3(i < 2 ? (i == 0 ? -1 : 1) * (width * .5f - .2f) : 0f, .015f,
                    i >= 2 ? (i == 2 ? -1 : 1) * (width * .5f - .2f) : 0f);
                bar.transform.localScale = i < 2 ? new Vector3(.18f, .025f, width) : new Vector3(width, .025f, .18f);
                bar.GetComponent<Renderer>().sharedMaterial = material;
                Object.DestroyImmediate(bar.GetComponent<Collider>());
            }
            return zone;
        }

        static RoverCargo MakeRover(Transform parent, Vector3 floor, FlightEnvironment environment)
        {
            var root = new GameObject("Rover cargo - 10.6 kg");
            root.transform.SetParent(parent);
            var visual = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MarsRover/Perseverance.prefab"));
            visual.name = "Perseverance visual";
            visual.transform.SetParent(root.transform, false);
            visual.transform.localPosition = Vector3.zero; visual.transform.localRotation = Quaternion.identity; visual.transform.localScale = Vector3.one;
            foreach (var c in visual.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
            var renderers = visual.GetComponentsInChildren<Renderer>();
            Bounds b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            float scale = 2.2f / Mathf.Max(b.size.x, b.size.z);
            visual.transform.localScale = Vector3.one * scale;
            visual.transform.localPosition = -b.center * scale;
            Vector3 size = b.size * scale;
            var box = root.AddComponent<BoxCollider>(); box.size = size;
            var cargo = root.AddComponent<RoverCargo>();
            cargo.massKg = 10.6f; cargo.environment = environment;
            cargo.dragArea = new Vector3(size.y * size.z, size.x * size.z, size.x * size.y) * .8f;
            cargo.liftingPoint = new Vector3(0f, size.y * .5f + .04f, 0f);
            root.transform.position = floor + Vector3.up * (size.y * .5f + .035f);
            cargo.ConfigureBody();
            return cargo;
        }

        static MarsMissionZone Zone(int order, string name, Vector3 center, Vector3 size, MarsMissionStage stage,
            bool cargo, string[] voices, Transform parent, MarsMissionDirector director)
        {
            var go = new GameObject((order + 1).ToString("00") + " - " + name);
            go.transform.SetParent(parent); go.transform.position = center;
            var box = go.AddComponent<BoxCollider>(); box.isTrigger = true; box.size = size;
            var zone = go.AddComponent<MarsMissionZone>();
            zone.order = order; zone.stage = stage; zone.requiresCargo = cargo; zone.voiceIds = voices; zone.mission = director;
            return zone;
        }

        static Transform[] MakeWaypoints(string name, List<Vector3> points, Transform parent)
        {
            var group = new GameObject(name).transform; group.SetParent(parent);
            return points.Select((p, i) => { var t = new GameObject(i.ToString("00")).transform; t.SetParent(group); t.position = p; return t; }).ToArray();
        }

        static Transform MakeBeacon(Transform parent)
        {
            var root = new GameObject("Next route beacon").transform; root.SetParent(parent);
            var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/MarsLandscape/Ice blue signal.mat");
            for (int i = 0; i < 4; i++)
            {
                var bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
                bar.name = "Navigation light"; bar.transform.SetParent(root, false);
                bar.transform.localRotation = Quaternion.Euler(0f, 0f, 45f + i * 90f);
                bar.transform.localPosition = bar.transform.localRotation * Vector3.right * 1f;
                bar.transform.localScale = new Vector3(.12f, 1.5f, .12f);
                bar.GetComponent<Renderer>().sharedMaterial = material;
                Object.DestroyImmediate(bar.GetComponent<Collider>());
            }
            root.gameObject.SetActive(false);
            return root;
        }
    }
}
