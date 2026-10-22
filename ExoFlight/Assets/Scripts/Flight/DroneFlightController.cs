using ExoFlight.Triggers;
using UnityEngine;

namespace ExoFlight.Flight
{
    [DisallowMultipleComponent, RequireComponent(typeof(Rigidbody))]
    public sealed class DroneFlightController : MonoBehaviour
    {
        public const int MotorCount = DroneFlightProfile.RotorCount;
        public DroneFlightProfile profile;
        public FlightEnvironment environment;
        public DroneFlightMode mode = DroneFlightMode.Acro;
        public Transform[] rotorVisuals = new Transform[MotorCount];

        public Rigidbody Body { get; private set; }
        public FlightControlInput Input { get; private set; }
        public bool Armed { get; private set; }
        public float TotalThrust { get; private set; }
        public bool MotorsSaturated { get; private set; }
        public float HoverThrottle
        {
            get
            {
                if (profile == null || environment == null) return 0f;
                float maximum = profile.ThrustAtRpm(profile.maximumRpm, environment.airDensity);
                float idle = profile.ThrustAtRpm(Mathf.Min(profile.idleRpm, profile.maximumRpm), environment.airDensity);
                // Values above 1 intentionally indicate insufficient thrust to hover.
                return (profile.mass * environment.gravity.magnitude / MotorCount - idle) / Mathf.Max(.0001f, maximum - idle);
            }
        }
        public Vector3 RotorAngularMomentum { get; private set; }

        readonly float[] rpm = new float[MotorCount];
        readonly float[] motorThrust = new float[MotorCount];
        readonly float[] corrections = new float[MotorCount];
        Vector3 integral;
        PlayerCheckpoint checkpoint;

        void Awake()
        {
            Body = GetComponent<Rigidbody>();
            ConfigureBody();
        }

        void OnEnable()
        {
            checkpoint = GetComponent<PlayerCheckpoint>();
            if (checkpoint != null) checkpoint.Respawned += ResetFlight;
        }

        void OnDisable()
        {
            if (checkpoint != null) checkpoint.Respawned -= ResetFlight;
            ResetFlight();
        }

        public void ConfigureBody()
        {
            if (Body == null) Body = GetComponent<Rigidbody>();
            if (profile == null) return;
            Body.mass = Mathf.Max(.01f, profile.mass);
            Body.centerOfMass = profile.centerOfMass;
            Body.inertiaTensorRotation = Quaternion.identity;
            Body.inertiaTensor = new Vector3(Mathf.Max(.001f, profile.principalInertia.x),
                Mathf.Max(.001f, profile.principalInertia.y), Mathf.Max(.001f, profile.principalInertia.z));
            Body.useGravity = false;
            Body.linearDamping = 0f;
            Body.angularDamping = 0f;
            Body.maxAngularVelocity = 30f;
            Body.interpolation = RigidbodyInterpolation.Interpolate;
            Body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            Body.solverIterations = 12;
            Body.solverVelocityIterations = 4;
        }

        public void SetInput(FlightControlInput value) =>
            Input = new FlightControlInput(value.Throttle, value.Pitch, value.Yaw, value.Roll);

        public void SetArmed(bool value)
        {
            // Input adapters arm at neutral; the keyboard clears retained input first.
            if (value && Input.Throttle > .02f) return;
            Armed = value;
            integral = Vector3.zero;
            if (!value) Input = default;
        }

        public void SetMode(DroneFlightMode value)
        {
            mode = value;
            integral = Vector3.zero;
        }

        public float GetMotorRpm(int index) => rpm[index];
        public float GetMotorThrust(int index) => motorThrust[index];

        public void ResetFlight()
        {
            Armed = false;
            Input = default;
            integral = Vector3.zero;
            TotalThrust = 0f;
            MotorsSaturated = false;
            RotorAngularMomentum = Vector3.zero;
            System.Array.Clear(rpm, 0, rpm.Length);
            System.Array.Clear(motorThrust, 0, motorThrust.Length);
        }

        void FixedUpdate() => StepPhysics(Time.fixedDeltaTime);

