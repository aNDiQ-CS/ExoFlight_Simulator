using UnityEngine;

[DisallowMultipleComponent]
public sealed class MarsStorm : MonoBehaviour
{
    private static readonly int DustCoverage = Shader.PropertyToID("_MarsDustCoverage");
    [SerializeField] private ParticleSystem dust;
    [SerializeField] private Transform observer;
    [SerializeField] private BoxCollider shelter;
    [SerializeField] private Light sun;
    [SerializeField, Min(0.1f)] private float buildupSeconds = 7f;

    private Color clearColor;
    private float clearDensity;
    private float clearSun;
    private float intensity;
    private bool requested;
    private bool initialized;

    public void SetObserver(Transform target) => observer = target;
    public void BeginStorm() => requested = true;
    public void EndStorm() => requested = false;

    private void OnEnable()
    {
        clearColor = RenderSettings.fogColor;
        clearDensity = RenderSettings.fogDensity;
        clearSun = sun ? sun.intensity : 0f;
        intensity = 0f;
        initialized = true;
    }

    private void LateUpdate()
    {
        if (!observer) return;

        bool sheltered = shelter && shelter.bounds.Contains(observer.position);
        float target = requested && !sheltered ? 1f : 0f;
        float duration = sheltered ? 2.5f : buildupSeconds;
        intensity = Mathf.MoveTowards(intensity, target, Time.deltaTime / duration);

        RenderSettings.fogDensity = Mathf.Lerp(clearDensity, 0.023f, intensity);
        RenderSettings.fogColor = Color.Lerp(clearColor, new Color(.64f, .33f, .20f), intensity);
        Shader.SetGlobalFloat(DustCoverage, intensity);
        if (sun) sun.intensity = Mathf.Lerp(clearSun, clearSun * .42f, intensity);

        if (!dust) return;
        dust.transform.position = observer.position;
        var emission = dust.emission;
        emission.rateOverTime = intensity * 72f;
        if (!sheltered && intensity > .015f && !dust.isPlaying)
            dust.Play();
        if ((sheltered || intensity <= .015f) && dust.isPlaying)
            dust.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    private void OnDisable()
    {
        if (!initialized) return;
        RenderSettings.fogColor = clearColor;
        RenderSettings.fogDensity = clearDensity;
        Shader.SetGlobalFloat(DustCoverage, 0f);
        if (sun) sun.intensity = clearSun;
        if (dust) dust.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
    }
}
