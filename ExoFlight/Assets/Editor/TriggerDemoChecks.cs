using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ExoFlight.Triggers;
using UnityEditor;
using Unity.EditorCoroutines.Editor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;

namespace ExoFlight.Editor
{
    /// <summary>Integration checks using real PhysX overlaps and the demo's actual assets.</summary>
    [InitializeOnLoad]
    public sealed class TriggerDemoChecks
    {
        const string Pending = "ExoFlight.TriggerChecks.Pending";
        readonly List<string> results = new List<string>();
        readonly List<string> speakers = new List<string>();
        PlayerCheckpoint player;
        TriggerSequence sequence;
        DialoguePlayer dialogue;
        TriggerZone[] zones;
        Keyboard testKeyboard;
        Vector3 thirdZonePosition;
        bool runInBackground;
        int missionLogCount;

        static TriggerDemoChecks()
        {
            EditorApplication.playModeStateChanged += state => {
                if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Pending, false))
                {
                    SessionState.SetBool(Pending, false);
                    new TriggerDemoChecks().StartChecks();
                }
            };
        }

        [MenuItem("Tools/ExoFlight/Run Trigger Demo Checks")]
        public static void RunFromMenu()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.GetActiveScene().path != TriggerDemoBuilder.ScenePath)
            {
                Debug.LogWarning("Open TriggerTest and stop Play Mode before running the checks.");
                return;
            }
            SessionState.SetBool(Pending, true);
            EditorApplication.isPlaying = true;
        }

        void StartChecks()
        {
            runInBackground = Application.runInBackground;
            Application.runInBackground = true;
            Application.logMessageReceived += OnLog;
            EditorCoroutineUtility.StartCoroutineOwnerless(Guarded());
        }

        IEnumerator Guarded()
        {
            var checks = Checks();
            while (true)
            {
                if (!EditorApplication.isPlaying) { Finish("Interrupted"); yield break; }
                object wait;
                try
                {
                    if (!checks.MoveNext()) break;
                    wait = checks.Current;
                }
                catch (Exception error)
                {
                    Finish("FAILED: " + error);
                    yield break;
                }
                yield return wait;
            }
            Finish("ALL PASSED");
        }

        IEnumerator Checks()
        {
            yield return new EditorWaitForSeconds(.3f);
            player = UnityEngine.Object.FindAnyObjectByType<PlayerCheckpoint>();
            sequence = UnityEngine.Object.FindAnyObjectByType<TriggerSequence>();
            dialogue = UnityEngine.Object.FindAnyObjectByType<DialoguePlayer>();
            zones = UnityEngine.Object.FindObjectsByType<TriggerZone>().OrderBy(zone => zone.name).ToArray();
            thirdZonePosition = zones[2].transform.position;
            Vector3 initialCheckpoint = player.Position;
            var sensor = player.GetComponentInChildren<PlayerTriggerSensor>();
            Check(sensor.GetComponent<CapsuleCollider>().isTrigger && sensor.GetComponent<Rigidbody>().isKinematic,
                "Player has a kinematic capsule trigger");
            dialogue.LineStarted.AddListener(RecordLine);

            sensor.enabled = false;
            Place(zones[0].transform.position);
            yield return new EditorWaitForSeconds(.2f);
            Check(!sequence.IsRunning && sequence.NextStepIndex == 0, "Solid player collider cannot activate a zone");
            Place(new Vector3(-8, .03f, -9));
            yield return new EditorWaitForSeconds(.2f);
            sensor.enabled = true;
            Place(zones[2].transform.position);
            yield return new EditorWaitForSeconds(.2f);
            Place(zones[1].transform.position);
            yield return new EditorWaitForSeconds(.2f);
            Check(sequence.NextStepIndex == 0 && player.Position == initialCheckpoint,
                "Early zones 3 and 2 cannot skip step 1 or change the checkpoint");

            Place(zones[0].transform.position);
            yield return new EditorWaitForSeconds(.2f);
            Check(sequence.IsRunning && dialogue.IsPlaying, "Entering zone 1 starts dialogue through physics");
            sequence.enabled = false;
            yield return null;
            Check(!dialogue.IsPlaying && sequence.NextStepIndex == 0, "Interrupted dialogue does not complete the step");
            sequence.enabled = true;
            speakers.Clear();
            Place(new Vector3(-8, .03f, -9));
            yield return new EditorWaitForSeconds(.2f);
            Place(zones[0].transform.position);
            yield return new EditorWaitForSeconds(.2f);
            Place(zones[1].transform.position);
            yield return new EditorWaitForSeconds(.2f);
            Check(sequence.NextStepIndex == 0 && sequence.IsRunning, "Next zone is locked during dialogue");
            float deadline = Time.realtimeSinceStartup + 8f;
            while (sequence.IsRunning && Time.realtimeSinceStartup < deadline)
                yield return null;
            Check(sequence.NextStepIndex == 1 && dialogue.CompletedNormally, "Dialogue completes before advancing");
            Check(speakers.SequenceEqual(new[] { "Центр управления", "Дрон" }), "Both audio lines play in order");
            yield return new EditorWaitForSeconds(.3f);
            Check(sequence.NextStepIndex == 1 && missionLogCount == 0, "Standing in an early zone does not activate it later");

            // Put the two instant-action zones on top of each other to test one-physics-frame entry.
            Place(new Vector3(-10, .03f, 1));
            zones[2].transform.position = zones[1].transform.position;
            Physics.SyncTransforms();
            yield return new EditorWaitForSeconds(.2f);
            Place(zones[1].transform.position);
            yield return new EditorWaitForSeconds(.3f);
            Check(sequence.NextStepIndex == 2 && missionLogCount == 1 && player.Position == initialCheckpoint,
                "Overlapping zones cannot complete two steps from one entry");
            yield return new EditorWaitForSeconds(.3f);
            Check(sequence.NextStepIndex == 2, "Next overlapping zone still requires a fresh entry");
            zones[2].transform.position = thirdZonePosition;
            Place(new Vector3(-10, .03f, 1));
            yield return new EditorWaitForSeconds(.2f);
            Place(zones[1].transform.position);
            yield return new EditorWaitForSeconds(.2f);
            Check(missionLogCount == 1, "Completed debug action does not repeat");
            Place(zones[2].transform.position);
            yield return new EditorWaitForSeconds(.3f);
            Check(sequence.IsComplete && Vector3.Distance(player.Position, zones[2].Checkpoint.position) < .01f,
                "Third trigger replaces the respawn checkpoint");

            Place(new Vector3(0, .03f, -8));
            player.Respawn();
            yield return new EditorWaitForSeconds(.2f);
            Check(Vector3.Distance(player.transform.position, player.Position) < .15f, "Respawn restores the checkpoint position");
            Place(new Vector3(4, -30, 0));
            yield return new EditorWaitForSeconds(.2f);
            Check(Vector3.Distance(player.transform.position, player.Position) < .15f, "Falling returns to the new checkpoint");

            Place(new Vector3(0, .03f, -8));
            testKeyboard = InputSystem.AddDevice<Keyboard>();
            Cursor.lockState = CursorLockMode.Locked;
            InputSystem.QueueStateEvent(testKeyboard, new KeyboardState(Key.R));
            yield return new EditorWaitForSeconds(.2f);
            Check(Vector3.Distance(player.transform.position, player.Position) < .15f, "R returns to the new checkpoint");
            Place(zones[0].transform.position);
            yield return new EditorWaitForSeconds(.3f);
            Check(sequence.IsComplete && !dialogue.IsPlaying && missionLogCount == 1, "Completed sequence cannot restart on reentry");
        }

        void Place(Vector3 position)
        {
            var character = player.GetComponent<CharacterController>();
            character.enabled = false;
            position.y = position.y < -20 ? position.y : .03f;
            player.transform.SetPositionAndRotation(position, Quaternion.identity);
            character.enabled = true;
            Physics.SyncTransforms();
        }

        void Check(bool condition, string description)
        {
            if (!condition) throw new InvalidOperationException(description);
            results.Add("PASS " + description);
        }

        void RecordLine(string speaker, string text) => speakers.Add(speaker);
        void OnLog(string message, string trace, LogType type)
        {
            if (message.StartsWith("[Mission]")) missionLogCount++;
        }

        void Finish(string status)
        {
            File.WriteAllText("Temp/TriggerChecks.results.txt", string.Join("\n", results) + "\n" + status);
            Debug.Log("Trigger checks: " + status);
            Cleanup();
            EditorApplication.isPlaying = false;
        }

        void Cleanup()
        {
            Application.logMessageReceived -= OnLog;
            Application.runInBackground = runInBackground;
            if (dialogue != null) dialogue.LineStarted.RemoveListener(RecordLine);
            if (testKeyboard != null && testKeyboard.added) InputSystem.RemoveDevice(testKeyboard);
            if (zones != null && zones.Length == 3 && zones[2] != null) zones[2].transform.position = thirdZonePosition;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

    }
}
