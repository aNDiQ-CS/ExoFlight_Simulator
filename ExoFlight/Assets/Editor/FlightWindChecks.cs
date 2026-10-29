using System;
using System.Collections.Generic;
using System.IO;
using ExoFlight.Flight;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ExoFlight.Editor
{
    /// <summary>Wind field checks in Edit Mode: pure sampler math and the effect table.</summary>
    public static class FlightWindChecks
    {
        const string ReportPath = "Temp/wind-field-checks.txt";

        [MenuItem("Tools/ExoFlight/Run Wind Field Checks")]
        public static void Run()
        {
            var report = new List<string> { "Unity " + Application.unityVersion + " | " + DateTime.UtcNow.ToString("O") };
            WindProfile profile = ScriptableObject.CreateInstance<WindProfile>();
            try
            {
                profile.seed = 12345;
                profile.gustScale = 30f;
                profile.fullSpeedAltitude = 20f;
                profile.groundSpeedFactor = .5f;
                profile.octaves = 2;
                profile.speedScale = 1f;

                Action<bool, string> check = (condition, name) =>
                {
                    report.Add((condition ? "PASS " : "FAIL ") + name);
                    if (!condition) throw new InvalidOperationException(name);
                };

                profile.baseVelocity = new Vector3(0f, 0f, 20f);
                profile.gustSpeed = 8f;
                Vector3 first = WindField.Sample(profile, new Vector3(12.5f, 7.25f, -33.5f), 123.456f);
                Vector3 second = WindField.Sample(profile, new Vector3(12.5f, 7.25f, -33.5f), 123.456f);
                check((first - second).sqrMagnitude == 0f, "W1 sampler is deterministic for equal position and time");

                profile.gustSpeed = 0f;
                var random = new System.Random(7);
                Vector3 sum = Vector3.zero;
                const int meanSamples = 2000;
                for (int i = 0; i < meanSamples; i++)
                    sum += WindField.Sample(profile, RandomPoint(random, 25f, 40f), 0f);
                Vector3 mean = sum / meanSamples;
                report.Add($"MEASURE mean of 2000 samples above full speed altitude: {mean}");
                check(Mathf.Abs(mean.z - 20f) < 1f && Mathf.Abs(mean.x) < 1f && Mathf.Abs(mean.y) < 1f,
                    "W2 mean of the field matches the configured mean wind");

                float ground = WindField.Sample(profile, Vector3.zero, 0f).magnitude;
                float aloft = WindField.Sample(profile, new Vector3(0f, 60f, 0f), 0f).magnitude;
                report.Add($"MEASURE altitude profile: y=0 -> {ground:F3} m/s, y=60 -> {aloft:F3} m/s");
                check(Mathf.Abs(ground - 10f) < .2f && Mathf.Abs(aloft - 20f) < .2f,
                    "W3 wind speed follows the altitude profile");

                profile.gustSpeed = 10f;
                float gustSum = 0f, gustMax = 0f;
                const int gustSamples = 2000;
                for (int i = 0; i < gustSamples; i++)
                {
                    float gust = (WindField.Sample(profile, RandomPoint(random, 25f, 60f), 0f) - new Vector3(0f, 0f, 20f)).magnitude;
                    gustSum += gust;
                    gustMax = Mathf.Max(gustMax, gust);
                }
                float gustMean = gustSum / gustSamples;
                report.Add($"MEASURE gust amplitude for gustSpeed 10: mean {gustMean:F3} m/s, max {gustMax:F3} m/s");
                check(gustMean > 7f && gustMean < 13f && gustMax < 35f,
                    "W4 gust amplitude is calibrated so gustSpeed is the typical gust speed");

                float worstDivergence = 0f;
                const float h = .5f;
                for (int i = 0; i < 200; i++)
                {
                    Vector3 p = RandomPoint(random, 25f, 60f);
                    float dx = (WindField.Sample(profile, p + Vector3.right * h, 0f).x - WindField.Sample(profile, p - Vector3.right * h, 0f).x) / (2f * h);
                    float dy = (WindField.Sample(profile, p + Vector3.up * h, 0f).y - WindField.Sample(profile, p - Vector3.up * h, 0f).y) / (2f * h);
                    float dz = (WindField.Sample(profile, p + Vector3.forward * h, 0f).z - WindField.Sample(profile, p - Vector3.forward * h, 0f).z) / (2f * h);
                    worstDivergence = Mathf.Max(worstDivergence, Mathf.Abs(dx + dy + dz));
                }
                report.Add($"MEASURE worst divergence over 200 points: {worstDivergence:F5} 1/s");
                // The curl is analytically divergence-free; the residual is float32 differencing (measured ~0.05).
                check(worstDivergence < .15f, "W5 gust field has no divergence: air is neither created nor compressed");

                float worstStep = 0f, worstTime = 0f;
                for (int i = 0; i < 200; i++)
                {
                    Vector3 p = RandomPoint(random, 25f, 60f);
                    worstStep = Mathf.Max(worstStep, (WindField.Sample(profile, p + Vector3.forward, 0f) - WindField.Sample(profile, p, 0f)).magnitude);
                    worstTime = Mathf.Max(worstTime, (WindField.Sample(profile, p, .1f) - WindField.Sample(profile, p, 0f)).magnitude);
                }
                report.Add($"MEASURE change over 1 m: {worstStep:F3} m/s, over 0.1 s: {worstTime:F3} m/s");
                // A gust field eddies over gustScale metres, so a one-metre step may change about half of
                // the gust amplitude; advection moves the same eddies past a point in a tenth of a second.
                check(worstStep < profile.gustSpeed * .6f, "W6 field is spatially smooth");
                check(worstTime < profile.gustSpeed * 1.2f, "W7 field is temporally coherent at the simulation step");

                GC.Collect();
                long before = GC.GetTotalMemory(true);
                float sink = 0f;
                for (int i = 0; i < 20000; i++) sink += WindField.Sample(profile, RandomPoint(random, 25f, 60f), i * .01f).x;
                long allocated = GC.GetTotalMemory(false) - before;
                report.Add($"MEASURE allocation for 20000 samples: {allocated} bytes (sink {sink:F1})");
                check(allocated < 4096, "W8 sampler allocates nothing per sample");

                profile.gustSpeed = 0f;
                profile.baseVelocity = new Vector3(0f, 0f, 10f);
                profile.speedScale = 1f;
                Vector3 ten = WindField.Sample(profile, new Vector3(0f, 60f, 0f), 0f);
                profile.speedScale = 2f;
                Vector3 tenScaled = WindField.Sample(profile, new Vector3(0f, 60f, 0f), 0f);
                profile.baseVelocity = new Vector3(0f, 0f, 20f);
                profile.speedScale = 1f;
                Vector3 twenty = WindField.Sample(profile, new Vector3(0f, 60f, 0f), 0f);
                float forceTen = ten.z * ten.z;
                float forceTenScaled = tenScaled.z * tenScaled.z;
                report.Add($"MEASURE 10 m/s x1 -> {ten.z:F3}, 10 m/s x2 -> {tenScaled.z:F3}, 20 m/s x1 -> {twenty.z:F3}, drag ratio {forceTenScaled / forceTen:F4}");
                check((tenScaled - twenty).magnitude < .001f && Mathf.Abs(forceTenScaled / forceTen - 4f) < .05f,
                    "W15 speedScale multiplies wind speed, so drag grows with its square");

                report.Add("RESULT " + CountPassed(report) + " checks passed");
            }
            catch (Exception exception)
            {
                report.Add("ERROR " + exception.Message);
                throw;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(profile);
                File.WriteAllLines(ReportPath, report);
                Debug.Log("[Wind field checks] " + string.Join("\n", report));
            }
        }

        [MenuItem("Tools/ExoFlight/Print Wind Effect Table")]
        public static void PrintEffectTable()
        {
            var report = new List<string> { "Wind effect table | " + DateTime.UtcNow.ToString("O") };
            FlightEnvironment environment = FirstAsset<FlightEnvironment>();
            DroneFlightProfile drone = FirstAsset<DroneFlightProfile>();
            if (environment == null || drone == null)
            {
                report.Add("FAIL assign a FlightEnvironment and a DroneFlightProfile in the project first");
                File.WriteAllLines("Temp/wind-effect-table.txt", report);
                Debug.LogWarning("[Wind effect] missing assets");
                return;
            }

            float weight = drone.mass * Mathf.Abs(environment.gravity.y);
            float maximumThrust = 4f * drone.thrustCoefficient * environment.airDensity
                * Mathf.Pow(drone.maximumRpm / 60f, 2f) * Mathf.Pow(drone.propellerDiameter, 4f);
            report.Add($"Atmosphere {environment.name}: rho {environment.airDensity} kg/m3, g {Mathf.Abs(environment.gravity.y)} m/s2");
            report.Add($"Drone {drone.name}: mass {drone.mass} kg, weight {weight:F3} N, maximum thrust {maximumThrust:F3} N, "
                + $"margin {maximumThrust / weight:F2}x, drag area side {drone.dragArea.x} m2 / top {drone.dragArea.y} m2");
            WindProfile wind = environment.windProfile;
            report.Add(wind == null
                ? "Wind profile: not assigned, the constant windVelocity is used"
                : $"Wind profile {wind.name}: base {wind.baseVelocity} m/s, gusts {wind.gustSpeed} m/s, speedScale {wind.speedScale}");
            report.Add("");
            report.Add("speed m/s | side force N | top force N | share of weight | tilt to hold | thrust left");
            for (int speed = 0; speed <= 50; speed += 5)
            {
                float side = .5f * environment.airDensity * drone.dragArea.x * speed * speed;
                float top = .5f * environment.airDensity * drone.dragArea.y * speed * speed;
                float tilt = Mathf.Atan2(side, weight) * Mathf.Rad2Deg;
                float thrustLeft = maximumThrust * Mathf.Cos(tilt * Mathf.Deg2Rad);
                report.Add($"{speed,9} | {side,12:F3} | {top,11:F3} | {side / weight * 100f,14:F1}% | {tilt,12:F2} deg | {thrustLeft / weight * 100f,10:F1}%");
            }

            File.WriteAllLines("Temp/wind-effect-table.txt", report);
            Debug.Log("[Wind effect] " + string.Join("\n", report));
        }

        [MenuItem("Tools/ExoFlight/Run Wind Physics Checks (Play Mode)")]
        public static void RunPhysics()
        {
            if (!Application.isPlaying) throw new InvalidOperationException("Enter Play Mode first.");
            var report = new List<string> { "Unity " + Application.unityVersion + " | " + DateTime.UtcNow.ToString("O") };
            var scene = SceneManager.CreateScene("Wind Physics Checks", new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            GameObject root = null;
            DroneFlightProfile profile = null;
            FlightEnvironment environment = null;
            WindProfile wind = ScriptableObject.CreateInstance<WindProfile>();
            try
            {
                root = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/PlayerDrone.prefab"));
                SceneManager.MoveGameObjectToScene(root, scene);
                var drone = root.GetComponent<DroneFlightController>();
                root.GetComponentInChildren<Camera>().enabled = false;
                root.GetComponentInChildren<AudioListener>().enabled = false;
                profile = UnityEngine.Object.Instantiate(drone.profile);
                environment = UnityEngine.Object.Instantiate(drone.environment);
                drone.profile = profile;
                drone.environment = environment;
                drone.ConfigureBody();
                drone.Body.interpolation = RigidbodyInterpolation.None;
                environment.windProfile = wind;
                wind.seed = 12345;
                wind.fullSpeedAltitude = 20f;
                wind.groundSpeedFactor = .5f;
                wind.speedScale = 1f;
                wind.advectionSpeed = 0f;
                var physics = scene.GetPhysicsScene();

                Action<bool, string> check = (condition, name) =>
                {
                    report.Add((condition ? "PASS " : "FAIL ") + name);
                    if (!condition) throw new InvalidOperationException(name);
                };
                Action restart = () =>
                {
                    drone.ResetFlight();
                    drone.Body.position = new Vector3(0f, 50f, 0f);
                    drone.Body.rotation = Quaternion.identity;
                    drone.Body.linearVelocity = Vector3.zero;
                    drone.Body.angularVelocity = Vector3.zero;
                    drone.Body.WakeUp();
                    Physics.SyncTransforms();
                };
                Action<float, FlightControlInput, bool> run = (seconds, controls, armed) =>
                {
                    drone.SetArmed(armed);
                    drone.SetInput(controls);
                    for (int i = 0; i < Mathf.RoundToInt(seconds / .01f); i++)
                    {
                        drone.StepPhysics(.01f);
                        physics.Simulate(.01f);
                    }
                };

                environment.gravity = Vector3.zero;
                wind.gustSpeed = 0f;
                wind.baseVelocity = new Vector3(0f, 0f, 20f);
                restart();
                run(2f, default, false);
                report.Add($"MEASURE 20 m/s side wind for 2 s: drift {drone.Body.position.z:F3} m, speed {drone.Body.linearVelocity.z:F3} m/s");
                check(drone.Body.position.z > .5f && drone.Body.linearVelocity.z > .5f,
                    "W9 a level body drifts downwind");

                restart();
                drone.Body.linearVelocity = new Vector3(0f, 0f, 20f);
                run(.1f, default, false);
                report.Add($"MEASURE travelling with the air: speed {drone.Body.linearVelocity.z:F4} m/s");
                check(Mathf.Abs(drone.Body.linearVelocity.z - 20f) < .05f,
                    "W10 drag vanishes when the body travels with the air mass");

                environment.gravity = new Vector3(0f, -3.71f, 0f);
                wind.baseVelocity = Vector3.zero;
                restart();
                run(.5f, default, false);
                float fall = drone.Body.linearVelocity.y;
                wind.baseVelocity = new Vector3(0f, 15f, 0f);
                restart();
                run(.5f, default, false);
                float lifted = drone.Body.linearVelocity.y;
                report.Add($"MEASURE fall after 0.5 s: calm {fall:F3} m/s, 15 m/s updraft {lifted:F3} m/s");
                check(lifted > fall + .15f, "W11 an updraft slows the fall because the top face has the largest area");

                environment.gravity = Vector3.zero;
                wind.baseVelocity = new Vector3(0f, 0f, 25f);
                wind.gustSpeed = 15f;
                wind.gustScale = 8f;
                drone.windAtRotors = false;
                restart();
                run(1f, default, false);
                float centred = drone.Body.angularVelocity.magnitude;
                drone.windAtRotors = true;
                restart();
                run(1f, default, false);
                float perRotor = drone.Body.angularVelocity.magnitude;
                report.Add($"MEASURE rotation after 1 s in gusts: centre drag {centred:F4} rad/s, rotor points {perRotor:F4} rad/s");
                check(perRotor > .02f && perRotor > centred * 2f,
                    "W12 a gust gradient across the four rotor points creates a moment");

                environment.gravity = new Vector3(0f, -3.71f, 0f);
                drone.windAtRotors = false;
                wind.gustSpeed = 0f;
                wind.baseVelocity = Vector3.zero;
                restart();
                run(2f, new FlightControlInput(drone.HoverThrottle, 0, 0, 0), true);
                float calmThrust = drone.TotalThrust;
                wind.baseVelocity = new Vector3(0f, 0f, 30f);
                restart();
                run(2f, new FlightControlInput(drone.HoverThrottle, 0, 0, 0), true);
                float windyThrust = drone.TotalThrust;
                report.Add($"MEASURE hover thrust: calm {calmThrust:F4} N, 30 m/s wind {windyThrust:F4} N");
                check(Mathf.Abs(windyThrust - calmThrust) < .05f,
                    "W13 wind does not change rotor thrust: the model boundary is deliberate");

                report.Add("RESULT " + CountPassed(report) + " checks passed");
            }
            catch (Exception exception)
            {
                report.Add("ERROR " + exception.Message);
                throw;
            }
            finally
            {
                if (root != null) UnityEngine.Object.DestroyImmediate(root);
                if (profile != null) UnityEngine.Object.DestroyImmediate(profile);
                if (environment != null) UnityEngine.Object.DestroyImmediate(environment);
                UnityEngine.Object.DestroyImmediate(wind);
                SceneManager.UnloadSceneAsync(scene);
                File.WriteAllLines("Temp/wind-physics-checks.txt", report);
                Debug.Log("[Wind physics checks] " + string.Join("\n", report));
            }
        }

        static Vector3 RandomPoint(System.Random random, float minimumY, float maximumY) => new Vector3(
            (float)random.NextDouble() * 400f - 200f,
            Mathf.Lerp(minimumY, maximumY, (float)random.NextDouble()),
            (float)random.NextDouble() * 400f - 200f);

        static int CountPassed(List<string> report)
        {
            int passed = 0;
            foreach (string line in report)
                if (line.StartsWith("PASS ")) passed++;
            return passed;
        }

        static T FirstAsset<T>() where T : UnityEngine.Object
        {
            foreach (string guid in AssetDatabase.FindAssets("t:" + typeof(T).Name))
            {
                var asset = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid));
                if (asset != null) return asset;
            }
            return null;
        }
    }
}
