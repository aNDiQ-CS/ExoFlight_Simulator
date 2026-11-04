using System;
using ExoFlight.Cargo;
using ExoFlight.Flight;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace ExoFlight.Editor
{
    public static class CargoDemoBuilder
    {
        public const string ScenePath = "Assets/Scenes/CargoMissionTest.unity";
        public const string PrefabPath = "Assets/Prefabs/WalkerCargo.prefab";
        public const string EnvironmentPath = "Assets/CargoDemo/CargoPracticeAtmosphere.asset";

        [MenuItem("Tools/ExoFlight/Open Cargo Mission Demo")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Stop Play Mode and save the current scene first.");
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null)
            {
                EditorSceneManager.OpenScene(ScenePath);
                InstallAssist();
                return;
            }
            if (!AssetDatabase.IsValidFolder("Assets/CargoDemo")) AssetDatabase.CreateFolder("Assets", "CargoDemo");
            if (!AssetDatabase.CopyAsset(FlightDemoBuilder.ScenePath, ScenePath))
                throw new InvalidOperationException("Create FlightTest before the cargo demo.");
            var scene = EditorSceneManager.OpenScene(ScenePath);
            var drone = Object.FindAnyObjectByType<DroneFlightController>();
            if (drone == null) throw new InvalidOperationException("FlightTest has no player drone.");
            var environment = Object.Instantiate(drone.environment);
            environment.name = "CargoPracticeAtmosphere";
            // A calm practice scene; the existing flight/wind scene and shared profile stay unchanged.
            environment.windProfile = null;
            environment.windVelocity = Vector3.zero;
            AssetDatabase.CreateAsset(environment, EnvironmentPath);
            drone.environment = environment;
            drone.transform.SetPositionAndRotation(new Vector3(-28f, .4f, -20f), Quaternion.identity);
            drone.ConfigureBody();
            var carrier = drone.gameObject.AddComponent<DroneCargoCarrier>();
            var root = new GameObject("Cargo Delivery Mission");
            var mission = root.AddComponent<CargoDeliveryMission>();
            mission.carrier = carrier;
            var pickupMaterial = MakeMaterial("Pickup", new Color(.1f, .8f, .85f));
            var deliveryMaterial = MakeMaterial("Delivery", new Color(.35f, .85f, .25f));
            var cargoMaterial = MakeMaterial("Walker Cube", new Color(1f, .55f, .12f));
            var padMaterial = MakeMaterial("Landing Pad", new Color(.12f, .15f, .18f));
            Vector3 pickup = new Vector3(-28f, 0f, -15f);
            Vector3 delivery = new Vector3(-28f, 0f, 0f);
            mission.pickupZone = Zone(root.transform, "01 PICKUP", pickup, pickupMaterial, padMaterial);
            mission.deliveryZone = Zone(root.transform, "02 DELIVERY", delivery, deliveryMaterial, padMaterial);

            var cargoObject = new GameObject("Walker Cargo - 0.5 kg");
            cargoObject.transform.position = pickup + Vector3.up * .68f;
            cargoObject.AddComponent<BoxCollider>().size = Vector3.one * 1.2f;
            cargoObject.AddComponent<Rigidbody>();
            var cargo = cargoObject.AddComponent<WalkerCargo>();
            cargo.environment = environment;
            cargo.massKg = .5f;
            cargo.liftingPoint = Vector3.up * .6f;
            cargo.dragArea = Vector3.one * 1.44f;
            cargo.ConfigureBody();
            Part("Cube", cargoObject.transform, Vector3.zero, Vector3.one * 1.2f, cargoMaterial, false);
            PrefabUtility.SaveAsPrefabAssetAndConnect(cargoObject, PrefabPath, InteractionMode.AutomatedAction);
            cargoObject.transform.SetParent(root.transform, true);
            mission.cargo = cargo;
            if (drone.GetComponent<DroneCameraView>() == null) drone.gameObject.AddComponent<DroneCameraView>();
            drone.gameObject.AddComponent<CargoMissionAssist>().mission = mission;
            mission.pickupZone.ShowStatus("HOLD 1.5 s");
            mission.deliveryZone.ShowStatus("PICK UP CARGO FIRST");
            for (int i = 1; i < 5; i++)
                Part("Route marker " + i, root.transform, Vector3.Lerp(pickup, delivery, i / 5f) + Vector3.up * -.03f,
                    new Vector3(.3f, .025f, .9f), deliveryMaterial, false);
            PrefabUtility.RecordPrefabInstancePropertyModifications(drone);
            PrefabUtility.RecordPrefabInstancePropertyModifications(drone.transform);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = root;
            SceneView.lastActiveSceneView?.LookAt(pickup + Vector3.up, Quaternion.Euler(30f, 25f, 0f), 22f);
            Debug.Log("[Cargo] Created CargoMissionTest: 6 m pads, 1.2 m cube, direct dock, 1.5 s hold. V: camera, H: hover, P: demo.");
        }

        [MenuItem("Tools/ExoFlight/Install Cargo Test Assist in Current Scene")]
        public static void InstallAssist()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play Mode first.");
            var mission = Object.FindAnyObjectByType<CargoDeliveryMission>();
            if (mission == null || mission.carrier == null) throw new InvalidOperationException("Open CargoMissionTest first.");
            var assist = mission.carrier.GetComponent<CargoMissionAssist>();
            bool hasCamera = mission.carrier.GetComponent<DroneCameraView>() != null;
            if (assist != null && assist.mission == mission && hasCamera) return;
            if (!hasCamera) Undo.AddComponent<DroneCameraView>(mission.carrier.gameObject);
            if (assist == null) assist = Undo.AddComponent<CargoMissionAssist>(mission.carrier.gameObject);
            assist.mission = mission;
            EditorUtility.SetDirty(assist);
            var scene = mission.gameObject.scene;
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        static CargoHoverZone Zone(Transform parent, string name, Vector3 position, Material marker, Material pad)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.position = position;
            var trigger = root.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.center = new Vector3(0f, 3.1f, 0f);
            trigger.size = new Vector3(6f, 3f, 6f);
            var zone = root.AddComponent<CargoHoverZone>();
            zone.gizmoColor = marker.color;
            zone.label = name;
            Part("Pad", root.transform, Vector3.up * .025f, new Vector3(6f, .1f, 6f), pad, true);
            for (int i = -1; i <= 1; i += 2)
            {
                Part("Edge X", root.transform, new Vector3(i * 2.96f, .085f, 0f), new Vector3(.08f, .02f, 6f), marker, false);
                Part("Edge Z", root.transform, new Vector3(0f, .085f, i * 2.96f), new Vector3(6f, .02f, .08f), marker, false);
                Part("Alignment X", root.transform, new Vector3(i * 1.2f, .086f, 0f), new Vector3(.025f, .02f, 2.4f), marker, false);
                Part("Alignment Z", root.transform, new Vector3(0f, .086f, i * 1.2f), new Vector3(2.4f, .02f, .025f), marker, false);
            }
            var labelObject = new GameObject("World label");
            labelObject.transform.SetParent(root.transform, false);
            labelObject.transform.localPosition = new Vector3(0f, 5f, .5f);
            var text = labelObject.AddComponent<TextMesh>();
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.characterSize = .05f;
            text.fontSize = 64;
            text.color = marker.color;
            zone.worldLabel = text;
            return zone;
        }

        static GameObject Part(string name, Transform parent, Vector3 position, Vector3 scale, Material material, bool solid)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = material;
            if (!solid) Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        static Material MakeMaterial(string name, Color color)
        {
            string path = "Assets/CargoDemo/" + name + ".mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.color = color;
            material.SetFloat("_Smoothness", .15f);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        [MenuItem("Tools/ExoFlight/Upgrade Cargo Demo to Direct Dock")]
        public static void UpgradeDirectDock()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Stop Play Mode and save scene changes first.");
            const string dronePath = "Assets/Prefabs/PlayerDrone.prefab";
            var dronePrefab = PrefabUtility.LoadPrefabContents(dronePath);
            try
            {
                if (dronePrefab.GetComponent<DroneCameraView>() == null) dronePrefab.AddComponent<DroneCameraView>();
                PrefabUtility.SaveAsPrefabAsset(dronePrefab, dronePath);
            }
            finally { PrefabUtility.UnloadPrefabContents(dronePrefab); }
            var cargoPrefab = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                ResizeCargo(cargoPrefab.GetComponent<TransportableCargo>());
                PrefabUtility.SaveAsPrefabAsset(cargoPrefab, PrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(cargoPrefab); }
            AssetDatabase.SaveAssets();
            var scene = EditorSceneManager.OpenScene(ScenePath);
            var mission = Object.FindAnyObjectByType<CargoDeliveryMission>();
            var carrier = mission.carrier;
            var line = carrier.transform.Find("Rigid cargo grab - visual");
            if (line != null) Object.DestroyImmediate(line.gameObject);
            carrier.maximumReach = 6.5f;
            carrier.horizontalTolerance = 4.5f;
            carrier.minimumClearance = 0f;
            mission.holdSeconds = 1.5f;
            foreach (var zone in new[] { mission.pickupZone, mission.deliveryZone })
            {
                zone.Volume.center = Vector3.up * 3.1f;
                zone.Volume.size = new Vector3(6f, 3f, 6f);
                zone.maximumSpeed = 1.5f;
                zone.maximumAngularSpeed = 45f;
                zone.maximumTilt = 35f;
                foreach (Transform child in zone.transform)
                {
                    float sx = Mathf.Sign(child.localPosition.x), sz = Mathf.Sign(child.localPosition.z);
                    if (child.name == "Pad") child.localScale = new Vector3(6f, .1f, 6f);
                    if (child.name == "Edge X") { child.localPosition = new Vector3(sx * 2.96f, .085f, 0f); child.localScale = new Vector3(.08f, .02f, 6f); }
                    if (child.name == "Edge Z") { child.localPosition = new Vector3(0f, .085f, sz * 2.96f); child.localScale = new Vector3(6f, .02f, .08f); }
                    if (child.name == "Alignment X") { child.localPosition = new Vector3(sx * 1.2f, .086f, 0f); child.localScale = new Vector3(.025f, .02f, 2.4f); }
                    if (child.name == "Alignment Z") { child.localPosition = new Vector3(0f, .086f, sz * 1.2f); child.localScale = new Vector3(2.4f, .02f, .025f); }
                }
                zone.worldLabel.transform.localPosition = new Vector3(0f, 5f, .5f);
                zone.worldLabel.characterSize = .05f;
            }
            ResizeCargo(mission.cargo);
            mission.cargo.transform.position = mission.pickupZone.transform.position + Vector3.up * .68f;
            mission.pickupZone.ShowStatus("HOLD 1.5 s");
            mission.deliveryZone.ShowStatus("PICK UP CARGO FIRST");
            PrefabUtility.RecordPrefabInstancePropertyModifications(mission.cargo);
            PrefabUtility.RecordPrefabInstancePropertyModifications(mission.cargo.transform);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            InstallAssist();
            AssetDatabase.SaveAssets();
        }

        static void ResizeCargo(TransportableCargo cargo)
        {
            cargo.GetComponent<BoxCollider>().size = Vector3.one * 1.2f;
            cargo.transform.Find("Cube").localScale = Vector3.one * 1.2f;
            cargo.liftingPoint = Vector3.up * .6f;
            cargo.dragArea = Vector3.one * 1.44f;
            cargo.ConfigureBody();
        }
    }
}
