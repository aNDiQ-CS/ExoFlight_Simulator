using UnityEngine;

namespace ExoFlight.Triggers
{
    /// <summary>Capsule sensor for a walking rig or a dynamic drone Rigidbody.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CapsuleCollider))]
    public sealed class PlayerTriggerSensor : MonoBehaviour
    {
        public PlayerCheckpoint Player { get; private set; }
        CapsuleCollider capsule;
        CharacterController character;

        void Awake()
        {
            Player = GetComponentInParent<PlayerCheckpoint>();
            capsule = GetComponent<CapsuleCollider>();
            capsule.isTrigger = true;
            EnsurePhysicsBody();
            character = GetComponentInParent<CharacterController>();
            if (Player == null)
                Debug.LogError("Player Trigger Sensor needs PlayerCheckpoint on its player root.", this);
        }

        void FixedUpdate()
        {
            if (character == null)
                return;
            // The sensor is an unscaled, identity child of the player root.
            // Follow the XR capsule, including room-scale head movement.
            capsule.center = character.center;
            capsule.height = character.height;
            capsule.radius = character.radius + 0.05f;
        }

        void Reset()
        {
            GetComponent<CapsuleCollider>().isTrigger = true;
            EnsurePhysicsBody();
        }

        void EnsurePhysicsBody()
        {
            // A drone's sensor is a compound trigger on its parent Rigidbody.
            // A walking rig keeps its existing independent kinematic sensor.
            var body = GetComponentInParent<Rigidbody>();
            if (body != null && body.gameObject != gameObject)
                return;
            if (body == null) body = gameObject.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
        }

    }
}
