using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>1–4: seguimiento del AGV. 5: plano cenital orientado como Python.</summary>
public class SimulationCameraController : MonoBehaviour
{
    public float followDistanceInDiameters = 2.6f;
    public float followHeightInDiameters = 1.6f;
    public float followSharpness = 9f;
    public int SelectedView { get; private set; } = 5;

    SimPlayer player;
    CoordinateMapper mapper;
    SimMeta meta;
    Camera viewCamera;
    bool snap = true;
    int heldView;

    public void Initialize(SimPlayer simulation, CoordinateMapper coordinates, SimMeta metadata, Camera camera)
    {
        player = simulation;
        mapper = coordinates;
        meta = metadata;
        viewCamera = camera;
        if (viewCamera == null)
        {
            Debug.LogError("[SimulationCameraController] Falta la cámara principal.");
            enabled = false;
            return;
        }
        viewCamera.nearClipPlane = 0.08f;
        viewCamera.farClipPlane = Mathf.Max(viewCamera.farClipPlane, 500f);
        SelectView(5);
        Refresh(0f);
    }

    public void SelectView(int number)
    {
        if (number < 1 || number > 5 || viewCamera == null) return;
        if (number != 5 && (player == null || !player.TryGetAgvVisual("AGV-" + number, out _))) return;
        SelectedView = number;
        viewCamera.orthographic = number == 5;
        if (number != 5) viewCamera.fieldOfView = 60f;
        snap = true;
    }

    void Update()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null) return;
        int requested = 0;
        if (keyboard.digit1Key.isPressed || keyboard.numpad1Key.isPressed) requested = 1;
        if (keyboard.digit2Key.isPressed || keyboard.numpad2Key.isPressed) requested = 2;
        if (keyboard.digit3Key.isPressed || keyboard.numpad3Key.isPressed) requested = 3;
        if (keyboard.digit4Key.isPressed || keyboard.numpad4Key.isPressed) requested = 4;
        if (keyboard.digit5Key.isPressed || keyboard.numpad5Key.isPressed) requested = 5;
        if (requested != 0 && requested != heldView) SelectView(requested);
        heldView = requested;
    }

    void LateUpdate() => Refresh(Time.unscaledDeltaTime);

    public void Refresh(float seconds)
    {
        if (viewCamera == null || mapper == null || meta == null) return;
        if (SelectedView == 5)
        {
            float width = meta.width * mapper.scale;
            float height = meta.height * mapper.scale;
            Vector3 center = mapper.SimToUnity(new Vector2(meta.width / 2f, meta.height / 2f));
            viewCamera.transform.SetPositionAndRotation(center + Vector3.up * Mathf.Max(width, height) * 1.4f,
                Quaternion.LookRotation(Vector3.down, mapper.Rotation * Vector3.forward));
            viewCamera.orthographicSize = Mathf.Max(height / 2f, width / (2f * Mathf.Max(0.1f, viewCamera.aspect))) * 1.08f;
        }
        else if (player.TryGetAgvVisual("AGV-" + SelectedView, out var target))
        {
            float diameter = meta.AgvDiameter * mapper.scale;
            Vector3 focus = target.position + Vector3.up * diameter * 0.4f;
            Vector3 desired = target.position - target.forward * diameter * followDistanceInDiameters
                + Vector3.up * diameter * followHeightInDiameters;
            Vector3 position = snap ? desired : Vector3.Lerp(viewCamera.transform.position, desired,
                1f - Mathf.Exp(-followSharpness * Mathf.Max(0f, seconds)));
            // Resolver después del suavizado para no meter la cámara detrás de un muro o rack.
            Vector3 offset = position - focus;
            if (Physics.SphereCast(focus, 0.2f, offset.normalized, out var hit, offset.magnitude,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                position = focus + offset.normalized * Mathf.Max(0.3f, hit.distance - 0.15f);
            viewCamera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(focus - position, Vector3.up));
        }
        snap = false;
    }

    void OnGUI()
    {
        if (viewCamera == null) return;
        string view = SelectedView == 5 ? "Vista superior" : "AGV-" + SelectedView;
        GUI.Box(new Rect(12f, Screen.height - 40f, 390f, 28f), "1–4: seguir AGV   |   5: vista superior   ·   " + view);
    }
}
