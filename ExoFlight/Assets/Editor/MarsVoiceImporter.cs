using System;
using System.IO;
using System.Linq;
using ExoFlight.Missions.Mars;
using UnityEditor;
using UnityEngine;

namespace ExoFlight.EditorTools
{
    [InitializeOnLoad]
    public sealed class MarsVoiceImporter : AssetPostprocessor
    {
        public const string Folder = "Assets/MarsMission/Voice";
        public const string AudioFolder = Folder + "/Audio";
        public const string LibraryPath = Folder + "/MarsVoiceLibrary.asset";
        static bool scheduled;
        static double notBefore;
        [Serializable] sealed class Source { public MarsVoiceLibrary.Cue[] cues; }

        static MarsVoiceImporter()
        {
            // An import can coincide with script compilation; retry after every domain reload.
            Schedule();
        }

        static void Schedule()
        {
            notBefore = EditorApplication.timeSinceStartup + .2d;
            if (scheduled) return;
            scheduled = true;
            EditorApplication.update += WhenReady;
        }

        static void WhenReady()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.timeSinceStartup < notBefore) return;
            Rebind();
        }

        [MenuItem("ExoFlight/Марс: переподключить озвучку")]
        public static void Rebind()
        {
            scheduled = false;
            EditorApplication.update -= WhenReady;
            var library = AssetDatabase.LoadAssetAtPath<MarsVoiceLibrary>(LibraryPath);
            if (library == null) return;
            var clips = AssetDatabase.FindAssets("t:AudioClip", new[] { AudioFolder })
                .Select(AssetDatabase.GUIDToAssetPath).OrderBy(p => p, StringComparer.Ordinal).ToArray();
            bool changed = false;
            foreach (var cue in library.cues)
            {
                string expected = Path.GetFileNameWithoutExtension(cue.fileName);
                var matches = clips.Where(p => string.Equals(Path.GetFileNameWithoutExtension(p), expected, StringComparison.OrdinalIgnoreCase)).ToArray();
                // Multiple candidates are ambiguous; never silently choose a take or a duplicate.
                AudioClip clip = matches.Length == 1 ? AssetDatabase.LoadAssetAtPath<AudioClip>(matches[0]) : null;
                if (matches.Length > 1) Debug.LogWarning("[Mars VO] Несколько файлов для " + cue.id + ": оставьте один " + expected, library);
                if (cue.audio == clip) continue;
                cue.audio = clip; changed = true;
            }
            if (changed) { EditorUtility.SetDirty(library); AssetDatabase.SaveAssetIfDirty(library); }
        }

        public static MarsVoiceLibrary EnsureLibrary()
        {
            var source = AssetDatabase.LoadAssetAtPath<TextAsset>(Folder + "/Scenario.json");
            if (source == null) throw new InvalidOperationException("Missing Mars voice Scenario.json");
            var library = AssetDatabase.LoadAssetAtPath<MarsVoiceLibrary>(LibraryPath);
            if (library == null)
            {
                library = ScriptableObject.CreateInstance<MarsVoiceLibrary>();
                library.cues = JsonUtility.FromJson<Source>(source.text).cues.ToList();
                AssetDatabase.CreateAsset(library, LibraryPath);
            }
            Rebind();
            return library;
        }

        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            if (!imported.Concat(deleted).Concat(moved).Concat(movedFrom)
                .Any(p => p.StartsWith(AudioFolder + "/", StringComparison.OrdinalIgnoreCase))) return;
            Schedule();
        }
    }
}
