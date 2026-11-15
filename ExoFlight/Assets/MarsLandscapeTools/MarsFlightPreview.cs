using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
public sealed class MarsFlightPreview : MonoBehaviour
{
    [SerializeField] private MarsStorm storm;
    [SerializeField] private Vector3[] route;
    [SerializeField] private int stormAfterLeg;
    [SerializeField] private int caveStartsAtLeg;
    private int leg;
    private bool guiding;
    private bool stormPreview;
    private float yaw;
    private float pitch;

    private void Start()
    {
        if (storm) storm.SetObserver(transform);
        ReadAngles();
    }

    private void Update()
    {
        var keys = Keyboard.current;
        if (keys != null && keys.gKey.wasPressedThisFrame) ToggleGuidedFlight();
        if (keys != null && keys.bKey.wasPressedThisFrame) ToggleStormPreview();
        if (guiding)
        {
            FlyRoute();
            return;
        }
        if (keys == null) return;

        var mouse = Mouse.current;
        if (mouse != null && mouse.rightButton.isPressed)
        {
            Vector2 delta = mouse.delta.ReadValue() * .12f;
            yaw += delta.x;
            pitch = Mathf.Clamp(pitch - delta.y, -80f, 80f);
            transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
        }

        Vector3 forward = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
        Vector3 input = (keys.wKey.ReadValue() - keys.sKey.ReadValue()) * forward
            + (keys.dKey.ReadValue() - keys.aKey.ReadValue()) * transform.right
            + (keys.eKey.ReadValue() - keys.qKey.ReadValue()) * Vector3.up;
        float speed = keys.leftShiftKey.isPressed ? 45f : 16f;
        Vector3 move = Vector3.ClampMagnitude(input, 1f) * (speed * Time.deltaTime);
        float distance = move.magnitude;
        if (distance <= 0f) return;

        Vector3 direction = move / distance;
        if (Physics.SphereCast(transform.position, .6f, direction, out RaycastHit hit,
                distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            distance = Mathf.Max(0f, hit.distance - .08f);
        transform.position += direction * distance;
    }

    public void ToggleGuidedFlight()
    {
        if (route == null || route.Length < 2) return;
        guiding = !guiding;
        if (!guiding)
        {
            ReadAngles();
            return;
        }
        leg = 1;
        transform.SetPositionAndRotation(route[0], Quaternion.LookRotation(route[1] - route[0]));
        SetStorm(false);
    }

    public void ToggleStormPreview() => SetStorm(!stormPreview);

    private void FlyRoute()
    {
        Vector3 destination = route[leg];
        float speed = leg < stormAfterLeg ? 28f : leg <= caveStartsAtLeg ? 21f : 12f;
        transform.position = Vector3.MoveTowards(transform.position, destination, speed * Time.deltaTime);
        Vector3 look = destination - transform.position;
        if (leg + 1 < route.Length)
            look += (route[leg + 1] - destination).normalized * Mathf.Max(0f, 12f - look.magnitude);
        if (look.sqrMagnitude > .01f)
            transform.rotation = Quaternion.Slerp(transform.rotation,
                Quaternion.LookRotation(look), 1f - Mathf.Exp(-2.6f * Time.deltaTime));

        if (transform.position != destination) return;
        if (leg == stormAfterLeg) SetStorm(true);
        leg++;
        if (leg < route.Length) return;
        guiding = false;
        ReadAngles();
        Debug.Log("Mars guided flight reached the research chamber.", this);
    }

    private void ReadAngles()
    {
        Vector3 angles = transform.eulerAngles;
        yaw = angles.y;
        pitch = Mathf.DeltaAngle(0f, angles.x);
    }

    private void SetStorm(bool enabled)
    {
        stormPreview = enabled;
        if (!storm) return;
        if (enabled) storm.BeginStorm();
        else storm.EndStorm();
    }
}