        /// <summary>One force update. Physics integrates the Rigidbody afterwards.</summary>
        public void StepPhysics(float dt)
        {
            if (profile == null || environment == null || Body == null || dt <= 0f) return;
            Body.AddForce(environment.gravity * Body.mass, ForceMode.Force);
            ApplyAerodynamics();

            float maxThrust = profile.ThrustAtRpm(profile.maximumRpm, environment.airDensity);
            float minThrust = Armed ? profile.ThrustAtRpm(Mathf.Min(profile.idleRpm, profile.maximumRpm), environment.airDensity) : 0f;
            float collective = Armed ? Mathf.Lerp(minThrust, maxThrust, Input.Throttle) : 0f;
            bool attitudeControl = Armed && (profile.airMode || Input.Throttle > .001f);
            Vector3 omega = Quaternion.Inverse(Body.rotation) * Body.angularVelocity;
            Vector3 rateError = DesiredRates() - omega;
            Vector3 previousIntegral = integral;
            if (Armed && Input.Throttle > .02f)
                integral = Vector3.ClampMagnitude(integral + rateError * dt, profile.integralLimit);
            else integral = Vector3.zero;
            Vector3 torque = Vector3.Scale(profile.principalInertia,
                rateError * profile.rateP + integral * profile.rateI);
            torque.y *= profile.yawRateGain;

            // X quad: diagonal rotors share a spin direction. Collective, pitch,
            // roll and yaw mixer rows are orthogonal for this symmetric layout.
            float sumX2 = 0f, sumZ2 = 0f;
            for (int i = 0; i < MotorCount; i++)
            {
                Vector3 p = profile.RotorPosition(i);
                sumX2 += p.x * p.x;
                sumZ2 += p.z * p.z;
            }
            // Offset COM shifts the collective thrust moment. Feed-forward cancels it.
            torque -= Vector3.Cross(-profile.centerOfMass, Vector3.up * (collective * MotorCount));
            float low = 0f, high = 0f;
            for (int i = 0; i < MotorCount; i++)
            {
                Vector3 p = profile.RotorPosition(i);
                corrections[i] = !attitudeControl ? 0f :
                    -p.z * torque.x / Mathf.Max(.0001f, sumZ2)
                    + p.x * torque.z / Mathf.Max(.0001f, sumX2)
                    + profile.RotorSpin(i) * torque.y / (MotorCount * profile.TorquePerThrust);
                low = Mathf.Min(low, corrections[i]);
                high = Mathf.Max(high, corrections[i]);
            }
            float span = high - low;
            float available = Mathf.Max(0f, maxThrust - minThrust);
            float correctionScale = span > available ? available / Mathf.Max(span, .0001f) : 1f;
            if (!profile.airMode && low < 0f)
                correctionScale = Mathf.Min(correctionScale, (collective - minThrust) / -low);
            float mixedCollective = Mathf.Clamp(collective, minThrust - low * correctionScale,
                Mathf.Max(minThrust - low * correctionScale, maxThrust - high * correctionScale));
            MotorsSaturated = correctionScale < .999f || Mathf.Abs(mixedCollective - collective) > .0001f;
            if (MotorsSaturated) integral = previousIntegral; // Anti-windup at actuator limits.

            TotalThrust = 0f;
            Vector3 previousRotorMomentum = RotorAngularMomentum;
            RotorAngularMomentum = Vector3.zero;
            for (int i = 0; i < MotorCount; i++)
            {
                float demand = Armed
                    ? Mathf.Clamp(mixedCollective + corrections[i] * correctionScale, minThrust, maxThrust) : 0f;
                float targetRpm = maxThrust > .00001f
                    ? profile.maximumRpm * Mathf.Sqrt(demand / maxThrust) : 0f;
                float timeConstant = targetRpm > rpm[i] ? profile.spinUpTime : profile.spinDownTime;
                rpm[i] = Mathf.Lerp(rpm[i], targetRpm, 1f - Mathf.Exp(-dt / Mathf.Max(.001f, timeConstant)));
                RotorAngularMomentum += Vector3.up * (-profile.RotorSpin(i) * profile.rotorInertia
                    * rpm[i] * (2f * Mathf.PI / 60f));
                motorThrust[i] = profile.ThrustAtRpm(rpm[i], environment.airDensity);
                TotalThrust += motorThrust[i];
                Body.AddForceAtPosition(Body.rotation * Vector3.up * motorThrust[i],
                    Body.position + Body.rotation * profile.RotorPosition(i), ForceMode.Force);
                Body.AddRelativeTorque(Vector3.up * (profile.RotorSpin(i) * motorThrust[i]
                    * profile.TorquePerThrust), ForceMode.Force);
            }
            // Unity 6000.4 does not expose PhysX's gyroscopic-forces flag. Supply the
            // Euler cross term explicitly: I*wDot = torque - w x (I*w + Hrotor) - HrotorDot.
            // Principal body axes coincide with local XYZ (configured in ConfigureBody).
            Vector3 momentum = Vector3.Scale(Body.inertiaTensor, omega)
                + (previousRotorMomentum + RotorAngularMomentum) * .5f;
            Body.AddRelativeTorque(-Vector3.Cross(omega, momentum)
                - (RotorAngularMomentum - previousRotorMomentum) / dt, ForceMode.Force);
        }

