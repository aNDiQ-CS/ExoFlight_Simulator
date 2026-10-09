using UnityEngine;

namespace ExoFlight.Triggers
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider))]
    public sealed class TriggerZone : MonoBehaviour
    {
        [Tooltip("Only used by a Set Checkpoint action. Place outside the trigger volumes.")]
        [SerializeField] Transform checkpoint;
        TriggerSequence sequence;
        public Transform Checkpoint => checkpoint;

        internal void Bind(TriggerSequence owner) => sequence = owner;

        void Reset() => GetComponent<BoxCollider>().isTrigger = true;

        void OnTriggerEnter(Collider other)
        {
            // Ignore hands, props and the player's solid CharacterController.
            if (sequence != null && other.TryGetComponent<PlayerTriggerSensor>(out var sensor)
                && sensor.isActiveAndEnabled && sensor.Player != null)
                sequence.TryActivate(this, sensor.Player);
        }

        // Deliberately no OnTriggerStay: an early visit requires a new entry.
        void OnDrawGizmos()
        {
            var volume = GetComponent<BoxCollider>();
            Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.6f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(volume.center, volume.size);
        }
    }
}
