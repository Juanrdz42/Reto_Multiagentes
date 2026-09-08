using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Carga simulation_export.json (generado por entregam3.py) y reproduce la
/// simulación en 3D: mueve un objeto por cada AGV y por cada pallet, e
/// interpola su posición entre frames grabados para que el movimiento se vea
/// fluido aunque el JSON solo tenga una muestra cada pocos pasos.
///
/// Requiere un CoordinateMapper en la escena con al menos 2 ZoneAnchor ya
/// colocados (ver ZoneAnchor.cs) para saber cómo convertir las posiciones
/// 2D de la simulación a posiciones 3D reales del cuarto.
/// </summary>
public class SimPlayer : MonoBehaviour
{
    [Header("Datos")]
    [Tooltip("Nombre del archivo dentro de Assets/StreamingAssets/")]
    public string jsonFileName = "simulation_export.json";

    [Header("Visuales")]
    [Tooltip("Prefab del AGV. Si lo dejas vacío se genera un placeholder (cubo + flecha).")]
    public GameObject agvPrefabOverride;
    [Tooltip("Prefab usado para cada pallet, p.ej. Assets/Cuarto Completo/Caja.prefab")]
    public GameObject palletPrefab;

    [Header("Reproducción")]
    [Tooltip("Segundos que dura cada frame grabado al reproducirse")]
    public float secondsPerFrame = 0.15f;
    public bool loop = true;
    [Range(0.1f, 5f)] public float playbackSpeed = 1f;

    [Header("Colores del placeholder de AGV por estado")]
    public Color colorIdle = Color.gray;
    public Color colorMovingToPallet = new Color(0.2f, 0.5f, 1f);
    public Color colorTransporting = new Color(0.2f, 0.8f, 0.3f);
    public Color colorCharging = new Color(1f, 0.85f, 0.1f);

    [Header("Colores de estación (requiere ZoneAnchor.stateRenderer)")]
    public Color colorStationAvailable = Color.white;
    public Color colorStationReserved = new Color(1f, 0.7f, 0.2f);
    public Color colorStationOccupied = new Color(0.85f, 0.2f, 0.2f);
    public Color colorStationOutOfService = Color.black;

    public static SimPlayer Instance { get; private set; }

    /// <summary>Misiones del frame que se está mostrando ahora mismo (para UI, p.ej. MissionPanelUI).</summary>
    public List<SimMissionFrame> CurrentMissions { get; private set; }
    public int CurrentCompleted { get; private set; }
    public int TotalMissions => data != null && data.meta != null ? data.meta.total_missions : 0;

    SimRoot data;
    int frameIndex;
    float timeInFrame;