        Vector3 DesiredRates()
        {
            Vector3 rates = new Vector3(Shape(Input.Pitch) * profile.maximumRates.x,
                Shape(Input.Yaw) * profile.maximumRates.y, -Shape(Input.Roll) * profile.maximumRates.z) * Mathf.Deg2Rad;
            if (mode == DroneFlightMode.Acro) return rates;

            Vector3 forward = Vector3.ProjectOnPlane(Body.rotation * Vector3.forward, Vector3.up);
            if (forward.sqrMagnitude < .001f)
                forward = Vector3.Cross(Body.rotation * Vector3.right, Vector3.up);
            Quaternion heading = Quaternion.LookRotation(forward.normalized, Vector3.up);
            Vector3 desiredUp = heading * Quaternion.Euler(Input.Pitch * profile.maximumTilt,
                0f, -Input.Roll * profile.maximumTilt) * Vector3.up;
            Quaternion error = Quaternion.FromToRotation(Body.rotation * Vector3.up, desiredUp);
            error.ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180f) angle -= 360f;
            Vector3 localError = angle == 0f ? Vector3.zero :
                Quaternion.Inverse(Body.rotation) * axis * (angle * Mathf.Deg2Rad);
            rates.x = Mathf.Clamp(localError.x * profile.angleGain,
                -profile.maximumRates.x * Mathf.Deg2Rad, profile.maximumRates.x * Mathf.Deg2Rad);
            rates.z = Mathf.Clamp(localError.z * profile.angleGain,
                -profile.maximumRates.z * Mathf.Deg2Rad, profile.maximumRates.z * Mathf.Deg2Rad);
            return rates;
        }

        float Shape(float value) => Mathf.Lerp(value, value * value * value, profile.expo);

        void ApplyAerodynamics()
        {
            Vector3 velocity = Quaternion.Inverse(Body.rotation) * (Body.linearVelocity - environment.windVelocity);
            Vector3 force = new Vector3(
                -velocity.x * Mathf.Abs(velocity.x) * profile.dragArea.x,
                -velocity.y * Mathf.Abs(velocity.y) * profile.dragArea.y,
                -velocity.z * Mathf.Abs(velocity.z) * profile.dragArea.z) * (.5f * environment.airDensity);
            Body.AddRelativeForce(force, ForceMode.Force);
            Vector3 omega = Quaternion.Inverse(Body.rotation) * Body.angularVelocity;
            Body.AddRelativeTorque(new Vector3(-omega.x * Mathf.Abs(omega.x),
                -omega.y * Mathf.Abs(omega.y), -omega.z * Mathf.Abs(omega.z))
                * (environment.airDensity * profile.rotationalDrag), ForceMode.Force);
        }

        void Update()
        {
            if (profile == null) return;
            for (int i = 0; i < Mathf.Min(MotorCount, rotorVisuals.Length); i++)
                if (rotorVisuals[i] != null)
                    rotorVisuals[i].Rotate(Vector3.up, -profile.RotorSpin(i) * rpm[i] * 6f * Time.deltaTime);
        }
    }
}
