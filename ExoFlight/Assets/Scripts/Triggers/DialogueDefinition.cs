using System;
using System.Collections.Generic;
using UnityEngine;

namespace ExoFlight.Triggers
{
    [CreateAssetMenu(menuName = "ExoFlight/Dialogue")]
    public sealed class DialogueDefinition : ScriptableObject
    {
        [Serializable]
        public sealed class Line
        {
            public string speaker;
            [TextArea(2, 6)] public string text;
            public AudioClip audio;
            [Min(0f)] public float pauseAfter = 0.25f;
        }

        public List<Line> lines = new List<Line>();

        public bool IsValid(out string error)
        {
            if (lines.Count == 0)
            {
                error = "Dialogue has no lines.";
                return false;
            }
            for (int i = 0; i < lines.Count; i++)
                if (lines[i] == null || lines[i].audio == null || lines[i].audio.length <= 0f)
                {
                    error = $"Dialogue line {i + 1} needs an audio clip.";
                    return false;
                }
            error = null;
            return true;
        }
    }
}
