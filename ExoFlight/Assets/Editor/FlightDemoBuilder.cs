using System;
using System.Linq;
using ExoFlight.Flight;
using ExoFlight.Triggers;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace ExoFlight.Editor
{
    public static class FlightDemoBuilder
    {
        public const string ScenePath = "Assets/Scenes/FlightTest.unity";
        public const string ProfilePath = "Assets/FlightDemo/MarsQuadcopter.asset";
        public const string EnvironmentPath = "Assets/FlightDemo/MarsAtmosphere.asset";

        [MenuItem("Tools/ExoFlight/Open Flight Demo")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play Mode before opening the flight demo.");
            if (SceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Save the current scene before opening FlightTest.");
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null)
            {
                EditorSceneManager.OpenScene(ScenePath);
                return;
            }
            EnsureFolder("Assets/FlightDemo");
            EnsureFolder("Assets/Prefabs");
            DroneFlightProfile profile = LoadOrCreate<DroneFlightProfile>(ProfilePath);
            FlightEnvironment environment = LoadOrCreate<FlightEnvironment>(EnvironmentPath);
            if (!AssetDatabase.CopyAsset(TriggerDemoBuilder.ScenePath, ScenePath))
                throw new InvalidOperationException("Could not create FlightTest from TriggerTest.");
            var scene = EditorSceneManager.OpenScene(ScenePath);
            foreach (var root in scene.GetRootGameObjects())
                if (root.GetComponentInChildren<DesktopWalkingInput>(true) != null)
                    root.SetActive(false);
            // The original checkpoint was at the walking capsule's feet.
            foreach (var zone in Object.FindObjectsByType<TriggerZone>())
                if (zone.Checkpoint != null)
                    zone.Checkpoint.position += Vector3.up * .4f;

            var bodyMaterial = Material("Drone Body", new Color(.08f, .13f, .18f));
            var rotorMaterial = Material("Drone Rotors", new Color(.1f, .7f, .8f));
            var noseMaterial = Material("Drone Nose", new Color(1f, .45f, .08f));
            var groundMaterial = Material("Mars Practice Ground", new Color(.34f, .19f, .13f));
            var gridMaterial = Material("Practice Grid", new Color(.46f, .30f, .21f));
            var drone = new GameObject("Player Drone");
            drone.transform.position = new Vector3(-8f, .4f, -9f);
            var rigidbody = drone.AddComponent<Rigidbody>();
            var collision = drone.AddComponent<BoxCollider>();
            collision.size = new Vector3(2.1f, .3f, 2.1f);
            var collisionMaterial = new PhysicsMaterial("Drone Contact") {
                dynamicFriction = .5f, staticFriction = .6f, bounciness = 0f
            };
            AssetDatabase.CreateAsset(collisionMaterial, "Assets/FlightDemo/Drone Contact.physicMaterial");
            collision.sharedMaterial = collisionMaterial;
            drone.AddComponent<PlayerCheckpoint>();
            var controller = drone.AddComponent<DroneFlightController>();
            controller.profile = profile;
            controller.environment = environment;
            controller.ConfigureBody();

            Part("Body", drone.transform, Vector3.zero, new Vector3(.5f, .25f, .65f), bodyMaterial);
            Part("Nose", drone.transform, new Vector3(0, 0, .36f), new Vector3(.25f, .12f, .12f), noseMaterial);
            BuildRotorVisuals(controller, bodyMaterial, rotorMaterial);
            var sensor = new GameObject("Player Trigger Sensor");
            sensor.layer = 2;
            sensor.transform.SetParent(drone.transform, false);
            var capsule = sensor.AddComponent<CapsuleCollider>();
            capsule.radius = .35f;
            capsule.height = .7f;
            capsule.isTrigger = true;
            sensor.AddComponent<PlayerTriggerSensor>();

            var cameraObject = new GameObject("FPV Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.SetParent(drone.transform, false);
            cameraObject.transform.localPosition = new Vector3(0f, .23f, 1.15f);
            cameraObject.transform.localRotation = Quaternion.Euler(-10f, 0f, 0f);
            var camera = cameraObject.AddComponent<Camera>();
            camera.nearClipPlane = .03f;
            camera.farClipPlane = 500f;
            camera.fieldOfView = 85f;
            cameraObject.AddComponent<AudioListener>();
            var input = drone.AddComponent<KeyboardDroneInput>();
            input.fpvCamera = camera;
            PrefabUtility.SaveAsPrefabAssetAndConnect(drone, "Assets/Prefabs/PlayerDrone.prefab",
                InteractionMode.AutomatedAction);

            new GameObject("Flight Simulation - 100 Hz").AddComponent<FlightSimulationClock>();
            var arena = new GameObject("Flight Practice Area");
            Part("Ground", arena.transform, new Vector3(0f, -.3f, 0f),
                new Vector3(200f, .5f, 200f), groundMaterial, true);
            for (int x = -100; x <= 100; x += 10)
            {
                Part("Grid X " + x, arena.transform, new Vector3(x, -.045f, 0),
                    new Vector3(.06f, .008f, 200f), gridMaterial);
                Part("Grid Z " + x, arena.transform, new Vector3(0, -.045f, x),
                    new Vector3(200f, .008f, .06f), gridMaterial);
            }
            MakeGate(arena.transform, new Vector3(-8, 0, 25), rotorMaterial);
            MakeGate(arena.transform, new Vector3(8, 0, 50), noseMaterial);
            MakeGate(arena.transform, new Vector3(-8, 0, 75), rotorMaterial);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = drone;
            SceneView.lastActiveSceneView?.LookAt(drone.transform.position, Quaternion.Euler(20, -25, 0), 8f);
            Debug.Log("[Flight] Created FlightTest. Press Play, then Space to arm.");
        }

        /// <summary>Updates the generated drone prefab without rebuilding the user's scene.</summary>
        public static void UpgradeToQuadcopter()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Stop Play Mode and save scene changes before upgrading.");
            const string legacyPath = "Assets/FlightDemo/MarsHexacopter.asset";
            if (AssetDatabase.LoadAssetAtPath<DroneFlightProfile>(ProfilePath) == null)
            {
                string error = AssetDatabase.MoveAsset(legacyPath, ProfilePath);
                if (!string.IsNullOrEmpty(error)) throw new InvalidOperationException(error);
            }
            var profile = AssetDatabase.LoadAssetAtPath<DroneFlightProfile>(ProfilePath);
            profile.name = "MarsQuadcopter";
            EditorUtility.SetDirty(profile);
            const string prefabPath = "Assets/Prefabs/PlayerDrone.prefab";
            var root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                var controller = root.GetComponent<DroneFlightController>();
                if (controller == null) throw new InvalidOperationException("Drone controller missing from prefab.");
                controller.profile = profile;
                // Only the generated motor assemblies are replaced. Camera, sensor and body stay in place.
                for (int i = 1; i <= 6; i++)
                    foreach (string prefix in new[] { "Arm ", "Motor ", "Rotor " })
                    {
                        var part = root.transform.Find(prefix + i);
                        if (part != null) Object.DestroyImmediate(part.gameObject);
                    }
                BuildRotorVisuals(controller, Material("Drone Body", new Color(.08f, .13f, .18f)),
                    Material("Drone Rotors", new Color(.1f, .7f, .8f)));
                controller.ConfigureBody();
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            AssetDatabase.SaveAssets();
            // Prefab references retain their GUIDs, including the renamed flight profile.
            var scene = SceneManager.GetActiveScene();
            if (scene.path == ScenePath) EditorSceneManager.SaveScene(scene);
            Debug.Log("[Flight] Drone upgraded to four rotors in X layout.");
        }

        static void BuildRotorVisuals(DroneFlightController controller, Material bodyMaterial, Material rotorMaterial)
        {
            var profile = controller.profile;
            controller.rotorVisuals = new Transform[DroneFlightProfile.RotorCount];
            for (int i = 0; i < DroneFlightProfile.RotorCount; i++)
            {
                Vector3 p = profile.RotorPosition(i);
                var arm = Part("Arm " + (i + 1), controller.transform, p * .5f,
                    new Vector3(.06f, .06f, p.magnitude), bodyMaterial);
                arm.transform.localRotation = Quaternion.LookRotation(p);
                Part("Motor " + (i + 1), controller.transform, p,
                    new Vector3(.12f, .12f, .12f), bodyMaterial);
                var rotor = new GameObject("Rotor " + (i + 1)).transform;
                rotor.SetParent(controller.transform, false);
                rotor.localPosition = p + Vector3.up * .09f;
                Part("Blade", rotor, Vector3.zero,
                    new Vector3(profile.propellerDiameter, .018f, .055f), rotorMaterial);
                Part("Blade cross", rotor, Vector3.zero,
                    new Vector3(.055f, .018f, profile.propellerDiameter), rotorMaterial);
                controller.rotorVisuals[i] = rotor;
            }
            float span = Mathf.Sqrt(2f) * profile.armLength + profile.propellerDiameter;
            controller.GetComponent<BoxCollider>().size = new Vector3(span, .3f, span);
        }

        static void MakeGate(Transform parent, Vector3 position, Material material)
        {
            var gate = new GameObject("Practice gate").transform;
            gate.SetParent(parent, false);
            gate.localPosition = position;
            Part("Left", gate, new Vector3(-4f, 3f, 0), new Vector3(.25f, 6f, .4f), material, true);
            Part("Right", gate, new Vector3(4f, 3f, 0), new Vector3(.25f, 6f, .4f), material, true);
            Part("Top", gate, new Vector3(0, 6f, 0), new Vector3(8.25f, .25f, .4f), material, true);
        }

        static GameObject Part(string name, Transform parent, Vector3 position, Vector3 scale,
            Material material, bool solid = false)
        {
            var part = GameObject.CreatePrimitive(PrimitiveType.Cube);
            part.name = name;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = position;
            part.transform.localScale = scale;
            part.GetComponent<Renderer>().sharedMaterial = material;
            if (!solid) Object.DestroyImmediate(part.GetComponent<Collider>());
            return part;
        }

        static Material Material(string name, Color color)
        {
            string path = "Assets/FlightDemo/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", .2f);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var value = AssetDatabase.LoadAssetAtPath<T>(path);
            if (value != null) return value;
            value = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(value, path);
            return value;
        }

        static void EnsureFolder(string path)
        {
            if (!AssetDatabase.IsValidFolder(path))
                AssetDatabase.CreateFolder("Assets", path.Substring("Assets/".Length));
        }
    }

    [CustomEditor(typeof(DroneFlightController))]
    public sealed class DroneFlightControllerInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            if (!Application.isPlaying) return;
            var drone = (DroneFlightController)target;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Live telemetry", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Armed", drone.Armed.ToString());
            EditorGUILayout.LabelField("Throttle", drone.Input.Throttle.ToString("P1"));
            EditorGUILayout.LabelField("Hover throttle (still air)", drone.HoverThrottle.ToString("P1"));
            EditorGUILayout.LabelField("Speed", drone.Body.linearVelocity.magnitude.ToString("F2") + " m/s");
            EditorGUILayout.LabelField("Total thrust", drone.TotalThrust.ToString("F2") + " N");
            EditorGUILayout.LabelField("Motor saturation", drone.MotorsSaturated.ToString());
            for (int i = 0; i < DroneFlightController.MotorCount; i++)
                EditorGUILayout.LabelField("Motor " + (i + 1), drone.GetMotorRpm(i).ToString("F0") + " rpm");
            Repaint();
        }
    }
}
