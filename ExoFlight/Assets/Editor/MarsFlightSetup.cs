using ExoFlight.Flight;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ExoFlight.EditorTools
{
    /// <summary>
    /// Ставит летающий дрон в открытую сцену Марса и гасит кинематографичную
    /// камеру-превью, чтобы за MainCamera не спорили две камеры.
    /// Всё через Undo: Ctrl+Z откатывает.
    /// </summary>
    public static class MarsFlightSetup
    {
        private const string DronePrefabPath = "Assets/Prefabs/PlayerDrone.prefab";
        private const string SpawnName = "Drone Spawn";
        private const float SpawnLift = 1.2f;

        [MenuItem("ExoFlight/Марс: включить полёт в открытой сцене", false, 10)]
        public static void EnableFlight()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || string.IsNullOrEmpty(scene.path))
            {
                Debug.LogError("[Mars] Сначала открой сцену Assets/MarsLandscape/Mars Landscape.unity.");
                return;
            }

            var existing = Object.FindAnyObjectByType<DroneFlightController>(FindObjectsInactive.Include);
            if (existing != null)
            {
                Selection.activeGameObject = existing.gameObject;
                Debug.Log("[Mars] Дрон уже в сцене: «" + existing.gameObject.name + "». Ничего не меняю.");
                return;
            }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(DronePrefabPath);
            if (prefab == null)
            {
                Debug.LogError("[Mars] Не нашёл префаб " + DronePrefabPath);
                return;
            }

            Vector3 position = new Vector3(0f, 3f, 0f);
            Quaternion rotation = Quaternion.identity;
            var spawn = FindSpawn();
            if (spawn != null)
            {
                position = spawn.transform.position + spawn.transform.up * SpawnLift;
                rotation = spawn.transform.rotation;
            }
            else
            {
                Debug.LogWarning("[Mars] Объект «" + SpawnName + "» не найден — ставлю дрон в (0, 3, 0).");
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            Undo.RegisterCreatedObjectUndo(instance, "Марс: дрон");
            instance.transform.SetPositionAndRotation(position, rotation);
            instance.name = "Player Drone (Mars)";

            int muted = MutePreviewCameras(instance.transform);

            EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeGameObject = instance;

            Debug.Log("[Mars] Дрон поставлен в " + instance.transform.position +
                      ", камер-превью погашено: " + muted + ".\n" +
                      "Space — взвести/снять моторы, W/S — газ, A/D — рыскание, стрелки — тангаж/крен, " +
                      "F — Acro/Angle, R — сброс, F1 — переключить клавиатура/джойстик.\n" +
                      "Ctrl+S — сохранить сцену, Ctrl+Z — откатить этот шаг.");
        }

        [MenuItem("ExoFlight/Марс: убрать дрон и вернуть камеру превью", false, 11)]
        public static void DisableFlight()
        {
            Scene scene = SceneManager.GetActiveScene();
            int removed = 0;
            foreach (var drone in Object.FindObjectsByType<DroneFlightController>(FindObjectsInactive.Include))
            {
                Undo.DestroyObjectImmediate(drone.gameObject);
                removed++;
            }

            int restored = 0;
            foreach (var cam in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include))
            {
                var preview = cam.GetComponentInParent<MarsFlightPreview>();
                if (preview == null || preview.enabled) continue;

                Undo.RecordObject(preview, "Марс: вернуть превью");
                preview.enabled = true;
                Undo.RecordObject(cam, "Марс: вернуть камеру");
                cam.enabled = true;

                var listener = cam.GetComponent<AudioListener>();
                if (listener != null)
                {
                    Undo.RecordObject(listener, "Марс: вернуть слушателя");
                    listener.enabled = true;
                }

                restored++;
            }

            if (scene.IsValid()) EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log("[Mars] Убрано дронов: " + removed + ", возвращено камер превью: " + restored + ".");
        }

        private static GameObject FindSpawn()
        {
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include))
            {
                if (t.name == SpawnName) return t.gameObject;
            }

            return null;
        }

        private static int MutePreviewCameras(Transform drone)
        {
            int muted = 0;
            foreach (var cam in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include))
            {
                if (cam.transform.IsChildOf(drone)) continue;

                var preview = cam.GetComponentInParent<MarsFlightPreview>();
                if (preview == null) continue;

                Undo.RecordObject(preview, "Марс: погасить превью");
                preview.enabled = false;
                Undo.RecordObject(cam, "Марс: погасить камеру");
                cam.enabled = false;

                var listener = cam.GetComponent<AudioListener>();
                if (listener != null)
                {
                    Undo.RecordObject(listener, "Марс: погасить слушателя");
                    listener.enabled = false;
                }

                muted++;
            }

            return muted;
        }
    }
}
