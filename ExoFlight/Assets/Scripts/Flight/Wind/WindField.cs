using UnityEngine;

namespace ExoFlight.Flight
{
    /// <summary>Pure sampler: no scene objects, no allocation, no engine time.</summary>
    public static class WindField
    {
        // Measured by FlightWindChecks W4 over 1-4 octaves: mean |curl| of the potential is ~0.78,
        // so this factor makes gustSpeed the typical gust speed in m/s (peaks reach ~2.4x of it).
        const float CurlToSpeed = 1.29f;

        public static Vector3 Sample(WindProfile profile, Vector3 worldPos, float time)
        {
            if (profile == null) return Vector3.zero;

            Vector3 mean = profile.baseVelocity;
            if (mean.sqrMagnitude > .0001f && profile.wanderSpeed > 0f)
                mean += mean.normalized * (profile.wanderSpeed
                    * Mathf.Sin(6.2831853f * time / Mathf.Max(1f, profile.wanderPeriod)));

            Vector3 wind = mean * AltitudeFactor(profile, worldPos.y);
            if (profile.gustSpeed > 0f) wind += Gust(profile, worldPos, time);
            return wind * profile.speedScale;
        }

        static float AltitudeFactor(WindProfile profile, float altitude)
        {
            float t = Mathf.Pow(Mathf.Clamp01(Mathf.Max(0f, altitude) / profile.fullSpeedAltitude), .14f);
            return Mathf.Lerp(profile.groundSpeedFactor, 1f, t);
        }

        static Vector3 Gust(WindProfile profile, Vector3 position, float time)
        {
            Vector3 travel = profile.advectionSpeed > 0f
                ? profile.advectionSpeed * Vector3.Normalize(profile.baseVelocity.sqrMagnitude > .0001f
                    ? profile.baseVelocity : Vector3.forward)
                : profile.baseVelocity;
            Vector3 q = (position - travel * time) / profile.gustScale
                + new Vector3(profile.seed * .137f, profile.seed * .311f, profile.seed * .073f);

            const float e = .04f;
            Vector3 dx = (Potential(profile, q + new Vector3(e, 0f, 0f)) - Potential(profile, q - new Vector3(e, 0f, 0f))) / (2f * e);
            Vector3 dy = (Potential(profile, q + new Vector3(0f, e, 0f)) - Potential(profile, q - new Vector3(0f, e, 0f))) / (2f * e);
            Vector3 dz = (Potential(profile, q + new Vector3(0f, 0f, e)) - Potential(profile, q - new Vector3(0f, 0f, e))) / (2f * e);

            Vector3 curl = new Vector3(dy.z - dz.y, dz.x - dx.z, dx.y - dy.x);
            return curl * (profile.gustSpeed * CurlToSpeed);
        }

        static Vector3 Potential(WindProfile profile, Vector3 v) => new Vector3(
            Octaves(profile, v, 1),
            Octaves(profile, v + new Vector3(37.1f, 11.7f, 5.3f), 2),
            Octaves(profile, v + new Vector3(-13.9f, 29.3f, 47.7f), 3));

        static float Octaves(WindProfile profile, Vector3 v, int channel)
        {
            int count = Mathf.Clamp(profile.octaves, 1, 4);
            float sum = 0f, amplitude = 1f, frequency = 1f, total = 0f;
            for (int i = 0; i < count; i++)
            {
                sum += amplitude * WindNoise.Value(v * frequency, profile.seed + channel * 101 + i * 17);
                total += amplitude;
                amplitude *= .5f;
                frequency *= 2f;
            }
            return sum / total;
        }
    }
}
