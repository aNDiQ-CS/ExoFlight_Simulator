using System.Collections.Generic;
using UnityEngine;

namespace ExoFlight.Flight
{
    public enum WeatherKind { Calm = 0, Breeze = 1, Dust = 2, Storm = 3 }

    /// <summary>
    /// Weather director for a level: picks the next weather by weight (not round-robin),
    /// holds it for a random time, blends smoothly, and lets scripted triggers push
    /// one-off weather ("a three minute storm") into an ordered queue.
    /// </summary>
    public sealed class WeatherDirector : MonoBehaviour
    {
        public AtmosphereController atmosphere;

        [Header("States (storm 0..1 per kind: Calm, Breeze, Dust, Storm)")]
        public float[] stormByKind = { .05f, .3f, .6f, 1f };

        [Header("Random weather")]
        [Tooltip("Relative odds for Calm, Breeze, Dust, Storm")]
        public float[] weights = { 4f, 3f, 2f, 1f };
        public float minHold = 60f;
        public float maxHold = 180f;
        [Tooltip("Seconds to blend from one weather to the next")]
        public float transition = 20f;
        [Tooltip("Weather pushed by a trigger blends in faster")]
        public float scriptedTransition = 4f;

        public WeatherKind Current { get; private set; }
        public float Storm { get; private set; }

        struct ScriptedWeather
        {
            public WeatherKind kind;
            public float seconds;
        }

        readonly Queue<ScriptedWeather> queue = new Queue<ScriptedWeather>();
        float targetStorm;
        float holdLeft;
        float blendSpeed = 1f;
        bool scriptedActive;

        void OnEnable()
        {
            if (atmosphere == null) return;
            Current = WeatherKind.Calm;
            Storm = atmosphere.storm;
            targetStorm = Storm;
            PickNext();
        }

        void Update()
        {
            if (atmosphere == null) return;
            float dt = Time.deltaTime;

            if (!scriptedActive && queue.Count > 0)
            {
                var w = queue.Dequeue();
                Current = w.kind;
                targetStorm = StormOf(w.kind);
                holdLeft = Mathf.Max(.1f, w.seconds);
                blendSpeed = 1f / Mathf.Max(.1f, scriptedTransition);
                scriptedActive = true;
            }
            else if (scriptedActive)
            {
                holdLeft -= dt;
                if (holdLeft <= 0f)
                {
                    scriptedActive = false;
                    transition = Mathf.Max(.1f, transition);
                    blendSpeed = 1f / transition;
                    PickNext();
                }
            }
            else
            {
                holdLeft -= dt;
                if (holdLeft <= 0f) PickNext();
            }

            Storm = Mathf.MoveTowards(Storm, targetStorm, blendSpeed * dt);
            atmosphere.storm = Storm;
        }

        /// <summary>Push weather into the queue; it plays after whatever is running.</summary>
        public void Enqueue(WeatherKind kind, float seconds) =>
            queue.Enqueue(new ScriptedWeather { kind = kind, seconds = Mathf.Max(.1f, seconds) });

        /// <summary>Shortcut for the usual case: a storm for N seconds.</summary>
        public void ForceStorm(float seconds) => Enqueue(WeatherKind.Storm, seconds);

        public int Pending => queue.Count;

        float StormOf(WeatherKind kind)
        {
            int i = Mathf.Clamp((int)kind, 0, stormByKind.Length - 1);
            return Mathf.Clamp01(stormByKind.Length > i ? stormByKind[i] : 0f);
        }

        void PickNext()
        {
            float total = 0f;
            for (int i = 0; i < weights.Length; i++) total += Mathf.Max(0f, weights[i]);

            WeatherKind picked = WeatherKind.Calm;
            if (total > 0f)
            {
                float roll = Random.value * total;
                float acc = 0f;
                for (int i = 0; i < weights.Length; i++)
                {
                    acc += Mathf.Max(0f, weights[i]);
                    if (roll <= acc) { picked = (WeatherKind)i; break; }
                }
            }

            Current = picked;
            targetStorm = StormOf(picked);
            holdLeft = Random.Range(Mathf.Min(minHold, maxHold), Mathf.Max(minHold, maxHold));
            blendSpeed = 1f / Mathf.Max(.1f, transition);
        }

        [ContextMenu("Force storm 180 s")]
        void DebugStorm() => ForceStorm(180f);

        [ContextMenu("Clear queue")]
        void DebugClear() => queue.Clear();
    }
}
