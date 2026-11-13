using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ExoFlight.Cargo;
using ExoFlight.Cargo.UI;
using ExoFlight.Flight;
using ExoFlight.Triggers;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Object = UnityEngine.Object;

namespace ExoFlight.Editor
{
    [InitializeOnLoad]
    public static class CargoOnboardDisplayChecks
    {
        static CargoOnboardDisplay ui;
        static CargoMissionAssist assist;
        static KeyboardDroneInput input;
        static DroneCameraView cameraView;
        static bool originalInputEnabled, sawHold, sawPickup, sawTransit, sawRelease;
        static double started;
        static float completedAt;
        static readonly List<string> report = new List<string>();

        static CargoOnboardDisplayChecks() => EditorApplication.playModeStateChanged += state => {
            if (state == PlayModeStateChange.ExitingPlayMode && ui != null) Finish("Play Mode stopped early.");
        };

        [MenuItem("Tools/ExoFlight/Run Onboard Display Check (Play Mode)")]
        public static void Run()
        {
            if (!Application.isPlaying || ui != null) throw new InvalidOperationException("Run in Play Mode in CargoUIDemo.");
            ui = Object.FindAnyObjectByType<CargoOnboardDisplay>();
            if (ui == null) throw new InvalidOperationException("Install the onboard display first.");
            input = ui.mission.carrier.GetComponent<KeyboardDroneInput>();
            cameraView = ui.mission.carrier.GetComponent<DroneCameraView>();
            assist = ui.mission.carrier.GetComponent<CargoMissionAssist>();
            originalInputEnabled = input.enabled;
            input.enabled = false;
            sawHold = sawPickup = sawTransit = sawRelease = false;
            completedAt = -1f;
            report.Clear();
            report.Add("Onboard display | real scene Update/FixedUpdate | Unity " + Application.unityVersion + " | " + DateTime.UtcNow.ToString("O"));
            try
            {
                Require(!ui.Visible && !ui.TaskEngaged, "Task panel is hidden before the task");
                Require(ui.assembly.GetComponentsInChildren<MeshRenderer>(true).Length == 0,
                    "Active panel has no physical housing, rails or brackets");
                var archived = Object.FindAnyObjectByType<CargoUiDemo>(FindObjectsInactive.Include);
                Require(archived != null && !archived.gameObject.activeInHierarchy, "All four previous UI modes are retained but inactive");
                Require(Object.FindObjectsByType<Canvas>().All(c => c.renderMode == RenderMode.WorldSpace),
                    "No active screen-space UI or screen-edge effect remains");
                Require(ui.transform.IsChildOf(ui.mission.carrier.transform) && !ui.transform.IsChildOf(input.fpvCamera.transform),
                    "Terminal is parented to the drone body, not the camera");
                Require(ui.GetComponentsInChildren<Collider>(true).Length == 0, "Cosmetic display adds no collision or extra cargo physics");
                Require(!cameraView.ThirdPerson && !assist.useThirdPersonForDemo, "Scene and assisted preview use first person without a headset");
                Capture("01-before-task");
                var body = ui.mission.carrier.GetComponent<Rigidbody>();
                Vector3 start = body.position;
                body.position = ui.mission.deliveryZone.Volume.bounds.center;
                ui.RefreshPresentation(1f);
                Require(!ui.Visible, "Visiting delivery before pickup does not show an unrelated mission terminal");
                body.position = ui.mission.pickupZone.Volume.bounds.center;
                ui.RefreshPresentation(.1f);
                Require(ui.visibility.alpha > 0f && ui.visibility.alpha < 1f,
                    "Entering the task begins a gradual fade instead of an instant opaque popup");
                ui.RefreshPresentation(1f);
                Require(ui.Visible, "Entering the pickup volume accepts the task and reveals the terminal");
                body.position = start;
                ui.RefreshPresentation(1f);
                Require(ui.Visible, "An accepted task stays visible after leaving the pickup volume");
                ui.mission.carrier.GetComponent<PlayerCheckpoint>().Respawn();
                Require(!ui.Visible && !ui.TaskEngaged, "Reset hides hardware and clears task acceptance");
                assist.BeginDemo();
                Require(!cameraView.ThirdPerson, "Starting P demo preserves first-person view");
                started = EditorApplication.timeSinceStartup;
                EditorApplication.update += Observe;
            }
            catch (Exception e) { Finish(e.Message); }
        }

