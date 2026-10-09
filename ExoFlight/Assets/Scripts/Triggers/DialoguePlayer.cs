using System.Collections;
using UnityEngine;
using UnityEngine.Events;

namespace ExoFlight.Triggers
{
    [RequireComponent(typeof(AudioSource))]
    public sealed class DialoguePlayer : MonoBehaviour
    {
        [Tooltip("Speaker and text; a future subtitle UI can subscribe here.")]
        [SerializeField] UnityEvent<string, string> lineStarted = new UnityEvent<string, string>();
        [SerializeField] UnityEvent finished = new UnityEvent();
        public bool IsPlaying { get; private set; }
        public bool CompletedNormally { get; private set; }
        public int CurrentLineIndex { get; private set; } = -1;
        public UnityEvent<string, string> LineStarted => lineStarted;
        AudioSource source;
        int playbackVersion;

        void Awake()
        {
            source = GetComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 0f;
        }

        public IEnumerator Play(DialogueDefinition dialogue)
        {
            Stop();
            int version = playbackVersion;
            IsPlaying = true;
            for (int i = 0; i < dialogue.lines.Count; i++)
            {
                var line = dialogue.lines[i];
                if (!isActiveAndEnabled || !source.isActiveAndEnabled || line.audio == null)
                {
                    Stop();
                    yield break;
                }
                CurrentLineIndex = i;
                lineStarted.Invoke(line.speaker, line.text);
                Debug.Log($"[Dialogue] {line.speaker}: {line.text}", this);
                source.clip = line.audio;
                source.Play();
                yield return null;
                while (source.isPlaying || AudioListener.pause)
                {
                    if (version != playbackVersion)
                        yield break;
                    yield return null;
                }
                if (version != playbackVersion)
                    yield break;
                yield return new WaitForSeconds(Mathf.Max(0f, line.pauseAfter));
                if (version != playbackVersion)
                    yield break;
            }
            IsPlaying = false;
            CompletedNormally = true;
            CurrentLineIndex = -1;
            finished.Invoke();
        }

        public void Stop()
        {
            playbackVersion++;
            if (source != null)
                source.Stop();
            IsPlaying = false;
            CompletedNormally = false;
            CurrentLineIndex = -1;
        }

        void OnDisable() => Stop();
    }
}
