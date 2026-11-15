using UnityEngine;

namespace ExoFlight.Flight
{
    /// <summary>
    /// One knob for the weather: <see cref="storm"/> (0 = calm, 1 = full storm) drives
    /// the sky, the fog, the wind the drone feels and the dust it kicks up.
    /// Runtime clones of the sky material and the wind profile are used, so the assets
    /// on disk are never modified.
    /// </summary>
    public sealed class AtmosphereController : MonoBehaviour
    {
        public FlightEnvironment environment;
        public Material skyMaterial;
        public Light sun;
        public ParticleSystem stormDust;

        [Range(0f, 1f)] public float storm;

        [Header("Fog")]
        public Color calmFogColor = new Color(.52f, .45f, .40f, 1f);
        public Color stormFogColor = new Color(.62f, .42f, .26f, 1f);
        public float calmFogDensity = .0006f;
        public float stormFogDensity = .010f;

        [Header("Wind")]
        public float calmWindScale = .7f;
        public float stormWindScale = 1.6f;

        [Header("Dust")]
        public float stormDustRate = 220f;

        [Header("Sun")]
        public float calmSunIntensity = 1.3f;
        public float stormSunIntensity = .35f;

        Material runtimeSky;
        WindProfile runtimeProfile;
        WindProfile originalProfile;
        float appliedStorm = -1f;

        void OnEnable()
        {
            CloneAssets();
            Apply(true);
        }

        void OnDisable()
        {
            if (runtimeSky != null)
            {
                if (RenderSettings.skybox == runtimeSky) RenderSettings.skybox = skyMaterial;
                Destroy(runtimeSky);
                runtimeSky = null;
            }

            if (runtimeProfile != null)
            {
                if (environment != null) environment.windProfile = originalProfile;
                Destroy(runtimeProfile);
                runtimeProfile = null;
            }

            appliedStorm = -1f;
        }

        void Update() => Apply(false);

        void CloneAssets()
        {
            if (skyMaterial != null && runtimeSky == null)
            {
                runtimeSky = new Material(skyMaterial);
                runtimeSky.name = skyMaterial.name + " (runtime)";
                runtimeSky.hideFlags = HideFlags.DontSave;
                RenderSettings.skybox = runtimeSky;
            }

            if (environment != null && environment.windProfile != null && runtimeProfile == null)
            {
                originalProfile = environment.windProfile;
                runtimeProfile = Instantiate(originalProfile);
                runtimeProfile.name = environment.windProfile.name + " (runtime)";
                runtimeProfile.hideFlags = HideFlags.DontSave;
                environment.windProfile = runtimeProfile;
            }
        }

        void Apply(bool force)
        {
            float s = Mathf.Clamp01(storm);
            if (!force && Mathf.Abs(s - appliedStorm) < .0005f) return;
            appliedStorm = s;

            if (runtimeSky != null) runtimeSky.SetFloat("_Storm", s);

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = Mathf.Lerp(calmFogDensity, stormFogDensity, s);
            RenderSettings.fogColor = Color.Lerp(calmFogColor, stormFogColor, s);

            if (runtimeProfile != null)
                runtimeProfile.speedScale = Mathf.Lerp(calmWindScale, stormWindScale, s);

            if (sun != null) sun.intensity = Mathf.Lerp(calmSunIntensity, stormSunIntensity, s);

            if (stormDust != null)
            {
                var em = stormDust.emission;
                em.rateOverTimeMultiplier = stormDustRate * s;
            }
        }
    }
}
