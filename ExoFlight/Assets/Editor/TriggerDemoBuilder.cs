using System;
using System.Linq;
using ExoFlight.Triggers;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace ExoFlight.Editor
{
    public static class TriggerDemoBuilder
    {
        public const string ScenePath = "Assets/Scenes/TriggerTest.unity";
        const string DataPath = "Assets/TriggerDemo";

        [MenuItem("Tools/ExoFlight/Create Trigger Demo")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play Mode before creating the demo.");
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null)
            {
                EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath));
                return;
            }

            EnsureFolder(DataPath);
            EnsureFolder("Assets/Prefabs");
            var definition = ScriptableObject.CreateInstance<DialogueDefinition>();
            definition.lines.Add(new DialogueDefinition.Line {
                speaker = "Центр управления", text = "Проверка связи. Дрон, подтвердите готовность.",
                audio = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/TriggerDemo/Radio_A.wav"), pauseAfter = .4f });
            definition.lines.Add(new DialogueDefinition.Line {
                speaker = "Дрон", text = "Связь установлена. Готов к выполнению задания.",
                audio = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/TriggerDemo/Radio_B.wav"), pauseAfter = .2f });
            AssetDatabase.CreateAsset(definition, DataPath + "/DemoDialogue.asset");
            var dialogueAction = ScriptableObject.CreateInstance<DialogueAction>();
            SetReference(dialogueAction, "dialogue", definition);
            AssetDatabase.CreateAsset(dialogueAction, DataPath + "/01_Dialogue.asset");
            var logAction = ScriptableObject.CreateInstance<DebugLogAction>();
            var logProperties = new SerializedObject(logAction);
            logProperties.FindProperty("message").stringValue = "[Mission] Телеметрия получена. Теперь доступна новая контрольная точка.";
            logProperties.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.CreateAsset(logAction, DataPath + "/02_DebugLog.asset");
            var checkpointAction = ScriptableObject.CreateInstance<SetCheckpointAction>();
            AssetDatabase.CreateAsset(checkpointAction, DataPath + "/03_Checkpoint.asset");

            var previousScene = SceneManager.GetActiveScene();
            if (!AssetDatabase.CopyAsset("Assets/Scenes/WalkingTest.unity", ScenePath))
                throw new InvalidOperationException("Could not copy WalkingTest scene.");
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            try
            {
                var player = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<DesktopWalkingInput>()).Single();
                player.transform.SetPositionAndRotation(new Vector3(-8f, .03f, -9f), Quaternion.identity);
                player.gameObject.AddComponent<PlayerCheckpoint>();
                var sensor = new GameObject("Player Trigger Sensor");
                sensor.layer = 2; // Ignore Raycast, so XR rays do not hit their own sensor.
                sensor.transform.SetParent(player.transform, false);
                var capsule = sensor.AddComponent<CapsuleCollider>();
                capsule.isTrigger = true;
                capsule.height = 1.7f;
                capsule.radius = .3f;
                capsule.center = new Vector3(0f, .85f, 0f);
                var body = sensor.AddComponent<Rigidbody>();
                body.isKinematic = true;
                body.useGravity = false;
                sensor.AddComponent<PlayerTriggerSensor>();
                PrefabUtility.SaveAsPrefabAssetAndConnect(sensor, "Assets/Prefabs/PlayerTriggerSensor.prefab", InteractionMode.AutomatedAction);

                var mission = new GameObject("Trigger Mission - ordered steps");
                var source = mission.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 0f;
                source.volume = .65f;
                var dialogue = mission.AddComponent<DialoguePlayer>();
                var sequence = mission.AddComponent<TriggerSequence>();
                var zones = new[] {
                    MakeGate(mission.transform, "01 - Dialogue", -4f, new Color(.12f,.8f,.8f)),
                    MakeGate(mission.transform, "02 - Debug Log", 1f, new Color(1f,.55f,.12f)),
                    MakeGate(mission.transform, "03 - Checkpoint", 6f, new Color(.6f,.4f,1f)) };
                var checkpoint = new GameObject("Checkpoint - safe respawn").transform;
                checkpoint.SetParent(mission.transform);
                checkpoint.position = new Vector3(-8f, .03f, 9f);
                SetReference(zones[2], "checkpoint", checkpoint);
                var pad = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                pad.name = "Checkpoint Marker";
                pad.transform.SetParent(mission.transform);
                pad.transform.position = new Vector3(-8f, .012f, 9f);
                pad.transform.localScale = new Vector3(1.6f, .012f, 1.6f);
                Object.DestroyImmediate(pad.GetComponent<Collider>());
                pad.GetComponent<Renderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(DataPath + "/03 - Checkpoint.mat");

                var properties = new SerializedObject(sequence);
                properties.FindProperty("dialoguePlayer").objectReferenceValue = dialogue;
                var steps = properties.FindProperty("steps");
                steps.arraySize = 3;
                TriggerAction[] actions = { dialogueAction, logAction, checkpointAction };
                for (int i = 0; i < 3; i++)
                {
                    steps.GetArrayElementAtIndex(i).FindPropertyRelative("zone").objectReferenceValue = zones[i];
                    steps.GetArrayElementAtIndex(i).FindPropertyRelative("action").objectReferenceValue = actions[i];
                }
                properties.ApplyModifiedPropertiesWithoutUndo();
                EditorSceneManager.SaveScene(scene);
                AssetDatabase.SaveAssets();
                Debug.Log("Trigger demo created: " + ScenePath);
            }
            finally
            {
                SceneManager.SetActiveScene(previousScene);
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        static TriggerZone MakeGate(Transform parent, string name, float z, Color color)
        {
            var gate = new GameObject(name);
            gate.transform.SetParent(parent);
            gate.transform.position = new Vector3(-8f, 1.4f, z);
            var volume = gate.AddComponent<BoxCollider>();
            volume.size = new Vector3(2.6f, 2.8f, 1.2f);
            volume.isTrigger = true;
            var zone = gate.AddComponent<TriggerZone>();
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", .2f);
            AssetDatabase.CreateAsset(material, DataPath + "/" + name + ".mat");
            Beam(gate.transform, new Vector3(-1.4f, 0, 0), new Vector3(.15f, 2.8f, .25f), material);
            Beam(gate.transform, new Vector3(1.4f, 0, 0), new Vector3(.15f, 2.8f, .25f), material);
            Beam(gate.transform, new Vector3(0, 1.4f, 0), new Vector3(2.95f, .15f, .25f), material);
            var label = new GameObject("Label").AddComponent<TextMesh>();
            label.transform.SetParent(gate.transform, false);
            label.transform.localPosition = new Vector3(0, 1.85f, 0);
            label.text = name.ToUpperInvariant();
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.GetComponent<Renderer>().sharedMaterial = label.font.material;
            label.anchor = TextAnchor.MiddleCenter;
            label.alignment = TextAlignment.Center;
            label.fontSize = 48;
            label.characterSize = .07f;
            return zone;
        }

        static void Beam(Transform parent, Vector3 position, Vector3 scale, Material material)
        {
            var beam = GameObject.CreatePrimitive(PrimitiveType.Cube);
            beam.name = "Gate frame";
            beam.transform.SetParent(parent, false);
            beam.transform.localPosition = position;
            beam.transform.localScale = scale;
            beam.GetComponent<Renderer>().sharedMaterial = material;
            Object.DestroyImmediate(beam.GetComponent<Collider>());
        }

        static void SetReference(Object target, string field, Object value)
        {
            var properties = new SerializedObject(target);
            properties.FindProperty(field).objectReferenceValue = value;
            properties.ApplyModifiedPropertiesWithoutUndo();
        }

        static void EnsureFolder(string path)
        {
            if (!AssetDatabase.IsValidFolder(path))
                AssetDatabase.CreateFolder("Assets", path.Substring("Assets/".Length));
        }
    }
}
