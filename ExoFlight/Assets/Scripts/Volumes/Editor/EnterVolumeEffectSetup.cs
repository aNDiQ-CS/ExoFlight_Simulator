using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ExoFlight.Volumes.EditorTools
{
    public static class EnterVolumeEffectSetup
    {
        const string MenuRoot = "Tools/ExoFlight/Enter Volume Effect/";
        const string ProfileFolder = "Assets/Volumes";
        const string ProfilePath = ProfileFolder + "/EnterVolumeEffect Profile.asset";

        [MenuItem(MenuRoot + "0. Собрать всё (feature + profile + volume)", priority = 0)]
        public static void SetupAll()
        {
            InstallRendererFeature();
            CreateProfile();
            CreateVolumeInScene();
        }

        [MenuItem(MenuRoot + "1. Установить Renderer Feature в URP", priority = 20)]
        public static void InstallRendererFeature()
        {
            var rp = GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset;
            if (rp == null)
            {
                Debug.LogError("[EnterVolumeEffect] активный render pipeline не URP asset");
                return;
            }

            int added = 0;
            for (int i = 0; i < rp.rendererDataList.Length; i++)
            {
                ScriptableRendererData data = rp.rendererDataList[i];
                if (data == null || data.rendererFeatures.Exists(f => f is EnterVolumeEffectFeature))
                    continue;

                var feature = ScriptableObject.CreateInstance<EnterVolumeEffectFeature>();
                feature.name = "Enter Volume Effect";

                AssetDatabase.AddObjectToAsset(feature, data);
                data.rendererFeatures.Add(feature);
                SyncFeatureMap(data);
                EditorUtility.SetDirty(data);
                added++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[EnterVolumeEffect] feature добавлен в {added} рендерер(ов)");
        }

        [MenuItem(MenuRoot + "2. Создать Volume Profile", priority = 21)]
        public static void CreateProfile()
        {
            VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
            if (profile == null)
            {
                if (!AssetDatabase.IsValidFolder(ProfileFolder))
                    AssetDatabase.CreateFolder("Assets", "Volumes");

                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, ProfilePath);
            }

            if (profile.components.Find(c => c is EnterVolumeEffectVolume) == null)
            {
                var component = profile.Add<EnterVolumeEffectVolume>(true);
                component.intensity.value = 1f;
                component.waveStrength.value = 0.03f;
                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssets();
            }

            EditorGUIUtility.PingObject(profile);
            Selection.activeObject = profile;
        }

        [MenuItem(MenuRoot + "3. Создать Volume в сцене", priority = 22)]
        public static void CreateVolumeInScene()
        {
            var go = new GameObject("Volume - Enter Effect");
            var volume = go.AddComponent<Volume>();

            var collider = go.AddComponent<BoxCollider>();
            collider.isTrigger = true;
            go.transform.localScale = new Vector3(8f, 3f, 8f);

            volume.isGlobal = false;
            volume.priority = 0f;
            volume.blendDistance = 3f;
            volume.sharedProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);

            Camera cam = Camera.main;
            go.transform.position = cam != null
                ? cam.transform.position + cam.transform.forward * 10f
                : new Vector3(0f, 1.5f, 10f);

            Undo.RegisterCreatedObjectUndo(go, "Create Enter Volume");
            Selection.activeGameObject = go;
            SceneView.lastActiveSceneView?.FrameSelected();
            EditorSceneManager.MarkSceneDirty(go.scene);
        }

        // Renderer Feature Map держит локальные id саб-ассетов — URP ждёт её в синхроне со списком
        static void SyncFeatureMap(ScriptableRendererData data)
        {
            var so = new SerializedObject(data);
            so.Update();

            SerializedProperty features = so.FindProperty("m_RendererFeatures");
            SerializedProperty map = so.FindProperty("m_RendererFeatureMap");
            if (features == null || map == null)
                return;

            map.arraySize = features.arraySize;
            for (int i = 0; i < features.arraySize; i++)
            {
                var feature = features.GetArrayElementAtIndex(i).objectReferenceValue;
                if (feature != null && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out _, out long localId))
                    map.GetArrayElementAtIndex(i).longValue = localId;
            }

            so.ApplyModifiedProperties();
        }
    }
}
