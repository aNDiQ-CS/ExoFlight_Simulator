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

        public bool IsActive() => intensity.value > 0.0001f;
    }
}
