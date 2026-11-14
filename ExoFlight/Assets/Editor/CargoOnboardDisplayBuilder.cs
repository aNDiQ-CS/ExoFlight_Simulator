using System;
using ExoFlight.Cargo;
using ExoFlight.Cargo.UI;
using ExoFlight.Flight;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace ExoFlight.Editor
{
    public static class CargoOnboardDisplayBuilder
    {
        const string Folder = "Assets/CargoUiDemo/Onboard";
        static Font font;
        static readonly Color Ink = new Color(.86f, .88f, .85f);
        static readonly Color Muted = new Color(.5f, .57f, .57f);

        [MenuItem("Tools/ExoFlight/Use Onboard Cargo Display")]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Stop Play Mode and save scene changes first.");
            if (SceneManager.GetActiveScene().path != CargoUiDemoBuilder.ScenePath)
                EditorSceneManager.OpenScene(CargoUiDemoBuilder.ScenePath);
            var existing = Object.FindAnyObjectByType<CargoOnboardDisplay>(FindObjectsInactive.Include);
            if (existing != null)
            {
                if (existing.visibility == null)
                {
                    MakeCompact(existing);
                    EditorSceneManager.MarkSceneDirty(existing.gameObject.scene);
                    EditorSceneManager.SaveScene(existing.gameObject.scene);
                }
                Selection.activeGameObject = existing.gameObject;
                return;
            }
            var mission = Object.FindAnyObjectByType<CargoDeliveryMission>();
            if (mission == null) throw new InvalidOperationException("CargoUIDemo needs its cargo mission.");
            var drone = mission.carrier.gameObject;
            var input = drone.GetComponent<KeyboardDroneInput>();
            var cameraView = drone.GetComponent<DroneCameraView>();
            var oldUi = Object.FindAnyObjectByType<CargoUiDemo>(FindObjectsInactive.Include);
            if (oldUi != null)
            {
                oldUi.gameObject.name = "Archived - previous UI variants (disabled)";
                oldUi.gameObject.SetActive(false);
            }
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/CargoUiDemo", "Onboard");
            font = AssetDatabase.LoadAssetAtPath<Font>("Assets/VRTemplateAssets/Fonts/Inter/Inter-Regular.ttf");
            if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var housing = Material("Housing - graphite", new Color(.22f, .25f, .25f), .65f);
            var mount = Material("Mount - matte", new Color(.09f, .115f, .12f), .35f);
            var metal = Material("Fasteners", new Color(.37f, .39f, .38f), .85f);

            var root = new GameObject("Cargo Onboard Display");
            root.transform.SetParent(drone.transform, false);
            var display = root.AddComponent<CargoOnboardDisplay>();
            display.mission = mission;
            var assembly = new GameObject("Retractable terminal - visible during task").transform;
            assembly.SetParent(root.transform, false);
            assembly.localPosition = input.fpvCamera.transform.localPosition + new Vector3(0f, -.41f, 1.15f);
            assembly.localRotation = Quaternion.Euler(20f, 0f, 0f);
            display.assembly = assembly;
            var shell = new GameObject("Chamfered housing", typeof(MeshFilter), typeof(MeshRenderer));
            shell.transform.SetParent(assembly, false);
            shell.GetComponent<MeshFilter>().sharedMesh = HousingMesh();
            shell.GetComponent<Renderer>().sharedMaterial = housing;
            foreach (int sign in new[] { -1, 1 })
            {
                Part("Sliding support", assembly, new Vector3(sign * .29f, -.23f, -.41f), new Vector3(.025f, .025f, .88f), mount);
                Part("Lower hinge", assembly, new Vector3(sign * .29f, -.20f, .02f), new Vector3(.075f, .065f, .08f), metal);
                foreach (int vertical in new[] { -1, 1 })
                {
                    var screw = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    screw.name = "Recessed screw";
                    screw.transform.SetParent(assembly, false);
                    screw.transform.localPosition = new Vector3(sign * .443f, vertical * .19f, -.034f);
                    screw.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                    screw.transform.localScale = new Vector3(.013f, .002f, .013f);
                    screw.GetComponent<Renderer>().sharedMaterial = metal;
                    Object.DestroyImmediate(screw.GetComponent<Collider>());
                }
            }

            var screen = Rect("Instrument screen - World Space", assembly, new Vector2(960f, 400f), Vector2.zero);
            screen.localPosition = new Vector3(0f, 0f, -.036f);
            screen.localScale = Vector3.one * .00085f;
            var canvas = screen.gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = input.fpvCamera;
            screen.gameObject.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 4f;
            display.displayCanvas = canvas;
            var face = screen.gameObject.AddComponent<Image>();
            face.color = new Color(.035f, .052f, .057f);
            face.raycastTarget = false;
            display.statusLight = Image("Status lamp", screen, new Vector2(9f, 9f), new Vector2(-423f, 151f), display.activeColor);
            Label("Device", screen, "EXO / CARGO", new Vector2(400f, 38f), new Vector2(-177f, 150f), 24, Ink);
            display.phaseNumber = Label("Phase", screen, "01 / 03", new Vector2(200f, 38f), new Vector2(325f, 150f), 24, Muted);
            display.phaseNumber.alignment = TextAnchor.MiddleRight;
            Image("Header rule", screen, new Vector2(850f, 1.5f), new Vector2(0f, 111f), new Color(.2f, .25f, .25f));
            display.title = Label("Task status", screen, "Подбор груза", new Vector2(630f, 70f), new Vector2(-110f, 52f), 46, Ink);
            display.instruction = Label("Next action", screen, "Удерживайте позицию", new Vector2(635f, 42f), new Vector2(-107f, -12f), 25, Muted);
            Image("Metric divider", screen, new Vector2(1.5f, 100f), new Vector2(260f, 25f), new Color(.2f, .25f, .25f));
            display.metric = Label("Value", screen, "1,5", new Vector2(150f, 94f), new Vector2(350f, 48f), 60, display.activeColor);
            display.metric.alignment = TextAnchor.MiddleRight;
            display.metricUnit = Label("Units", screen, "СЕКУНД", new Vector2(150f, 35f), new Vector2(350f, -15f), 21, Muted);
            display.metricUnit.alignment = TextAnchor.MiddleRight;
            var track = Image("Hold progress", screen, new Vector2(850f, 8f), new Vector2(0f, -77f), new Color(.15f, .2f, .21f));
            var fill = Rect("Fill", track.transform, Vector2.zero, Vector2.zero);
            fill.anchorMin = Vector2.zero;
            fill.anchorMax = new Vector2(0f, 1f);
            fill.offsetMin = fill.offsetMax = Vector2.zero;
            display.progressFill = fill.gameObject.AddComponent<Image>();
            display.progressFill.raycastTarget = false;
            display.stages = new Text[3];
            string[] captions = { "01  ПОДБОР", "02  ПЕРЕВОЗКА", "03  ДОСТАВКА" };
            for (int i = 0; i < 3; i++)
                display.stages[i] = Label("Stage " + i, screen, captions[i], new Vector2(265f, 40f),
                    new Vector2(-292f + i * 292f, -143f), 22, i == 0 ? display.activeColor : Muted);
            assembly.gameObject.SetActive(false);

            cameraView.SetThirdPerson(false);
            cameraView.allowDesktopLook = true;
            input.cameraTilt = -8f;
            input.fieldOfView = 70f;
            input.fpvCamera.fieldOfView = 70f;
            input.fpvCamera.transform.localRotation = Quaternion.Euler(8f, 0f, 0f);
            drone.GetComponent<CargoMissionAssist>().useThirdPersonForDemo = false;
            PrefabUtility.RecordPrefabInstancePropertyModifications(cameraView);
            PrefabUtility.RecordPrefabInstancePropertyModifications(input);
            PrefabUtility.RecordPrefabInstancePropertyModifications(input.fpvCamera);
            PrefabUtility.RecordPrefabInstancePropertyModifications(input.fpvCamera.transform);
            MakeCompact(display);
            EditorSceneManager.MarkSceneDirty(root.scene);
            EditorSceneManager.SaveScene(root.scene);
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = root;
            Debug.Log("[Cargo UI] Compact spatial panel installed. Previous UI is archived and inactive. P: FPV demonstration.");
        }

        static void MakeCompact(CargoOnboardDisplay display)
        {
            var previous = display.assembly;
            var camera = display.mission.carrier.GetComponent<KeyboardDroneInput>().fpvCamera;
            previous.name = "Archived - physical terminal (disabled)";
            previous.gameObject.SetActive(false);
            var panel = new GameObject("Compact task panel - visible during task").transform;
            panel.SetParent(display.transform, false);
            Vector3 offset = new Vector3(.55f, -.68f, 1.2f);
            panel.localPosition = camera.transform.localPosition + offset;
            // Match the accepted scene's fixed spatial orientation; never follow the live camera.
            panel.localRotation = Quaternion.LookRotation(offset.normalized, Vector3.up);
            var screen = Object.Instantiate(display.displayCanvas.gameObject, panel, false);
            screen.name = "Task card - World Space";
            screen.SetActive(true);
            var rect = screen.GetComponent<RectTransform>();
            rect.localPosition = Vector3.zero;
            rect.localRotation = Quaternion.identity;
            rect.localScale = Vector3.one * .00085f;
            rect.sizeDelta = new Vector2(800f, 180f);
            screen.GetComponent<Image>().color = new Color(.035f, .052f, .057f, .78f);
            foreach (string name in new[] { "Device", "Phase", "Header rule", "Metric divider", "Stage 0", "Stage 1", "Stage 2" })
                screen.transform.Find(name).gameObject.SetActive(false);

            display.assembly = panel;
            display.displayCanvas = screen.GetComponent<Canvas>();
            display.visibility = panel.gameObject.AddComponent<CanvasGroup>();
            display.visibility.alpha = 0f;
            display.visibility.interactable = false;
            display.visibility.blocksRaycasts = false;
            display.deploySeconds = .45f;
            display.hideSeconds = .2f;
            display.title = screen.transform.Find("Task status").GetComponent<Text>();
            display.instruction = screen.transform.Find("Next action").GetComponent<Text>();
            display.metric = screen.transform.Find("Value").GetComponent<Text>();
            display.metricUnit = screen.transform.Find("Units").GetComponent<Text>();
            display.phaseNumber = screen.transform.Find("Phase").GetComponent<Text>();
            display.stages = new Text[0];
            Place(display.title.rectTransform, new Vector2(570f, 60f), new Vector2(-87f, 32f));
            display.title.fontSize = 42;
            Place(display.instruction.rectTransform, new Vector2(570f, 40f), new Vector2(-87f, -18f));
            display.instruction.fontSize = 24;
            Place(display.metric.rectTransform, new Vector2(116f, 64f), new Vector2(312f, 32f));
            display.metric.fontSize = 46;
            Place(display.metricUnit.rectTransform, new Vector2(116f, 34f), new Vector2(312f, -18f));
            display.metricUnit.fontSize = 18;
            display.statusLight = screen.transform.Find("Status lamp").GetComponent<Image>();
            Place(display.statusLight.rectTransform, new Vector2(3f, 180f), new Vector2(-398.5f, 0f));
            var track = screen.transform.Find("Hold progress").GetComponent<RectTransform>();
            Place(track, new Vector2(744f, 4f), new Vector2(0f, -65f));
            display.progressFill = track.Find("Fill").GetComponent<Image>();
            panel.gameObject.SetActive(false);
        }

        static void Place(RectTransform rect, Vector2 size, Vector2 position)
        {
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
        }

        static RectTransform Rect(string name, Transform parent, Vector2 size, Vector2 position)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            return rect;
        }

        static Text Label(string name, Transform parent, string text, Vector2 size, Vector2 position, int fontSize, Color color)
        {
            var label = Rect(name, parent, size, position).gameObject.AddComponent<Text>();
            label.font = font;
            label.text = text;
            label.fontSize = fontSize;
            label.color = color;
            label.alignment = TextAnchor.MiddleLeft;
            label.supportRichText = false;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.raycastTarget = false;
            return label;
        }

        static Image Image(string name, Transform parent, Vector2 size, Vector2 position, Color color)
        {
            var graphic = Rect(name, parent, size, position).gameObject.AddComponent<Image>();
            graphic.color = color;
            graphic.raycastTarget = false;
            return graphic;
        }

        static void Part(string name, Transform parent, Vector3 position, Vector3 scale, Material material)
        {
            var part = GameObject.CreatePrimitive(PrimitiveType.Cube);
            part.name = name;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = position;
            part.transform.localScale = scale;
            part.GetComponent<Renderer>().sharedMaterial = material;
            Object.DestroyImmediate(part.GetComponent<Collider>());
        }

        static Material Material(string name, Color color, float metallic)
        {
            string path = Folder + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.color = color;
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Smoothness", .3f);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        static Mesh HousingMesh()
        {
            string path = Folder + "/Terminal housing.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh != null) return mesh;
            float w = .48f, h = .23f, c = .035f;
            Vector2[] corners = { new Vector2(-w+c,-h), new Vector2(w-c,-h), new Vector2(w,-h+c), new Vector2(w,h-c),
                new Vector2(w-c,h), new Vector2(-w+c,h), new Vector2(-w,h-c), new Vector2(-w,-h+c) };
            var vertices = new System.Collections.Generic.List<Vector3>();
            var indices = new System.Collections.Generic.List<int>();
            for (int face = 0; face < 2; face++)
            for (int i = 1; i < 7; i++)
            {
                float z = face == 0 ? -.03f : .03f;
                Vector2 a = corners[0], b = corners[face == 0 ? i+1 : i], d = corners[face == 0 ? i : i+1];
                int start = vertices.Count;
                vertices.Add(new Vector3(a.x, a.y, z)); vertices.Add(new Vector3(b.x, b.y, z)); vertices.Add(new Vector3(d.x, d.y, z));
                indices.Add(start); indices.Add(start+1); indices.Add(start+2);
            }
            for (int i = 0; i < 8; i++)
            {
                Vector2 a = corners[i], b = corners[(i+1)%8];
                int start = vertices.Count;
                vertices.Add(new Vector3(a.x,a.y,-.03f)); vertices.Add(new Vector3(b.x,b.y,-.03f));
                vertices.Add(new Vector3(b.x,b.y,.03f)); vertices.Add(new Vector3(a.x,a.y,.03f));
                indices.AddRange(new[] { start, start+1, start+2, start, start+2, start+3 });
            }
            mesh = new Mesh { name = "Terminal chamfered housing" };
            mesh.SetVertices(vertices); mesh.SetTriangles(indices, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }
    }
}
