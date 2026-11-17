using ExoFlight.Missions.Mars;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace ExoFlight.EditorTools
{
    public static class MarsRouteGuidanceSetup
    {
        public static void Install(MarsMissionDirector mission)
        {
            var guidance = mission.GetComponent<MarsRouteGuidance>();
            if (guidance == null) guidance = mission.gameObject.AddComponent<MarsRouteGuidance>();
            guidance.mission = mission;
            guidance.pilot = mission.drone.GetComponent<DroneMissionAutopilot>();
            guidance.targetSpacing = 36f;
            guidance.maximumShortcutError = 3f;
            guidance.previewCount = 2;
            guidance.activeMaterial = Material("Route active amber", new Color(1f, .58f, .08f), 2f, 0f);
            guidance.upcomingMaterial = Material("Route upcoming cyan", new Color(.12f, .48f, .55f), .7f, 0f);
            var frameMaterial = Material("Route frame graphite", new Color(.07f, .09f, .11f), .03f, .65f);
            var trimMaterial = Material("Route frame edge", new Color(.32f, .39f, .42f), .15f, .7f);
            var frameMesh = MarsCheckpointArt.Frame();
            var trimMesh = MarsCheckpointArt.Trim();
            var lightMesh = MarsCheckpointArt.Lights();
            if (guidance.markers == null || guidance.markers.Length == 0)
            {
                var group = new GameObject("Visible route checkpoints").transform;
                group.SetParent(mission.transform, false);
                guidance.markers = new MarsCheckpointVisual[3];
                for (int i = 0; i < guidance.markers.Length; i++)
                {
                    var root = new GameObject("Route gate " + i).transform;
                    root.SetParent(group, false);
                    guidance.markers[i] = new MarsCheckpointVisual { root = root };
                }
            }
            // Remove only our previous marker artwork and captions; the mission UI is separate.
            foreach (var marker in guidance.markers)
            {
                if (marker.ring != null) Object.DestroyImmediate(marker.ring.gameObject);
                if (marker.label != null) Object.DestroyImmediate(marker.label.gameObject);
                marker.ring = null; marker.label = null;
                if (marker.artwork == null)
                {
                    marker.artwork = new GameObject("Segmented race gate").transform;
                    marker.artwork.SetParent(marker.root, false);
                }
                Part(marker.artwork, "Graphite segments", frameMesh, frameMaterial);
                Part(marker.artwork, "Machined edge", trimMesh, trimMaterial);
                marker.accents = Part(marker.artwork, "Light rails and chevrons", lightMesh,
                    marker == guidance.markers[0] ? guidance.activeMaterial : guidance.upcomingMaterial);
                marker.artwork.localScale = Vector3.one * 4f;
            }
            if (guidance.takeoffLabel != null) Object.DestroyImmediate(guidance.takeoffLabel.gameObject);
            guidance.takeoffLabel = null;
            // Preview representative, separated points in Edit Mode too.
            var preview = MarsRoutePresentation.Select(guidance.pilot.outbound, new int[0], 2,
                guidance.targetSpacing, guidance.maximumShortcutError);
            for (int slot = 0; slot < guidance.markers.Length; slot++)
            {
                var root = guidance.markers[slot].root;
                root.gameObject.SetActive(slot < guidance.previewCount && slot < preview.Length);
                if (slot >= preview.Length) continue;
                int index = preview[slot];
                int previous = slot == 0 ? Mathf.Max(0, index - 1) : preview[slot - 1];
                root.position = guidance.pilot.outbound[index].position;
                root.rotation = Quaternion.LookRotation(root.position - guidance.pilot.outbound[previous].position);
            }
            var parked = GameObject.Find("Ares 07 | FPV Quadcopter");
            if (parked != null && parked.scene == mission.gameObject.scene
                && !parked.transform.IsChildOf(mission.drone.transform))
            {
                parked.name = "Archived parked Ares - launch view clearance";
                parked.SetActive(false);
            }
            if (mission.navigationBeacon != null)
            {
                mission.navigationBeacon.gameObject.SetActive(false);
                mission.navigationBeacon = null;
            }
            EditorUtility.SetDirty(guidance);
            EditorUtility.SetDirty(mission);
        }
        static MeshRenderer Part(Transform parent, string name, Mesh mesh, Material material)
        {
            var child = parent.Find(name);
            if (child == null)
            {
                child = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer)).transform;
                child.SetParent(parent, false);
            }
            child.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = child.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return renderer;
        }
        static Material Material(string name, Color color, float emission, float metallic)
        {
            string path = "Assets/MarsMission/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool create = material == null;
            if (create)
            {
                var source = AssetDatabase.LoadAssetAtPath<Material>("Assets/MarsLandscape/Ice blue signal.mat");
                material = new Material(source) { name = name };
            }
            material.SetColor("_BaseColor", color);
            material.SetColor("_EmissionColor", color * emission);
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Smoothness", .5f);
            material.EnableKeyword("_EMISSION");
            material.SetFloat("_Cull", 0f);
            if (create) AssetDatabase.CreateAsset(material, path);
            else { EditorUtility.SetDirty(material); AssetDatabase.SaveAssetIfDirty(material); }
            return material;
        }
    }
}
