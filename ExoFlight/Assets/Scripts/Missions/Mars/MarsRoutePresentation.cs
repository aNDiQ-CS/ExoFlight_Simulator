using System.Collections.Generic;
using UnityEngine;

namespace ExoFlight.Missions.Mars
{
    /// <summary>Simplifies visual guidance only; all flight waypoints and story gates remain intact.</summary>
    public static class MarsRoutePresentation
    {
        public static int[] Select(Transform[] route, int[] required, int first, float spacing, float tolerance)
        {
            first = Mathf.Min(first, route.Length - 1);
            var result = new List<int> { first };
            int current = first;
            while (current < route.Length - 1)
            {
                int stop = route.Length - 1;
                foreach (int gate in required) if (gate > current) stop = Mathf.Min(stop, gate);
                int next = current + 1;
                float length = Vector3.Distance(route[current].position, route[next].position);
                for (int candidate = current + 2; candidate <= stop; candidate++)
                {
                    float extra = Vector3.Distance(route[candidate - 1].position, route[candidate].position);
                    if (length + extra > spacing * 1.5f || !FollowsPath(route, current, candidate, tolerance)
                        || !ClearShortcut(route[current].position, route[candidate].position)) break;
                    next = candidate; length += extra;
                    if (length >= spacing) break;
                }
                result.Add(next);
                current = next;
            }
            return result.ToArray();
        }

        public static bool ClearShortcut(Vector3 from, Vector3 to)
        {
            Vector3 delta = to - from;
            foreach (var hit in Physics.SphereCastAll(from, 1.25f, delta.normalized, delta.magnitude,
                ~0, QueryTriggerInteraction.Ignore))
                if (hit.rigidbody == null) return false;
            return true;
        }

        public static bool FollowsPath(Transform[] route, int from, int to, float tolerance)
        {
            Vector3 a = route[from].position, delta = route[to].position - a;
            for (int i = from + 1; i < to; i++)
            {
                float t = delta.sqrMagnitude < .0001f ? 0f : Mathf.Clamp01(Vector3.Dot(route[i].position - a, delta) / delta.sqrMagnitude);
                if (Vector3.Distance(route[i].position, a + delta * t) > tolerance) return false;
            }
            return true;
        }
    }
}
