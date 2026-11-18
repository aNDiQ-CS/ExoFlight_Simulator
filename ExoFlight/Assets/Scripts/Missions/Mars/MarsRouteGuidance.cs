using System;
using ExoFlight.Cargo;
using ExoFlight.Flight;
using UnityEngine;

namespace ExoFlight.Missions.Mars
{
    [Serializable]
    public sealed class MarsCheckpointVisual
    {
        public Transform root;
        public LineRenderer ring;
        public TextMesh label;
        public Transform artwork;
        public MeshRenderer accents;
    }

    /// <summary>Spatial route guidance only. Does not complete mission triggers, move bodies or control the drone.</summary>
    [DefaultExecutionOrder(350)]
    public sealed class MarsRouteGuidance : MonoBehaviour
    {
        public MarsMissionDirector mission;
        public DroneMissionAutopilot pilot;
        public Material activeMaterial, upcomingMaterial;
        public MarsCheckpointVisual[] markers;
        public TextMesh takeoffLabel;
        [Min(1f)] public float captureRadius = 4.5f;
        [Min(10f)] public float targetSpacing = 36f;
        [Min(.5f)] public float maximumShortcutError = 3f;
        [Range(1, 3)] public int previewCount = 2;
        public int[] OutboundPresentation { get; private set; }
        public int[] LoadedPresentation { get; private set; }
        public int CurrentIndex { get; private set; } = 2;
        public Vector3 CurrentTarget { get; private set; }
        public bool ShowingLoadedRoute { get; private set; }
        public int VisibleCount { get; private set; }
        Vector3 previousPosition;
        int[] outboundGates, loadedGates;
        const float PlatformClearance = 26f;
        MaterialPropertyBlock accentProperties;