    readonly Dictionary<string, Transform> agvVisuals = new Dictionary<string, Transform>();
    readonly Dictionary<string, Transform> palletVisuals = new Dictionary<string, Transform>();
    readonly Dictionary<string, ZoneAnchor> stationAnchors = new Dictionary<string, ZoneAnchor>();

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        if (!LoadData()) { enabled = false; return; }
        IndexStationAnchors();
        FitCoordinateMapper();
    }

    bool LoadData()
    {
        string path = Path.Combine(Application.streamingAssetsPath, jsonFileName);
        if (!File.Exists(path))
        {
            Debug.LogError($"[SimPlayer] No existe el archivo '{path}'. Copia simulation_export.json a Assets/StreamingAssets/.");
            return false;
        }

        string json = File.ReadAllText(path);
        data = JsonUtility.FromJson<SimRoot>(json);

        if (data == null || data.frames == null || data.frames.Count == 0)
        {
            Debug.LogError($"[SimPlayer] '{path}' no tiene frames válidos.");
            return false;
        }

        Debug.Log($"[SimPlayer] Cargados {data.frames.Count} frames, {data.meta.n_agvs} AGVs, {data.meta.total_steps} pasos simulados.");
        return true;
    }

    void IndexStationAnchors()
    {
        foreach (var anchor in FindObjectsOfType<ZoneAnchor>())
        {
            if (string.IsNullOrEmpty(anchor.simId)) continue;
            stationAnchors[anchor.simId] = anchor;
        }
    }

    void FitCoordinateMapper()
    {
        var mapper = CoordinateMapper.Instance;
        if (mapper == null)
        {
            Debug.LogError("[SimPlayer] No hay un CoordinateMapper en la escena. Agrega uno a cualquier GameObject.");
            enabled = false;
            return;
        }

        var simPositions = new Dictionary<string, Vector2>();
        foreach (var s in data.stations) simPositions[s.name] = new Vector2(s.pos[0], s.pos[1]);
        foreach (var c in data.chargers) simPositions[c.name] = new Vector2(c.pos[0], c.pos[1]);

        mapper.Fit(new List<ZoneAnchor>(stationAnchors.Values), simPositions);
    }

    void Update()
    {
        if (data == null || data.frames == null || data.frames.Count < 2) return;

        AdvanceFrame();

        float lerp = timeInFrame / secondsPerFrame;
        int nextIndex = Mathf.Min(frameIndex + 1, data.frames.Count - 1);
        SimFrame frameA = data.frames[frameIndex];
        SimFrame frameB = data.frames[nextIndex];

        ApplyFrame(frameA, frameB, lerp);
    }

    void AdvanceFrame()
    {
        timeInFrame += Time.deltaTime * playbackSpeed;
        while (timeInFrame >= secondsPerFrame)
        {
            timeInFrame -= secondsPerFrame;
            frameIndex++;
            if (frameIndex >= data.frames.Count - 1)
            {
                if (loop)
                {
                    frameIndex = 0;
                }
                else
                {
                    frameIndex = data.frames.Count - 2;
                    timeInFrame = 0f;
                    break;
                }
            }
        }
    }

    void ApplyFrame(SimFrame a, SimFrame b, float lerp)
    {
        var mapper = CoordinateMapper.Instance;

        foreach (var agvA in a.agvs)
        {
            SimAgvFrame agvB = FindAgv(b, agvA.id) ?? agvA;
            Vector2 simPos = Vector2.Lerp(ToV2(agvA.pos), ToV2(agvB.pos), lerp);
            Vector3 worldPos = mapper.SimToUnity(simPos);

            Transform visual = GetOrCreateAgv(agvA.id);
            Vector3 dir = worldPos - visual.position;
            visual.position = worldPos;
            if (dir.sqrMagnitude > 0.0001f)
                visual.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);

            TintAgv(visual, agvA.state);
        }

        foreach (var pA in a.pallets)
        {
            SimPalletFrame pB = FindPallet(b, pA.id) ?? pA;
            Vector2 simPos = Vector2.Lerp(ToV2(pA.pos), ToV2(pB.pos), lerp);
            Transform visual = GetOrCreatePallet(pA.id);
            visual.position = mapper.SimToUnity(simPos);
        }

        foreach (var s in a.stations)
        {
            if (stationAnchors.TryGetValue(s.id, out var anchor) && anchor.stateRenderer != null)
                TintStation(anchor.stateRenderer, s.state);
        }

        CurrentMissions = a.missions;
        CurrentCompleted = a.completed;
    }

    Transform GetOrCreateAgv(string id)
    {
        if (agvVisuals.TryGetValue(id, out var existing)) return existing;

        Transform created = agvPrefabOverride != null
            ? Instantiate(agvPrefabOverride).transform
            : BuildAgvPlaceholder();
        created.name = $"AGV_{id}";
        agvVisuals[id] = created;
        return created;
    }

    Transform GetOrCreatePallet(string id)
    {
        if (palletVisuals.TryGetValue(id, out var existing)) return existing;

        Transform created = palletPrefab != null
            ? Instantiate(palletPrefab).transform
            : BuildPalletPlaceholder();
        created.name = $"Pallet_{id}";
        palletVisuals[id] = created;
        return created;
    }

    static Transform BuildAgvPlaceholder()
    {
        var root = new GameObject("AGV");

        var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
        body.name = "Body";
        body.transform.SetParent(root.transform, false);
        body.transform.localScale = new Vector3(0.4f, 0.25f, 0.6f);
        body.transform.localPosition = new Vector3(0f, 0.125f, 0f);
        Destroy(body.GetComponent<Collider>());

        var arrow = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        arrow.name = "Direction";
        arrow.transform.SetParent(root.transform, false);
        arrow.transform.localScale = new Vector3(0.05f, 0.15f, 0.05f);
        arrow.transform.localPosition = new Vector3(0f, 0.3f, 0.35f);
        arrow.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        Destroy(arrow.GetComponent<Collider>());

        return root.transform;
    }

    static Transform BuildPalletPlaceholder()
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.transform.localScale = new Vector3(0.3f, 0.2f, 0.3f);
        Destroy(go.GetComponent<Collider>());
        return go.transform;
    }

    void TintAgv(Transform visual, string state)
    {
        if (agvPrefabOverride != null) return; // no forzamos color sobre un modelo real

        var renderer = visual.GetComponentInChildren<Renderer>();
        if (renderer == null) return;

        Color c = state switch
        {
            "MOVING_TO_PALLET" => colorMovingToPallet,
            "TRANSPORTING" => colorTransporting,
            "CHARGING" => colorCharging,
            _ => colorIdle,
        };
        renderer.material.color = c;
    }

    void TintStation(Renderer renderer, string state)
    {
        Color c = state switch
        {
            "RESERVED" => colorStationReserved,
            "OCCUPIED" => colorStationOccupied,
            "OUT_OF_SERVICE" => colorStationOutOfService,
            _ => colorStationAvailable,
        };
        renderer.material.color = c;
    }

    static SimAgvFrame FindAgv(SimFrame f, string id)
    {
        foreach (var a in f.agvs) if (a.id == id) return a;
        return null;
    }

    static SimPalletFrame FindPallet(SimFrame f, string id)
    {
        foreach (var p in f.pallets) if (p.id == id) return p;
        return null;
    }

    static Vector2 ToV2(float[] arr) => new Vector2(arr[0], arr[1]);
}
