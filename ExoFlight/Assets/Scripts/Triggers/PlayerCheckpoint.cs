using UnityEngine;

namespace ExoFlight.Triggers
{
    [DisallowMultipleComponent]
    public sealed class PlayerCheckpoint : MonoBehaviour
    {
        public Vector3 Position { get; private set; }
        public Quaternion Rotation { get; private set; }

        void Awake() => Save(transform);

        public void Save(Transform point)
        {
            Position = point.position;
            Rotation = point.rotation;
        }

        [ContextMenu("Respawn at checkpoint (Play Mode)")]
        public void Respawn()
        {
            if (!Application.isPlaying)
                return;

            var character = GetComponent<CharacterController>();
            bool restoreController = character != null && character.enabled;
            if (restoreController)
                character.enabled = false;

            transform.SetPositionAndRotation(Position, Rotation);
            if (TryGetComponent<Rigidbody>(out var body))
            {
                body.position = Position;
                body.rotation = Rotation;
                if (!body.isKinematic)
                {
                    body.linearVelocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                }
            }

            if (restoreController)
                character.enabled = true;
            Physics.SyncTransforms();
        }
    }
}
