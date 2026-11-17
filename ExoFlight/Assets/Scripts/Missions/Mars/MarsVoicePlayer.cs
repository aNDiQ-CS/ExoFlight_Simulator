using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace ExoFlight.Missions.Mars
{
    /// <summary>One radio channel: story lines are queued, transient advice is never backlogged.</summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class MarsVoicePlayer : MonoBehaviour
    {
        public MarsVoiceLibrary library;
        public bool demoLinesOnly = true;
        [Min(.05f)] public float missingAudioSeconds = .5f;
        [Min(0f)] public float gapSeconds = .2f;
        public UnityEvent<string, string> lineStarted = new UnityEvent<string, string>();
        public string CurrentId { get; private set; }
        public bool IsBusy => current != null || pending.Count > 0;
        public int PlayedCount { get; private set; }
        public float LastLineTime { get; private set; }
        sealed class Pending { public MarsVoiceLibrary.Cue cue; public float readyAt; }
        readonly Queue<Pending> pending = new Queue<Pending>();
        readonly HashSet<string> spoken = new HashSet<string>();
        readonly Dictionary<string, float> lastHint = new Dictionary<string, float>();
        MarsVoiceLibrary.Cue current;
        AudioSource source;
        float remaining;
        bool hint, audioStarted;

        void Awake()
        {
            source = GetComponent<AudioSource>();
            source.playOnAwake = source.loop = false;
            source.spatialBlend = 0f;
        }

        public bool HasSpoken(string id) => spoken.Contains(id);

        public void Say(params string[] ids) => SayAfter(0f, ids);

        public void SayAfter(float delay, params string[] ids)
        {
            foreach (string id in ids)
            {
                var cue = library != null ? library.Find(id) : null;
                if (cue == null || (demoLinesOnly && !cue.demo) || spoken.Contains(id)) continue;
                if (hint) { source.Stop(); current = null; CurrentId = null; hint = false; }
                spoken.Add(id);
                pending.Enqueue(new Pending { cue = cue, readyAt = Time.time + Mathf.Max(0f, delay) });
            }
        }

        public bool Hint(string id, float cooldown = 25f)
        {
            if (IsBusy || Time.time - LastLineTime < 2f) return false;
            if (lastHint.TryGetValue(id, out float last) && Time.time - last < cooldown) return false;
            var cue = library != null ? library.Find(id) : null;
            if (cue == null || (demoLinesOnly && !cue.demo)) return false;
            lastHint[id] = Time.time;
            hint = true;
            Begin(cue);
            return true;
        }

        void Begin(MarsVoiceLibrary.Cue cue)
        {
            current = cue;
            CurrentId = cue.id;
            PlayedCount++;
            spoken.Add(cue.id);
            LastLineTime = Time.time;
            lineStarted.Invoke(cue.speaker, cue.text);
            Debug.Log($"[Mars VO {cue.id}] {cue.speaker}: {cue.text}" +
                (cue.audio == null ? $" [ожидается {cue.fileName}]" : ""), this);
            audioStarted = cue.audio != null;
            remaining = audioStarted ? gapSeconds : missingAudioSeconds + gapSeconds;
            if (audioStarted) { source.clip = cue.audio; source.Play(); }
        }

        void Update()
        {
            if (AudioListener.pause) return;
            if (current == null)
            {
                if (pending.Count > 0 && Time.time >= pending.Peek().readyAt) { hint = false; Begin(pending.Dequeue().cue); }
                return;
            }
            if (audioStarted && source.isPlaying) return;
            remaining -= Time.deltaTime;
            if (remaining > 0f) return;
            current = null;
            CurrentId = null;
            hint = false;
            LastLineTime = Time.time;
        }

        public void ResetPlayback()
        {
            if (source != null) source.Stop();
            pending.Clear(); spoken.Clear(); lastHint.Clear();
            current = null; CurrentId = null; hint = false;
            PlayedCount = 0; LastLineTime = Time.time;
        }

        void OnDisable() => ResetPlayback();
    }
}