        static void Observe()
        {
            try
            {
                if (!Application.isPlaying || ui == null) throw new Exception("Scene stopped.");
                if (EditorApplication.timeSinceStartup - started > 90) throw new Exception("Mission timed out at " + assist.Stage);
                var mission = ui.mission;
                if (mission.State == CargoMissionState.Failed) throw new Exception("Physical mission failed.");
                ui.RefreshPresentation(0f);
                if (!sawHold && mission.State == CargoMissionState.AwaitingPickup && mission.HoldProgress > .6f)
                {
                    sawHold = true;
                    Require(ui.Visible && ui.progressFill.rectTransform.anchorMax.x > .3f
                        && ui.title.text == "Захват груза", "Actual pickup dwell drives the terminal status and progress");
                    Capture("02-pickup");
                    var corners = new Vector3[4];
                    ui.displayCanvas.GetComponent<RectTransform>().GetWorldCorners(corners);
                    var points = corners.Select(p => input.fpvCamera.WorldToViewportPoint(p)).ToArray();
                    float width = points.Max(p => p.x) - points.Min(p => p.x);
                    float height = points.Max(p => p.y) - points.Min(p => p.y);
                    Require(width * height < .06f && points.Max(p => p.y) < .5f,
                        "Panel occupies less than 6% of the viewport and leaves the centre unobstructed");
                    Require(ui.metric.cachedTextGenerator.vertexCount > 4,
                        "Countdown text generates visible glyphs without being clipped by its text rectangle");
                    CheckMouseLook();
                }
                if (!sawPickup && mission.State == CargoMissionState.Transporting)
                {
                    sawPickup = true;
                    Require(ui.Visible && mission.carrier.CurrentCargo == mission.cargo && mission.carrier.Drone.PayloadMass > 0f,
                        "Physical payload is attached and the display stays active");
                }
                if (sawPickup && !sawTransit && mission.State == CargoMissionState.Transporting
                    && !ui.InZone(mission.pickupZone) && !ui.InZone(mission.deliveryZone))
                {
                    sawTransit = true;
                    Require(ui.Visible && ui.title.text == "Груз на борту" && ui.metricUnit.text == "МЕТРОВ",
                        "Outside both zones, the active task shows delivery distance");
                    Capture("03-transit");
                }
                if (!sawRelease && mission.State == CargoMissionState.AwaitingLanding)
                {
                    sawRelease = true;
                    Require(ui.Visible && mission.carrier.CurrentCargo == null, "Terminal remains until the released load has actually landed");
                    Require(ui.metricUnit.text == "ПОСАДКА" && ui.progressFill.rectTransform.anchorMax.x == 1f,
                        "After release the terminal shows landing status, not a new pickup countdown");
                    Capture("04-landing");
                }
                if (mission.State != CargoMissionState.Completed) return;
                if (completedAt < 0f) { completedAt = Time.unscaledTime; return; }
                if (Time.unscaledTime - completedAt < ui.hideSeconds + .1f) return;
                Require(mission.cargo.IsDelivered && mission.cargo.HasGroundContact, "The assisted run completes actual physical delivery");
                Require(!ui.Visible && !ui.TaskEngaged && !ui.assembly.gameObject.activeSelf,
                    "Completion fades the panel out completely, with no lingering toast or housing");
                Capture("05-after-task");
                Require(sawHold && sawPickup && sawTransit && sawRelease, "Pickup, transit, release and completion were all observed");
                ui.mission.carrier.GetComponent<PlayerCheckpoint>().Respawn();
                Require(!ui.Visible && !cameraView.ThirdPerson, "Restart returns to a clean first-person view");
                report.Add("ALL PASSED");
                Finish(null);
            }
            catch (Exception e) { Finish(e.Message); }
        }

        static void CheckMouseLook()
        {
            var original = InputSystem.settings;
            var settings = Object.Instantiate(original);
            Mouse mouse = null;
            Vector3 position = ui.assembly.position;
            Quaternion rotation = ui.assembly.rotation;
            Quaternion cameraRotation = input.fpvCamera.transform.localRotation;
            try
            {
                settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
                settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
                InputSystem.settings = settings;
                mouse = InputSystem.AddDevice<Mouse>();
                InputSystem.EnableDevice(mouse);
                var state = new MouseState { delta = new Vector2(200f, 25f) }.WithButton(MouseButton.Right);
                InputSystem.QueueStateEvent(mouse, state);
                InputSystem.Update();
                cameraView.ReadMouseLook(mouse, .01f);
                cameraView.SetThirdPerson(false);
                Require(Quaternion.Angle(cameraRotation, input.fpvCamera.transform.localRotation) > 10f,
                    "Right mouse drag changes the desktop viewing direction");
                Require(Vector3.Distance(position, ui.assembly.position) < .0001f && Quaternion.Angle(rotation, ui.assembly.rotation) < .001f,
                    "Looking around leaves the display fixed to the body instead of following the gaze");
                Capture("06-look-aside");
                InputSystem.QueueStateEvent(mouse, new MouseState());
                InputSystem.Update();
                cameraView.ReadMouseLook(mouse, 1f);
                cameraView.SetThirdPerson(false);
                Require(cameraView.PreviewLook == Vector2.zero, "Releasing right mouse returns the preview view to centre");
            }
            finally
            {
                if (mouse != null) InputSystem.RemoveDevice(mouse);
                InputSystem.settings = original;
                Object.DestroyImmediate(settings);
            }
        }

        static void Capture(string name)
        {
            Directory.CreateDirectory("Temp/OnboardDisplay");
            var camera = input.fpvCamera;
            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;
            var target = RenderTexture.GetTemporary(1600, 900, 24);
            var image = new Texture2D(1600, 900, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = target;
                Canvas.ForceUpdateCanvases();
                camera.Render();
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0);
                image.Apply();
                File.WriteAllBytes("Temp/OnboardDisplay/" + name + ".png", image.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                RenderTexture.ReleaseTemporary(target);
                Object.DestroyImmediate(image);
            }
        }

        static void Require(bool condition, string message)
        {
            report.Add((condition ? "PASS " : "FAIL ") + message);
            if (!condition) throw new Exception(message);
        }

        static void Finish(string error)
        {
            EditorApplication.update -= Observe;
            if (error != null) report.Add("FAILED: " + error);
            if (assist != null) assist.StopAssist(true);
            if (input != null) input.enabled = originalInputEnabled;
            File.WriteAllLines("Temp/cargo-onboard-checks.txt", report);
            Debug.Log("[Onboard checks] " + string.Join("\n", report));
            ui = null;
        }
    }
}
