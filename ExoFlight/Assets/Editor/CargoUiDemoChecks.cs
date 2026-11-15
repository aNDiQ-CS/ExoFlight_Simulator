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
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace ExoFlight.Editor
{
    /// <summary>Observe the real assisted mission and its presentation, including event/reset lifetimes.</summary>
    [InitializeOnLoad]
    public static class CargoUiDemoChecks
    {
        static CargoUiDemo ui;
        static CargoMissionAssist assist;
        static KeyboardDroneInput input;
        static bool originalInputEnabled, sawHold, sawPickup, sawRelease, sawQuietTransit;
        static double started;
        static float pickedUpAt, finishedAt;
        static readonly List<string> report = new List<string>();

        static CargoUiDemoChecks() => EditorApplication.playModeStateChanged += state => {
            if (state == PlayModeStateChange.ExitingPlayMode && ui != null) Finish("Play Mode stopped early.");
        };

        [MenuItem("Tools/ExoFlight/Run Cargo UI Demo Check (Play Mode)")]
        public static void Run()
        {
            if (!Application.isPlaying || ui != null) throw new InvalidOperationException("Enter Play Mode in CargoUIDemo first.");
            ui = Object.FindAnyObjectByType<CargoUiDemo>();
            if (ui == null) throw new InvalidOperationException("Open CargoUIDemo.");
            assist = ui.mission.carrier.GetComponent<CargoMissionAssist>();
            input = ui.mission.carrier.GetComponent<KeyboardDroneInput>();
            originalInputEnabled = input.enabled;
            input.enabled = false; // Run without OS focus; restore at the end.
            report.Clear();
            report.Add("Cargo UI actual Update/FixedUpdate | Unity " + Application.unityVersion + " | " + DateTime.UtcNow.ToString("O"));
            sawHold = sawPickup = sawRelease = sawQuietTransit = false;
            finishedAt = 0f;
            try
            {
                Require(ui.mission.pickupZone.worldLabel == null && ui.mission.deliveryZone.worldLabel == null,
                    "Old text labels are removed from the new scene");
                Require(ui.GetComponentsInChildren<Text>(true).All(t => !t.text.Contains("H:") && !t.text.Contains("V:")),
                    "No H/V key prompts in UI");
                Require(ui.pickupCard.GetComponent<Canvas>().renderMode == RenderMode.WorldSpace
                    && ui.deliveryCard.GetComponent<Canvas>().renderMode == RenderMode.WorldSpace,
                    "Both destination cards use actual world-space canvases");
                Require(ui.cameraCard.GetComponentInParent<Canvas>().renderMode == RenderMode.ScreenSpaceCamera
                    && ui.cameraCard.GetComponentInParent<Canvas>().worldCamera == ui.playerCamera,
                    "Context UI is assigned to the player camera");
                Require(ui.pickupCard.transform.position.y > ui.mission.pickupZone.Volume.bounds.max.y,
                    "World labels are raised above the hover volumes");
                CheckKeyboardAndFacing();
                ui.SetMode(CargoUiMode.CameraNotice);
                Require(!ui.CameraNoticeVisible, "Camera notification is hidden outside mission approach areas");
                ui.SetMode(CargoUiMode.ScreenFeedback);
                Require(ui.screenFeedback.color.a == 0f, "Screen effect is clear before interaction");
                assist.BeginDemo();
                started = EditorApplication.timeSinceStartup;
                EditorApplication.update += Observe;
            }
            catch (Exception e) { Finish(e.Message); }
        }

        static void CheckKeyboardAndFacing()
        {
            var settings = InputSystem.settings;
            var testSettings = Object.Instantiate(settings);
            Keyboard keyboard = null;
            try
            {
                testSettings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
                testSettings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
                InputSystem.settings = testSettings;
                keyboard = InputSystem.AddDevice<Keyboard>();
                InputSystem.EnableDevice(keyboard);
                var keys = new[] { Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4 };
                for (int i = 0; i < keys.Length; i++)
                {
                    InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys[i]));
                    InputSystem.Update();
                    ui.ReadKeyboard(keyboard);
                    Require((int)ui.Mode == i + 1, "Number key selects UI mode " + (i + 1));
                }
            }
            finally
            {
                if (keyboard != null) InputSystem.RemoveDevice(keyboard);
                InputSystem.settings = settings;
                Object.DestroyImmediate(testSettings);
            }
            Transform camera = ui.playerCamera.transform;
            Vector3 position = camera.position;
            Quaternion rotation = camera.rotation;
            ui.SetMode(CargoUiMode.FixedWorld);
            Quaternion fixedRotation = ui.pickupCard.transform.rotation;
            camera.position += Vector3.right * 8f;
            ui.RefreshPresentation();
            Require(Quaternion.Angle(fixedRotation, ui.pickupCard.transform.rotation) < .01f,
                "Static world UI retains its orientation as the camera moves");
            ui.SetMode(CargoUiMode.BillboardWorld);
            Require(Vector3.Dot(ui.pickupCard.transform.forward, (ui.pickupCard.transform.position - camera.position).normalized) > .999f,
                "Billboard faces the camera from an oblique viewing position");
            ui.SetMode(CargoUiMode.FixedWorld);
            Require(Quaternion.Angle(fixedRotation, ui.pickupCard.transform.rotation) < .01f,
                "Returning from billboard restores the original fixed orientation");
            camera.SetPositionAndRotation(position, rotation);
        }

        static void Observe()
        {
            try
            {
                if (ui == null || !Application.isPlaying) throw new Exception("Scene stopped.");
                if (EditorApplication.timeSinceStartup - started > 90) throw new Exception("Mission timeout at " + assist.Stage);
                var mission = ui.mission;
                if (mission.State == CargoMissionState.Failed) throw new Exception("Physical delivery failed.");
                if (!sawHold && mission.State == CargoMissionState.AwaitingPickup && mission.HoldProgress > .45f)
                {
                    sawHold = true;
                    ui.SetMode(CargoUiMode.FixedWorld);
                    Require(ui.pickupCard.progressFill.rectTransform.anchorMax.x > .2f,
                        "World progress bar follows the real continuous pickup dwell");
                    Capture("01-world");
                    ui.SetMode(CargoUiMode.BillboardWorld);
                    Capture("02-billboard");
                    ui.SetMode(CargoUiMode.CameraNotice);
                    Require(ui.CameraNoticeVisible && !ui.pickupCard.gameObject.activeSelf,
                        "Camera UI appears on approach and replaces world cards");
                    Color cleanEdge = Capture("03-camera");
                    ui.SetMode(CargoUiMode.ScreenFeedback);
                    Require(ui.screenFeedback.color.a > .2f && !ui.CameraNoticeVisible,
                        "Screen edges light up during actual pickup dwell without a persistent panel");
                    Color tintedEdge = Capture("04-screen");
                    Require(tintedEdge.g > cleanEdge.g + .04f,
                        "Rendered screen pixels actually receive the cyan edge tint");
                }
                if (!sawPickup && mission.State == CargoMissionState.Transporting)
                {
                    sawPickup = true;
                    pickedUpAt = Time.unscaledTime;
                    ui.SetMode(CargoUiMode.ScreenFeedback);
                    Require(ui.CameraNoticeVisible && ui.cameraCard.status.text == "Груз закреплён"
                        && ui.screenFeedback.color.a > .4f,
                        "Physical pickup triggers the confirmation and screen flash");
                    Require(mission.carrier.CurrentCargo == mission.cargo && mission.carrier.Drone.PayloadMass > 0f,
                        "UI demo still attaches a physical payload with flight mass");
                    Capture("05-attached");
                    ui.SetMode(CargoUiMode.CameraNotice);
                }
                if (sawPickup && !sawQuietTransit && mission.State == CargoMissionState.Transporting
                    && Time.unscaledTime > pickedUpAt + ui.notificationSeconds + .2f
                    && Vector3.Distance(mission.carrier.Drone.Body.position, mission.deliveryZone.Volume.bounds.center) > 7f)
                {
                    sawQuietTransit = true;
                    ui.RefreshPresentation();
                    Require(!ui.CameraNoticeVisible, "Event notice expires during transit outside the next zone");
                }
                if (!sawRelease && mission.State == CargoMissionState.AwaitingLanding)
                {
                    sawRelease = true;
                    ui.SetMode(CargoUiMode.ScreenFeedback);
                    Require(mission.carrier.CurrentCargo == null && ui.CameraNoticeVisible,
                        "Release notification follows physical cargo detachment");
                }
                if (mission.State != CargoMissionState.Completed) return;
                if (finishedAt == 0f)
                {
                    finishedAt = Time.unscaledTime;
                    ui.RefreshPresentation();
                    Require(mission.cargo.HasGroundContact && ui.cameraCard.status.text == "Груз доставлен",
                        "Delivery success is shown only after cargo has landed");
                    Capture("06-delivered");
                    return;
                }
                if (Time.unscaledTime - finishedAt < ui.notificationSeconds + .2f) return;
                Require(!ui.CameraNoticeVisible && ui.screenFeedback.color.a == 0f,
                    "Completed event panel and screen tint disappear after their durations");
                Require(sawHold && sawPickup && sawRelease && sawQuietTransit, "All expected mission/UI phases were observed");
                assist.GetComponent<PlayerCheckpoint>().Respawn();
                ui.SetMode(CargoUiMode.CameraNotice);
                Require(mission.State == CargoMissionState.AwaitingPickup && !ui.CameraNoticeVisible
                    && ui.screenFeedback.color.a == 0f, "Reset clears both mission and stale UI feedback");
                ui.SetMode(CargoUiMode.FixedWorld);
                report.Add("ALL PASSED");
                Finish(null);
            }
            catch (Exception e) { Finish(e.Message); }
        }

        static Color Capture(string name)
        {
            Directory.CreateDirectory("Temp/CargoUI");
            Camera camera = ui.playerCamera;
            RenderTexture previousTarget = camera.targetTexture, previousActive = RenderTexture.active;
            var target = RenderTexture.GetTemporary(1600, 900, 24);
            var image = new Texture2D(1600, 900, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = target;
                Canvas.ForceUpdateCanvases();
                camera.Render();
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0f, 0f, 1600f, 900f), 0, 0);
                image.Apply();
                File.WriteAllBytes("Temp/CargoUI/" + name + ".png", image.EncodeToPNG());
                return image.GetPixel(12, 12);
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                RenderTexture.ReleaseTemporary(target);
                Object.DestroyImmediate(image);
            }
        }

        static void Require(bool condition, string name)
        {
            report.Add((condition ? "PASS " : "FAIL ") + name);
            if (!condition) throw new Exception(name);
        }

        static void Finish(string error)
        {
            EditorApplication.update -= Observe;
            if (error != null) report.Add("FAILED: " + error);
            if (assist != null) assist.StopAssist(true);
            if (input != null) input.enabled = originalInputEnabled;
            File.WriteAllLines("Temp/cargo-ui-checks.txt", report);
            Debug.Log("[Cargo UI checks] " + string.Join("\n", report));
            ui = null;
        }
    }
}
