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
    public static class CargoUiDemoBuilder
    {
        public const string ScenePath = "Assets/Scenes/CargoUIDemo.unity";
        const string AssetFolder = "Assets/CargoUiDemo";
        static readonly Color Panel = new Color(.035f, .055f, .075f, 1f);
        static Font font;

        [MenuItem("Tools/ExoFlight/Open Cargo UI Demo")]
        public static void Open()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Stop Play Mode and save current scene changes first.");
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null)
            {
                EditorSceneManager.OpenScene(ScenePath);
                if (Object.FindAnyObjectByType<CargoUiDemo>(FindObjectsInactive.Include) != null) return;
            }
            else
            {
                if (!AssetDatabase.CopyAsset(CargoDemoBuilder.ScenePath, ScenePath))
                    throw new InvalidOperationException("Create CargoMissionTest first.");
                EditorSceneManager.OpenScene(ScenePath);
            }
            var scene = SceneManager.GetActiveScene();
            var mission = Object.FindAnyObjectByType<CargoDeliveryMission>();
            if (mission == null) throw new InvalidOperationException("Source scene has no cargo mission.");
            var drone = mission.carrier.GetComponent<DroneFlightController>();
            // Work only in the new copy. Keep all mission references and physical settings intact.
            foreach (var root in scene.GetRootGameObjects())
                if (root != mission.gameObject && root != drone.gameObject
                    && root.GetComponent<Light>() == null && root.GetComponent<FlightSimulationClock>() == null)
                    Object.DestroyImmediate(root);
            mission.transform.position += new Vector3(28f, 0f, 15f);
            mission.deliveryZone.transform.position += Vector3.right * 11f;
            for (int i = 1; i < 5; i++)
            {
                var marker = mission.transform.Find("Route marker " + i);
                if (marker != null) marker.position = Vector3.Lerp(mission.pickupZone.transform.position,
                    mission.deliveryZone.transform.position, i / 5f) + Vector3.up * -.03f;
            }
            drone.transform.SetPositionAndRotation(new Vector3(0f, .4f, -8f), Quaternion.identity);
            var cameraView = drone.GetComponent<DroneCameraView>();
            cameraView.thirdPersonOffset = new Vector3(0f, 2.6f, -9f);
            cameraView.thirdPersonFov = 75f;
            cameraView.SetThirdPerson(true);
            PrefabUtility.RecordPrefabInstancePropertyModifications(cameraView);
            PrefabUtility.RecordPrefabInstancePropertyModifications(drone.transform);
            mission.holdSeconds = 1.5f;
            foreach (var zone in new[] { mission.pickupZone, mission.deliveryZone })
            {
                if (zone.worldLabel != null) Object.DestroyImmediate(zone.worldLabel.gameObject);
                zone.worldLabel = null;
            }

            if (!AssetDatabase.IsValidFolder(AssetFolder)) AssetDatabase.CreateFolder("Assets", "CargoUiDemo");
            font = AssetDatabase.LoadAssetAtPath<Font>("Assets/VRTemplateAssets/Fonts/Inter/Inter-Regular.ttf");
            if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            BuildGround();
            var view = drone.GetComponent<KeyboardDroneInput>().fpvCamera;
            view.clearFlags = CameraClearFlags.SolidColor;
            view.backgroundColor = new Color(.23f, .17f, .15f);
            PrefabUtility.RecordPrefabInstancePropertyModifications(view);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.55f, .48f, .43f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = .012f;
            RenderSettings.fogColor = view.backgroundColor;
            var light = Object.FindAnyObjectByType<Light>();
            light.transform.rotation = Quaternion.Euler(50f, -35f, 0f);
            light.color = new Color(1f, .87f, .75f);
            light.intensity = 1.6f;

            var rootUi = new GameObject("Cargo UI - choose Mode (1-4)");
            var demo = rootUi.AddComponent<CargoUiDemo>();
            demo.mission = mission;
            demo.playerCamera = view;
            demo.pickupCard = WorldCard(rootUi.transform, "Pickup - World Space Canvas", mission.pickupZone, view);
            demo.deliveryCard = WorldCard(rootUi.transform, "Delivery - World Space Canvas", mission.deliveryZone, view);
            demo.pickupCard.Show("01  /  PICKUP", "Подбор груза", "Удерживайте позицию · 1,5 с", 0f, demo.pickupColor);
            demo.deliveryCard.Show("02  /  DELIVERY", "Доставка груза", "Сначала заберите груз", 0f, demo.deliveryColor);

            var screen = Rect("Camera UI Canvas", rootUi.transform, Vector2.zero, Vector2.zero);
            var canvas = screen.gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = view;
            canvas.planeDistance = .5f;
            canvas.sortingOrder = 100;
            var scaler = screen.gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600f, 900f);
            scaler.matchWidthOrHeight = .5f;
            var edges = Rect("Screen edge feedback", screen, Vector2.zero, Vector2.zero);
            Stretch(edges);
            demo.screenFeedback = edges.gameObject.AddComponent<CargoScreenFeedback>();
            demo.screenFeedback.raycastTarget = false;
            demo.screenFeedback.color = Color.clear;
            edges.gameObject.SetActive(false);

            demo.modeCaption = Label("Mode", screen, new Vector2(350f, 32f), new Vector2(28f, -24f), 18);
            demo.modeCaption.rectTransform.anchorMin = demo.modeCaption.rectTransform.anchorMax = new Vector2(0f, 1f);
            demo.modeCaption.rectTransform.pivot = new Vector2(0f, 1f);
            demo.modeCaption.text = "01   /   WORLD SPACE";
            demo.modeCaption.color = new Color(.87f, .86f, .82f);
            var card = Rect("Context notification", screen, new Vector2(520f, 176f), new Vector2(0f, -155f));
            card.anchorMin = card.anchorMax = new Vector2(.5f, 1f);
            demo.cameraCard = Card(card);
            card.gameObject.SetActive(false);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = rootUi;
            SceneView.lastActiveSceneView?.LookAt(new Vector3(0f, 2f, 7f), Quaternion.Euler(24f, -25f, 0f), 25f);
            Debug.Log("[Cargo UI] CargoUIDemo ready. 1-4: presentation mode; P: existing assisted mission demo.");
        }

        static CargoUiCard WorldCard(Transform parent, string name, CargoHoverZone zone, Camera camera)
        {
            var rect = Rect(name, parent, new Vector2(520f, 176f), Vector2.zero);
            rect.position = zone.transform.position + Vector3.up * 6f;
            rect.localScale = Vector3.one * .01f;
            var canvas = rect.gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = camera;
            rect.gameObject.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 10f;
            return Card(rect);
        }

        static CargoUiCard Card(RectTransform root)
        {
            var card = root.gameObject.AddComponent<CargoUiCard>();
            var background = root.gameObject.AddComponent<Image>();
            background.color = Panel;
            background.raycastTarget = false;
            var accent = Rect("Accent", root, new Vector2(4f, 176f), new Vector2(-258f, 0f));
            card.accent = accent.gameObject.AddComponent<Image>();
            card.accent.raycastTarget = false;
            card.heading = Label("Heading", root, new Vector2(464f, 28f), new Vector2(0f, 52f), 20);
            card.status = Label("Status", root, new Vector2(464f, 36f), new Vector2(0f, 15f), 28);
            card.detail = Label("Detail", root, new Vector2(464f, 28f), new Vector2(0f, -24f), 18);
            card.detail.color = new Color(.64f, .71f, .76f);
            var track = Rect("Progress track", root, new Vector2(464f, 4f), new Vector2(0f, -63f));
            var image = track.gameObject.AddComponent<Image>();
            image.color = new Color(.16f, .22f, .27f);
            image.raycastTarget = false;
            var fill = Rect("Progress fill", track, Vector2.zero, Vector2.zero);
            Stretch(fill);
            fill.anchorMax = new Vector2(0f, 1f);
            card.progressFill = fill.gameObject.AddComponent<Image>();
            card.progressFill.raycastTarget = false;
            return card;
        }

        static RectTransform Rect(string name, Transform parent, Vector2 size, Vector2 position)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            return rect;
        }

        static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        static Text Label(string name, Transform parent, Vector2 size, Vector2 position, int fontSize)
        {
            var label = Rect(name, parent, size, position).gameObject.AddComponent<Text>();
            label.font = font;
            label.fontSize = fontSize;
            label.color = new Color(.95f, .97f, 1f);
            label.alignment = TextAnchor.MiddleLeft;
            label.raycastTarget = false;
            label.supportRichText = false;
            return label;
        }

        static void BuildGround()
        {
            var terrain = new GameObject("Cargo UI test range");
            var ground = Material("Mars Ground", new Color(.34f, .23f, .18f));
            var concrete = Material("Apron", new Color(.19f, .20f, .21f));
            var line = Material("Apron markings", new Color(.42f, .42f, .38f));
            Part("Ground", terrain.transform, new Vector3(0f, -.26f, 8f), new Vector3(70f, .5f, 70f), ground, true);
            Part("Cargo apron", terrain.transform, new Vector3(5.5f, -.025f, 6f), new Vector3(28f, .05f, 36f), concrete, true);
            for (int i = -1; i <= 1; i += 2)
                Part("Apron side marking", terrain.transform, new Vector3(5.5f + i * 13.5f, .005f, 6f), new Vector3(.08f, .01f, 35f), line, false);
            for (int i = 0; i < 8; i++)
            {
                float side = i % 2 == 0 ? -1f : 1f;
                var rock = Part("Boundary rock", terrain.transform, new Vector3(5.5f + side * (20f + i % 3), .5f, -12f + i * 5f),
                    new Vector3(2f + i % 3, 1f + (i % 2) * .7f, 2.4f), ground, true);
                rock.transform.rotation = Quaternion.Euler(0f, i * 37f, 8f);
            }
        }

        static Material Material(string name, Color color)
        {
            string path = AssetFolder + "/" + name + ".mat";
            var result = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (result != null) return result;
            result = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            result.color = color;
            result.SetFloat("_Smoothness", .1f);
            AssetDatabase.CreateAsset(result, path);
            return result;
        }

        static GameObject Part(string name, Transform parent, Vector3 position, Vector3 size, Material material, bool solid)
        {
            var part = GameObject.CreatePrimitive(PrimitiveType.Cube);
            part.name = name;
            part.transform.SetParent(parent, false);
            part.transform.position = position;
            part.transform.localScale = size;
            part.GetComponent<Renderer>().sharedMaterial = material;
            if (!solid) Object.DestroyImmediate(part.GetComponent<Collider>());
            return part;
        }
    }
}
