using System.Collections.Generic;
using ExoFlight.Flight;
using UnityEngine;

namespace ExoFlight.Cargo
{
    /// <summary>A dynamic payload. Subclasses provide behaviour; mass always belongs to this Rigidbody.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(Rigidbody))]
    public class TransportableCargo : MonoBehaviour
    {
        [Min(.01f)] public float massKg = .5f;
        public FlightEnvironment environment;
        [Tooltip("Cd times area in local XYZ, square metres.")]
        public Vector3 dragArea = new Vector3(.25f, .25f, .25f);
        public Vector3 liftingPoint = new Vector3(0f, .25f, 0f);

        public Rigidbody Body { get; private set; }
        public DroneCargoCarrier Carrier { get; private set; }
        public bool IsDelivered { get; private set; }
        public Vector3 WorldLiftingPoint => Body != null
            ? Body.position + Body.rotation * Vector3.Scale(liftingPoint, transform.lossyScale)
            : transform.TransformPoint(liftingPoint);
        public bool HasGroundContact => supports.Count > 0;
        public float Mass => Body != null ? Body.mass : massKg;

        readonly HashSet<Collider> supports = new HashSet<Collider>();
        Vector3 homePosition;
        Quaternion homeRotation;
        float windTime;

        protected virtual void Awake()
        {
            ConfigureBody();
            RememberHome();
        }

        public void ConfigureBody()
        {
            if (Body == null) Body = GetComponent<Rigidbody>();
            Body.mass = Mathf.Max(.01f, massKg);
            Body.useGravity = false;
            Body.isKinematic = false;
            Body.linearDamping = Body.angularDamping = 0f;
            Body.interpolation = RigidbodyInterpolation.Interpolate;
            Body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            Body.solverIterations = 16;
            Body.solverVelocityIterations = 8;
            Body.maxAngularVelocity = 30f;
            Body.ResetCenterOfMass();
            Body.ResetInertiaTensor();
        }

        public void RememberHome()
        {
            homePosition = Body != null ? Body.position : transform.position;
            homeRotation = Body != null ? Body.rotation : transform.rotation;
        }

        protected virtual void FixedUpdate() => StepPhysics(Time.fixedDeltaTime);

        public void StepPhysics(float dt)
        {
            if (Body == null || environment == null || dt <= 0f) return;
            if (!Mathf.Approximately(Body.mass, Mathf.Max(.01f, massKg))) ConfigureBody();
            windTime += dt;
            supports.RemoveWhere(c => c == null || !c.enabled || !c.gameObject.activeInHierarchy);
            Body.AddForce(environment.gravity * Body.mass);
            Vector3 wind = Carrier != null ? Carrier.Drone.WindAt(Body.position)
                : environment.windProfile != null ? WindField.Sample(environment.windProfile, Body.position, windTime)
                : environment.windVelocity;
            Vector3 v = Quaternion.Inverse(Body.rotation) * (Body.linearVelocity - wind);
            Vector3 drag = new Vector3(-v.x * Mathf.Abs(v.x) * Mathf.Max(0f, dragArea.x),
                -v.y * Mathf.Abs(v.y) * Mathf.Max(0f, dragArea.y),
                -v.z * Mathf.Abs(v.z) * Mathf.Max(0f, dragArea.z)) * (.5f * environment.airDensity);
            Body.AddRelativeForce(drag);
            // Same Euler term as the drone, expressed in this body's principal inertia frame.
            Quaternion principal = Body.rotation * Body.inertiaTensorRotation;
            Vector3 omega = Quaternion.Inverse(principal) * Body.angularVelocity;
            Body.AddTorque(principal * -Vector3.Cross(omega, Vector3.Scale(Body.inertiaTensor, omega)));
        }

        public virtual bool CanBePickedUp(DroneCargoCarrier carrier) =>
            isActiveAndEnabled && !IsDelivered && Carrier == null && Body != null && environment != null;

        internal void PickedUpBy(DroneCargoCarrier carrier)
        {
            Carrier = carrier;
            supports.Clear();
            OnPickedUp(carrier);
        }

        internal void ReleasedBy(DroneCargoCarrier carrier)
        {
            Carrier = null;
            OnReleased(carrier);
        }

        public void MarkDelivered()
        {
            if (IsDelivered || Carrier != null) return;
            IsDelivered = true;
            OnDelivered();
        }

        public void ResetToHome()
        {
            if (Carrier != null) Carrier.Release();
            ConfigureBody();
            Body.position = homePosition;
            Body.rotation = homeRotation;
            Body.linearVelocity = Body.angularVelocity = Vector3.zero;
            Body.WakeUp();
            IsDelivered = false;
            supports.Clear();
            windTime = 0f;
            OnReset();
        }

        protected virtual void OnPickedUp(DroneCargoCarrier carrier) { }
        protected virtual void OnReleased(DroneCargoCarrier carrier) { }
        protected virtual void OnDelivered() { }
        protected virtual void OnReset() { }

        protected virtual void OnDisable()
        {
            if (Carrier != null) Carrier.Release();
            supports.Clear();
        }

        void OnJointBreak(float force) { if (Carrier != null) Carrier.Release(); }
        void OnCollisionEnter(Collision collision) => TrackSupport(collision);
        void OnCollisionStay(Collision collision) => TrackSupport(collision);
        void OnCollisionExit(Collision collision) => supports.Remove(collision.collider);

        void TrackSupport(Collision collision)
        {
            Vector3 up = environment != null && environment.gravity.sqrMagnitude > .0001f
                ? -environment.gravity.normalized : Vector3.up;
            for (int i = 0; i < collision.contactCount; i++)
                if (Vector3.Dot(collision.GetContact(i).normal, up) > .5f)
                {
                    supports.Add(collision.collider);
                    return;
                }
            supports.Remove(collision.collider);
        }
    }
}
