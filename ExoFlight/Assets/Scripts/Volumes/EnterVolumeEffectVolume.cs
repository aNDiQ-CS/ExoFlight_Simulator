using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace ExoFlight.Volumes
{
    [Serializable]
    [VolumeComponentMenu("ExoFlight/Enter Volume Effect")]
    public sealed class EnterVolumeEffectVolume : VolumeComponent, IPostProcessComponent
    {
        [Tooltip("0 — эффект выключен")]
        public ClampedFloatParameter intensity = new ClampedFloatParameter(0f, 0f, 1f);

        public ColorParameter tint = new ColorParameter(Color.white, true, false, true);

        [Tooltip("Демо-параметр")]
        public ClampedFloatParameter waveStrength = new ClampedFloatParameter(0.03f, 0f, 0.25f);

        [Tooltip("Демо-параметр")]
        public MinFloatParameter waveFrequency = new MinFloatParameter(10f, 0f);

        public bool IsActive() => intensity.value > 0.0001f;
    }
}