        void Awake()
        {
            accentProperties = new MaterialPropertyBlock();
            outboundGates = MapGates(pilot.outbound, 0, 1);
            loadedGates = MapGates(pilot.loaded, 1, mission.zones.Length);
            OutboundPresentation = MarsRoutePresentation.Select(pilot.outbound, outboundGates, 2, targetSpacing, maximumShortcutError);
            LoadedPresentation = MarsRoutePresentation.Select(pilot.loaded, loadedGates, 0, targetSpacing, maximumShortcutError);
            mission.cargoMission.restarted.AddListener(ResetRoute);
            ResetRoute();
        }
        void OnDestroy() { if (mission != null) mission.cargoMission.restarted.RemoveListener(ResetRoute); }
        void ResetRoute()
        {
            CurrentIndex = Mathf.Min(2, pilot.outbound.Length - 1);
            ShowingLoadedRoute = false;
            previousPosition = mission.drone.transform.position;
        }
        int[] MapGates(Transform[] route, int first, int end)
        {
            int[] indices = new int[mission.zones.Length];
            for (int i = 0; i < indices.Length; i++) indices[i] = -1;
            int start = 0;
            for (int gate = first; gate < end; gate++)
            {
                float best = float.PositiveInfinity;
                for (int i = start; i < route.Length; i++)
                {
                    float distance = (route[i].position - mission.zones[gate].transform.position).sqrMagnitude;
                    if (distance < best) { best = distance; indices[gate] = i; }
                }
                // Searching only forward distinguishes the outward and return tunnel legs.
                start = Mathf.Max(start, indices[gate] + 1);
            }
            return indices;
        }
        void LateUpdate() => Refresh();
        public void Refresh()
        {
            if (mission == null || pilot == null || markers == null) return;
            if (takeoffLabel != null) takeoffLabel.gameObject.SetActive(false);
            if (mission.MissionComplete || mission.Stage == MarsMissionStage.Failed) { Hide(); return; }
            Vector3 position = mission.drone.Body.position;
            var state = mission.cargoMission.State;
            bool hasCargoRoute = state == CargoMissionState.Transporting || state == CargoMissionState.AwaitingLanding;
            if (hasCargoRoute != ShowingLoadedRoute && state != CargoMissionState.Completed)
            {
                ShowingLoadedRoute = hasCargoRoute;
                CurrentIndex = hasCargoRoute ? 0 : Mathf.Min(2, pilot.outbound.Length - 1);
                previousPosition = position;
            }
            if (state == CargoMissionState.Completed)
            {
                CurrentTarget = pilot.landing.position + Vector3.up * 1.5f;
                Hide();
                previousPosition = position;
                return;
            }
            var route = ShowingLoadedRoute ? pilot.loaded : pilot.outbound;
            int[] gates = ShowingLoadedRoute ? loadedGates : outboundGates;
            int[] presentation = ShowingLoadedRoute ? LoadedPresentation : OutboundPresentation;
            for (int i = 0; i < mission.NextZoneIndex; i++)
                if (gates[i] >= 0) CurrentIndex = Mathf.Max(CurrentIndex, gates[i]);
            int ordinal = OrdinalAtOrAfter(presentation, CurrentIndex);
            if (pilot.FollowingRoute && pilot.LoadedRoute == ShowingLoadedRoute)
                ordinal = OrdinalAtOrAfter(presentation, pilot.Waypoint);
            else
            {
                int stopAt = mission.NextZoneIndex < gates.Length && gates[mission.NextZoneIndex] >= 0
                    ? gates[mission.NextZoneIndex] : route.Length - 1;
                while (ordinal < presentation.Length - 1 && presentation[ordinal] < stopAt
                    && SegmentDistance(route[presentation[ordinal]].position, previousPosition, position) <= captureRadius)
                    ordinal++;
            }
            CurrentIndex = presentation[ordinal];
            CurrentTarget = route[CurrentIndex].position;
            VisibleCount = 0;
            for (int slot = 0; slot < markers.Length; slot++)
            {
                bool visible = slot < previewCount && ordinal + slot < presentation.Length;
                if (visible) visible = !NearPlatform(route[presentation[ordinal + slot]].position);
                markers[slot].root.gameObject.SetActive(visible);
                if (!visible) continue;
                int index = presentation[ordinal + slot];
                int previous = ordinal + slot > 0 ? presentation[ordinal + slot - 1] : Mathf.Max(0, index - 1);
                Vector3 direction = route[index].position - route[previous].position;
                Show(slot, route[index].position, direction);
                VisibleCount++;
            }
            previousPosition = position;
        }
        bool NearPlatform(Vector3 point)
        {
            var cargo = mission.cargoMission;
            return WithinClearance(point, cargo.pickupZone.transform.position)
                || WithinClearance(point, cargo.deliveryZone.transform.position);
        }
        static bool WithinClearance(Vector3 point, Vector3 pad)
        {
            float x = point.x - pad.x, z = point.z - pad.z;
            return x * x + z * z <= PlatformClearance * PlatformClearance;
        }
        static int OrdinalAtOrAfter(int[] indices, int value)
        {
            for (int i = 0; i < indices.Length; i++) if (indices[i] >= value) return i;
            return indices.Length - 1;
        }
        void Show(int slot, Vector3 point, Vector3 direction)
        {
            var marker = markers[slot];
            marker.root.gameObject.SetActive(true);
            if (direction.sqrMagnitude < .01f) direction = Vector3.forward;
            Vector3 up = Mathf.Abs(Vector3.Dot(direction.normalized, Vector3.up)) > .95f ? Vector3.forward : Vector3.up;
            marker.root.SetPositionAndRotation(point, Quaternion.LookRotation(direction.normalized, up));
            float radius = mission.Stage == MarsMissionStage.Cave || mission.Stage == MarsMissionStage.CaveExit ? 3.3f : 4f;
            if (marker.ring != null) marker.ring.gameObject.SetActive(false);
            if (marker.label != null) marker.label.gameObject.SetActive(false);
            if (marker.artwork == null || marker.accents == null) return;
            marker.artwork.localScale = Vector3.one * radius;
            var material = slot == 0 ? activeMaterial : upcomingMaterial;
            marker.accents.sharedMaterial = material;
            accentProperties.Clear();
            float glow = slot == 0 ? 1f + .08f * Mathf.Sin(Time.time * 2f) : 1f;
            accentProperties.SetColor("_EmissionColor", material.GetColor("_EmissionColor") * glow);
            marker.accents.SetPropertyBlock(accentProperties);
        }
        static float SegmentDistance(Vector3 point, Vector3 from, Vector3 to)
        {
            Vector3 delta = to - from;
            float t = delta.sqrMagnitude < .0001f ? 0f : Mathf.Clamp01(Vector3.Dot(point - from, delta) / delta.sqrMagnitude);
            return Vector3.Distance(point, from + delta * t);
        }
        void Hide()
        {
            VisibleCount = 0;
            if (takeoffLabel != null) takeoffLabel.gameObject.SetActive(false);
            foreach (var marker in markers) marker.root.gameObject.SetActive(false);
        }
        void OnDisable() { if (markers != null) Hide(); }
    }
}
