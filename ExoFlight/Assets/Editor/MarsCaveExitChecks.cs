using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ExoFlight.Missions.Mars;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ExoFlight.EditorTools
{
    public static class MarsCaveExitChecks
    {
        [MenuItem("Tools/ExoFlight/Check Cave Exit Geometry (Edit Mode)")]
        public static string Run()
        {
            if (Application.isPlaying) throw new InvalidOperationException("Use Edit Mode.");
            var mission = Object.FindFirstObjectByType<MarsMissionDirector>();
            var pilot = mission.drone.GetComponent<DroneMissionAutopilot>();
            var log = new List<string>();
            int failures = 0;
            Action<bool,string> check = (ok,message) => { log.Add((ok ? "PASS " : "FAIL ") + message); if (!ok) failures++; };
            Physics.SyncTransforms();
            check(AssetDatabase.GetAssetPath(Terrain.activeTerrain.terrainData).StartsWith("Assets/MarsMission/"),
                "The mission has its own terrain asset; the source Mars map is preserved.");
            check(mission.cargoMission.deliveryZone.transform.position.z > MarsCaveExitSetup.Mouth.z + 5f,
                "Delivery lies beyond the new cave exit.");
            check(Vector3.Distance(pilot.landing.position, mission.cargoMission.deliveryZone.transform.position) > 5f,
                "Drone landing remains separate from the rover touchdown position.");
            for (int i = 22; i < pilot.loaded.Length - 1; i++)
            {
                Vector3 a = pilot.loaded[i].position, b = pilot.loaded[i + 1].position;
                check(b.z >= a.z - .1f, "Forward cave route segment " + i + " does not return toward the entrance.");
                var hits = Physics.SphereCastAll(a, 2.5f, (b-a).normalized, Vector3.Distance(a,b), ~0, QueryTriggerInteraction.Ignore)
                    .Where(h => h.rigidbody == null).ToArray();
                check(hits.Length == 0, "2.5 m clearance on segment " + i + ": " + string.Join(",",hits.Select(h=>h.collider.name)));
            }
            int previous = -1;
            for (int gate = 1; gate < mission.zones.Length; gate++)
            {
                int closest = Enumerable.Range(0,pilot.loaded.Length).OrderBy(i =>
                    Vector3.Distance(pilot.loaded[i].position,mission.zones[gate].transform.position)).First();
                check(closest > previous && Vector3.Distance(pilot.loaded[closest].position,mission.zones[gate].transform.position)<3f,
                    "Story gate " + gate + " is on the ordered forward route.");
                previous = closest;
            }
            Vector3 oldPosition = mission.drone.transform.position;
            Vector3 oldGate = mission.zones[1].transform.position;
            try
            {
                Vector3 floor = mission.cargoMission.deliveryZone.transform.position;
                mission.drone.transform.position = floor + new Vector3(30f,40f,0f);
                check(Mathf.Abs(mission.display.DeliveryDistanceMeters-50f)<.001f, "Distance is 50 m for a 30 m horizontal / 40 m vertical offset.");
                mission.zones[1].transform.position = oldPosition + Vector3.one * 1000f;
                check(Mathf.Abs(mission.display.DeliveryDistanceMeters-50f)<.001f, "Moving a story checkpoint does not change delivery distance.");
                mission.drone.transform.position = floor + Vector3.up * 40f;
                check(Mathf.Abs(mission.display.DeliveryDistanceMeters-40f)<.001f, "Distance directly above the pad includes altitude, rather than showing zero.");
            }
            finally
            {
                mission.drone.transform.position = oldPosition;
                mission.zones[1].transform.position = oldGate;
                Physics.SyncTransforms();
            }
            Vector3 touchdown = mission.cargoMission.deliveryZone.transform.position;
            var ground = Physics.RaycastAll(touchdown + Vector3.up*2f, Vector3.down, 3f, ~0, QueryTriggerInteraction.Ignore)
                .Where(h=>h.rigidbody==null).OrderBy(h=>h.distance).ToArray();
            check(ground.Length > 0 && Mathf.Abs(ground[0].point.y-touchdown.y)<.05f, "Touchdown reference lies on the actual deck collider.");
            check(Mathf.Abs(Terrain.activeTerrain.SampleHeight(touchdown)-touchdown.y)<.3f, "Deck is set into a graded terrain apron.");
            string result = (failures==0 ? "PASS" : "FAIL "+failures) + "\n" + string.Join("\n",log);
            File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath,"../../docs/mars-cave-exit-geometry-"+DateTime.Now.ToString("yyyy-MM-dd")+".txt")),result);
            Debug.Log("[Mars cave exit checks] " + (failures==0 ? "PASS" : "FAIL"));
            return result;
        }
    }
}
