using ExoFlight.Triggers;
using UnityEngine;

namespace ExoFlight.Missions.Mars
{
    [RequireComponent(typeof(BoxCollider))]
    public sealed class MarsMissionZone : MonoBehaviour
    {
        public MarsMissionDirector mission;
        public int order;
        public MarsMissionStage stage;
        public bool requiresCargo;
        public string[] voiceIds;
        public Color color = new Color(1f, .65f, .2f, .6f);
        public BoxCollider Volume => GetComponent<BoxCollider>();

        void Reset() => Volume.isTrigger = true;
        void OnTriggerEnter(Collider other) => Check(other);
        void OnTriggerStay(Collider other) => Check(other);
        void Check(Collider other)
        {
            if (mission == null || !other.TryGetComponent<PlayerTriggerSensor>(out var sensor)
                || !sensor.isActiveAndEnabled || other.attachedRigidbody != mission.drone.Body) return;
            if (Contains(mission.drone.Body.position)) mission.TryEnter(this);
        }

        public bool Contains(Vector3 position)
        {
            Vector3 p = transform.InverseTransformPoint(position) - Volume.center;
            Vector3 half = Volume.size * .5f;
            return Mathf.Abs(p.x) <= half.x && Mathf.Abs(p.y) <= half.y && Mathf.Abs(p.z) <= half.z;
        }

        void OnDrawGizmos()
        {
            Gizmos.color = color;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(Volume.center, Volume.size);
#if UNITY_EDITOR
            UnityEditor.Handles.Label(transform.position, name);
#endif
        }
    }
}
