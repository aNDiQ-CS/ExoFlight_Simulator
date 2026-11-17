using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ExoFlight.Missions.Mars;
using ExoFlight.Triggers;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ExoFlight.EditorTools
{
    public static class MarsGateDesignChecks
    {
        public static string Result { get; private set; }
        [MenuItem("Tools/ExoFlight/Test Sparse Route Gates (Play Mode)")]
        public static void Run()
        {
            if (!Application.isPlaying) throw new InvalidOperationException("Enter Play Mode first.");
            var g = Object.FindFirstObjectByType<MarsRouteGuidance>();
            var log = new List<string>();
            string status = "PASS";
            try
            {
                Action<bool, string> check = (ok, text) => { log.Add((ok ? "PASS " : "FAIL ") + text); if (!ok) throw new Exception(text); };
                g.pilot.Stop(true);
                g.mission.drone.GetComponent<PlayerCheckpoint>().Respawn();
                g.Refresh();
                check(g.takeoffLabel == null && g.markers.All(m => m.label == null
                    && m.root.GetComponentsInChildren<TextMesh>(true).Length == 0), "All route captions removed, including takeoff and distances");
                check(g.VisibleCount == 2 && g.previewCount == 2, "Only the active gate and one upcoming gate are visible");
                check(g.markers.All(m => m.artwork != null && m.accents != null && m.artwork.GetComponentsInChildren<MeshFilter>().Length == 3),
                    "Shared frame, trim and light meshes are assigned");
                check(g.markers.All(m => m.root.GetComponentsInChildren<Collider>(true).Length == 0), "Gate artwork has no physics colliders");
                check(g.OutboundPresentation.Length < g.pilot.outbound.Length * .8f, "Outbound visual gates are substantially fewer than flight waypoints");
                check(g.LoadedPresentation.Length < g.pilot.loaded.Length, "Loaded-route visual gates are fewer than flight waypoints");
                for (int pass = 0; pass < 2; pass++)
                {
                    var route = pass == 0 ? g.pilot.outbound : g.pilot.loaded;
                    var indices = pass == 0 ? g.OutboundPresentation : g.LoadedPresentation;
                    check(indices.Last() == route.Length - 1, "Pickup/delivery endpoint is retained: " + pass);
                    float length = 0f;
                    for (int i = 1; i < indices.Length; i++)
                    {
                        check(indices[i] > indices[i - 1], "Ordered presentation indices: " + pass + "/" + i);
                        // Independently check new visual shortcuts against the actual map colliders.
                        if (indices[i] - indices[i - 1] > 1)
                            check(MarsRoutePresentation.ClearShortcut(route[indices[i - 1]].position, route[indices[i]].position),
                                "Terrain/cave clearance for shortcut: " + pass + "/" + i);
                        length += Vector3.Distance(route[indices[i - 1]].position, route[indices[i]].position);
                    }
                    int start = 0, first = pass == 0 ? 0 : 1, end = pass == 0 ? 1 : g.mission.zones.Length;
                    for (int gate = first; gate < end; gate++)
                    {
                        int nearest = Enumerable.Range(start, route.Length - start)
                            .OrderBy(i => (route[i].position - g.mission.zones[gate].transform.position).sqrMagnitude).First();
                        check(indices.Contains(nearest), "Story gate remains a visible navigation point: " + gate);
                        start = nearest + 1;
                    }
                    log.Add($"MEASURE route {pass}: {route.Length} flight points -> {indices.Length} visible-gate positions; mean spacing {length / (indices.Length - 1):F1} m");
                }
                foreach (int index in g.OutboundPresentation)
                {
                    g.mission.drone.Body.position = g.pilot.outbound[index].position;
                    g.Refresh();
                }
                check(g.mission.NextZoneIndex == 0 && Vector3.Distance(g.CurrentTarget, g.mission.zones[0].transform.position) < 3f,
                    "Sparse navigation waits at the real story gate and cannot skip mission progression");
            }
            catch (Exception ex) { status = "FAIL: " + ex.Message; }
            finally { g.mission.drone.GetComponent<PlayerCheckpoint>().Respawn(); }
            Result = status + "\n" + string.Join("\n", log);
            File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "../../docs/mars-gate-design-verification-2026-11-17.txt")), Result);
            Debug.Log("[Sparse route gates] " + status);
        }
    }
}
