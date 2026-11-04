using System.Collections.Generic;
using ExoFlight.Triggers;
using UnityEngine;

namespace ExoFlight.Cargo
{
    [DisallowMultipleComponent, RequireComponent(typeof(BoxCollider))]
    public sealed class CargoHoverZone : MonoBehaviour
    {
        [Min(.01f)] public float maximumSpeed = 1.5f;
        [Min(.1f)] public float maximumAngularSpeed = 45f;
        [Range(0f, 80f)] public float maximumTilt = 35f;
        public Color gizmoColor = Color.cyan;
        public TextMesh worldLabel;
        public string label = "PICKUP";
        readonly HashSet<Collider> sensors = new HashSet<Collider>();
        BoxCollider volume;
        public BoxCollider Volume => volume != null ? volume : (volume = GetComponent<BoxCollider>());

        void Awake() => Volume.isTrigger = true;
        void Reset() => Volume.isTrigger = true;
        void OnDisable() => sensors.Clear();
        void OnTriggerEnter(Collider other) => Track(other);
        void OnTriggerStay(Collider other) => Track(other);
        void OnTriggerExit(Collider other) => sensors.Remove(other);
        void Track(Collider other)
        {
            if (other.TryGetComponent<PlayerTriggerSensor>(out var sensor) && sensor.isActiveAndEnabled)
                sensors.Add(other);
        }

        public bool IsHovering(DroneCargoCarrier carrier)
        {
            if (!isActiveAndEnabled || !Volume.enabled || carrier == null || !carrier.isActiveAndEnabled
                || carrier.Drone == null || !carrier.Drone.Armed) return false;
            bool hasSensor = false;
            sensors.RemoveWhere(c => c == null || !c.enabled || !c.gameObject.activeInHierarchy);
            foreach (var sensor in sensors)
                if (sensor.attachedRigidbody == carrier.Drone.Body) { hasSensor = true; break; }
            var body = carrier.Drone.Body;
            Vector3 local = transform.InverseTransformPoint(body.position) - Volume.center;
            Vector3 half = Volume.size * .5f;
            Vector3 up = -carrier.Drone.environment.gravity.normalized;
            return hasSensor && Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y
                && Mathf.Abs(local.z) <= half.z && body.linearVelocity.magnitude <= maximumSpeed
                && body.angularVelocity.magnitude * Mathf.Rad2Deg <= maximumAngularSpeed
                && Vector3.Angle(body.rotation * Vector3.up, up) <= maximumTilt;
        }

        public bool ContainsCargoFootprint(TransportableCargo cargo)
        {
            if (cargo == null) return false;
            bool found = false;
            foreach (var collider in cargo.GetComponentsInChildren<Collider>())
            {
                if (!collider.enabled || collider.isTrigger || collider.attachedRigidbody != cargo.Body) continue;
                found = true;
                Bounds bounds = collider.bounds;
                // Conservative world AABB corners: all cargo geometry must be above the pad.
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = bounds.center + Vector3.Scale(bounds.extents,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 p = transform.InverseTransformPoint(corner) - Volume.center;
                    if (Mathf.Abs(p.x) > Volume.size.x * .5f || Mathf.Abs(p.z) > Volume.size.z * .5f) return false;
                }
            }
            return found;
        }

        public void ShowStatus(string status)
        {
            if (worldLabel != null) worldLabel.text = label + "\n" + status;
        }

        void OnDrawGizmos()
        {
            Gizmos.color = gizmoColor;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(Volume.center, Volume.size);
        }
    }
}
