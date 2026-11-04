using ExoFlight.Flight;
using ExoFlight.Triggers;
using UnityEngine;

namespace ExoFlight.Cargo
{
    [DefaultExecutionOrder(-50)]
    [DisallowMultipleComponent, RequireComponent(typeof(DroneFlightController))]
    public sealed class DroneCargoCarrier : MonoBehaviour
    {
        public Vector3 mountPoint = new Vector3(0f, -.18f, 0f);
        [Min(.1f)] public float maximumReach = 6.5f;
        [Min(.05f)] public float horizontalTolerance = 4.5f;
        [Min(0f)] public float minimumClearance;
        public DroneFlightController Drone { get; private set; }
        public TransportableCargo CurrentCargo { get; private set; }
        public Vector3 WorldMount => Drone != null && Drone.Body != null
            ? Drone.Body.position + Drone.Body.rotation * Vector3.Scale(mountPoint, transform.lossyScale)
            : transform.TransformPoint(mountPoint);
        ConfigurableJoint attachment;
        PlayerCheckpoint checkpoint;

        void Awake() => Drone = GetComponent<DroneFlightController>();
        void OnEnable()
        {
            if (Drone == null) Drone = GetComponent<DroneFlightController>();
            checkpoint = GetComponent<PlayerCheckpoint>();
            if (checkpoint != null) checkpoint.Respawned += OnRespawn;
        }

        public bool CanAttach(TransportableCargo cargo)
        {
            if (!isActiveAndEnabled || CurrentCargo != null || Drone == null || !Drone.Armed
                || cargo == null || !cargo.CanBePickedUp(this) || cargo.environment != Drone.environment) return false;
            Vector3 up = -Drone.environment.gravity.normalized;
            Vector3 delta = WorldMount - cargo.WorldLiftingPoint;
            float height = Vector3.Dot(delta, up);
            return height >= minimumClearance - .02f && delta.magnitude <= maximumReach
                && Vector3.ProjectOnPlane(delta, up).magnitude <= horizontalTolerance;
        }

        public bool TryAttach(TransportableCargo cargo)
        {
            if (!CanAttach(cargo)) return false;
            // Arcade capture from the marked area, followed by a real rigid dock.
            // The cargo's top mounts directly against the underside of the drone: no cable/strut.
            Vector3 dockedPosition = DockedPosition(cargo);
            cargo.Body.position = dockedPosition;
            cargo.Body.rotation = Drone.Body.rotation;
            cargo.transform.SetPositionAndRotation(dockedPosition, Drone.Body.rotation);
            Physics.SyncTransforms();
            cargo.Body.WakeUp();
            Drone.Body.WakeUp();
            // Keep the bodies' velocities. The constraint resolves the capture impulse.
            attachment = cargo.gameObject.AddComponent<ConfigurableJoint>();
            attachment.connectedBody = Drone.Body;
            // Auto anchors use the interpolated Transform and can pull a moving payload
            // back to an old rendered pose. Construct both anchors from physical poses.
            attachment.autoConfigureConnectedAnchor = false;
            attachment.anchor = cargo.liftingPoint;
            Vector3 localAnchor = Quaternion.Inverse(Drone.Body.rotation)
                * (cargo.WorldLiftingPoint - Drone.Body.position);
            Vector3 scale = transform.lossyScale;
            attachment.connectedAnchor = new Vector3(localAnchor.x / scale.x,
                localAnchor.y / scale.y, localAnchor.z / scale.z);
            SetMotion(ConfigurableJointMotion.Locked);
            attachment.enableCollision = false;
            attachment.massScale = attachment.connectedMassScale = 1f;
            attachment.projectionMode = JointProjectionMode.None;
            CurrentCargo = cargo;
            cargo.PickedUpBy(this);
            RefreshPayload();
            return true;
        }

        public TransportableCargo Release()
        {
            var cargo = CurrentCargo;
            if (attachment != null)
            {
                // Destroy is deferred; unlock first so no extra physics step retains the cargo.
                SetMotion(ConfigurableJointMotion.Free);
                attachment.enableCollision = true;
                Destroy(attachment);
                attachment = null;
            }
            CurrentCargo = null;
            if (Drone != null) Drone.SetPayload(0f, Vector3.zero);
            if (cargo != null) cargo.ReleasedBy(this);
            // Both bodies keep their physical linear and angular velocities on release.
            return cargo;
        }

        void SetMotion(ConfigurableJointMotion motion)
        {
            attachment.xMotion = attachment.yMotion = attachment.zMotion = motion;
            attachment.angularXMotion = attachment.angularYMotion = attachment.angularZMotion = motion;
        }

        public void RefreshPayload()
        {
            if (CurrentCargo == null || attachment == null)
            {
                if (CurrentCargo != null || Drone.PayloadMass > 0f) Release();
                return;
            }
            Vector3 localCom = Quaternion.Inverse(Drone.Body.rotation)
                * (CurrentCargo.Body.worldCenterOfMass - Drone.Body.position);
            Drone.SetPayload(CurrentCargo.Mass, localCom);
        }

        void FixedUpdate() => RefreshPayload();

        public Vector3 DockedPosition(TransportableCargo cargo) => WorldMount
            - Drone.Body.rotation * Vector3.Scale(cargo.liftingPoint, cargo.transform.lossyScale);

        void OnRespawn() => Release();
        void OnDisable()
        {
            if (checkpoint != null) checkpoint.Respawned -= OnRespawn;
            Release();
        }
    }
}
