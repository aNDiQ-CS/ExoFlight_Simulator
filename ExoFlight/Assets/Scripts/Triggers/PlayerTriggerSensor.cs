using UnityEngine;

namespace ExoFlight.Triggers
{
    /// <summary>A separate trigger body; the CharacterController still handles solid collisions.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CapsuleCollider), typeof(Rigidbody))]
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
            var body = GetComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
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
            var body = GetComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
        }
    }
}
