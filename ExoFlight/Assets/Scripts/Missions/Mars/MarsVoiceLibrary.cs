using System;
using System.Collections.Generic;
using UnityEngine;

namespace ExoFlight.Missions.Mars
{
    [CreateAssetMenu(menuName = "ExoFlight/Missions/Mars voice library")]
    public sealed class MarsVoiceLibrary : ScriptableObject
    {
        [Serializable]
        public sealed class Cue
        {
            public string id, stage, condition, speaker, fileName;
            [TextArea(2, 6)] public string text;
            public bool demo = true;
            public float estimatedSeconds;
            public AudioClip audio;
        }

        public List<Cue> cues = new List<Cue>();
        public Cue Find(string id) => cues.Find(c => c.id == id);
    }
}
