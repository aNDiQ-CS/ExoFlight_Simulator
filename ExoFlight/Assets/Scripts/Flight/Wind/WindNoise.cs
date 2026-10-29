using UnityEngine;

namespace ExoFlight.Flight
{
    /// <summary>Deterministic 3D value noise in [0,1], allocation free.</summary>
    public static class WindNoise
    {
        public static float Value(Vector3 point, int seed)
        {
            int x0 = Mathf.FloorToInt(point.x), y0 = Mathf.FloorToInt(point.y), z0 = Mathf.FloorToInt(point.z);
            float tx = Fade(point.x - x0), ty = Fade(point.y - y0), tz = Fade(point.z - z0);
            float x00 = Mathf.Lerp(Hash(x0, y0, z0, seed), Hash(x0 + 1, y0, z0, seed), tx);
            float x10 = Mathf.Lerp(Hash(x0, y0 + 1, z0, seed), Hash(x0 + 1, y0 + 1, z0, seed), tx);
            float x01 = Mathf.Lerp(Hash(x0, y0, z0 + 1, seed), Hash(x0 + 1, y0, z0 + 1, seed), tx);
            float x11 = Mathf.Lerp(Hash(x0, y0 + 1, z0 + 1, seed), Hash(x0 + 1, y0 + 1, z0 + 1, seed), tx);
            return Mathf.Lerp(Mathf.Lerp(x00, x10, ty), Mathf.Lerp(x01, x11, ty), tz);
        }

        static float Fade(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);

        static float Hash(int x, int y, int z, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(z * 83492791)
                    ^ ((uint)seed * 2654435761u);
                h ^= h >> 13;
                h *= 0x5bd1e995u;
                h ^= h >> 15;
                return (h & 0x00FFFFFFu) / 16777215f;
            }
        }
    }
}
