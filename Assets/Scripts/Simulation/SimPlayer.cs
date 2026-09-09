using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Carga simulation_export.json (generado por m4.py) y reproduce la
/// simulación en 3D: mueve un objeto por cada AGV y por cada pallet, e
/// interpola su posición entre frames grabados para que el movimiento se vea
/// fluido aunque el JSON solo tenga una muestra cada pocos pasos.
///
/// Comparte CoordinateMapper con SimulationLayout y ReferenceLayoutGizmo.
/// </summary>
public class SimPlayer : MonoBehaviour
{
    [Header("Datos")]
    [Tooltip("Nombre del archivo dentro de Assets/StreamingAssets/")]
    public string jsonFileName = "simulation_export.json";

    [Header("Escenario")]
    public SimulationLayout layout;

    [Header("Visuales")]
    [Tooltip("Prefab del AGV. Si lo dejas vacío se genera un placeholder (cubo + flecha).")]
    public GameObject agvPrefabOverride;
    [Tooltip("Prefab usado para cada pallet, p.ej. Assets/Caja.prefab")]
    public GameObject palletPrefab;

    [Header("Reproducción")]
    [Tooltip("Segundos que dura cada frame grabado al reproducirse")]
    public float secondsPerFrame = 0.05f;
    public bool loop = false;
    public bool paused;
    public SimResults Results => data == null ? null : data.results;
    public int LastStep => data == null ? 0 : data.frames[data.frames.Count - 1].t;
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

    static SimPlayer instance;
    public static SimPlayer Instance
    {
        get { if (instance == null) instance = FindFirstObjectByType<SimPlayer>(); return instance; }
        private set => instance = value;
    }

    /// <summary>Misiones del frame que se está mostrando ahora mismo (para UI, p.ej. MissionPanelUI).</summary>
    public List<SimMissionFrame> CurrentMissions { get; private set; }
    public int CurrentCompleted { get; private set; }
    public int CurrentStep { get; private set; }
    public List<SimAgvFrame> CurrentAgvs { get; private set; }
    public List<SimStationFrame> CurrentStations { get; private set; }
    public SimMeta Metadata => data == null ? null : data.meta;
    public int TotalMissions => data != null && data.meta != null ? data.meta.total_missions : 0;

    public bool TryGetAgvVisual(string id, out Transform visual) => agvVisuals.TryGetValue(id, out visual);

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

    void OnEnable()
    {
        Instance = this;
        if (Application.isPlaying && GetComponent<SimulationSessionUI>() == null)
            gameObject.AddComponent<SimulationSessionUI>();
    }

    void Start()
    {
        loop = false;
        if (GetComponent<SimulationSessionUI>() == null) gameObject.AddComponent<SimulationSessionUI>();
        if (!LoadData()) { paused = true; return; }
        var mapper = CoordinateMapper.Instance;
        if (mapper == null || !mapper.Ready)
        {
            Debug.LogError("[SimPlayer] Falta CoordinateMapper o su escala no es válida.");
            enabled = false;
            return;
        }
        if (layout != null) layout.Apply(data, mapper);
        IndexStationAnchors();
        var appearance = GetComponent<SimulationAppearance>();
        if (appearance == null) appearance = gameObject.AddComponent<SimulationAppearance>();
        appearance.Initialize();
        ApplyFrame(data.frames[0], data.frames[0], 0f);
        var cameras = GetComponent<SimulationCameraController>();
        if (cameras == null) cameras = gameObject.AddComponent<SimulationCameraController>();
        cameras.Initialize(this, mapper, data.meta, Camera.main);
    }

    bool LoadData()
    {
        string cached = Path.Combine(Application.persistentDataPath, "last_simulation.json");
        string path = File.Exists(cached) ? cached : Path.Combine(Application.streamingAssetsPath, jsonFileName);
        if (!File.Exists(path))
        {
            Debug.LogError($"[SimPlayer] No existe el archivo '{path}'. Copia simulation_export.json a Assets/StreamingAssets/.");
            return false;
        }

        string json = File.ReadAllText(path);
        data = JsonUtility.FromJson<SimRoot>(json);
        if (data != null && data.meta != null && data.meta.authoritative_positions && data.meta.navigation_version < 4)
        {
            json = File.ReadAllText(Path.Combine(Application.streamingAssetsPath, jsonFileName));
            data = JsonUtility.FromJson<SimRoot>(json);
        }

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
        foreach (var anchor in FindObjectsByType<ZoneAnchor>())
        {
            if (string.IsNullOrEmpty(anchor.simId)) continue;
            stationAnchors[anchor.simId] = anchor;
        }
    }

    void Update()
    {
        if (data == null || data.frames == null || data.frames.Count < 2) return;

        if (data.meta.authoritative_positions && data.meta.navigation_version < 4)
        {
            LoadExport(File.ReadAllText(Path.Combine(Application.streamingAssetsPath, jsonFileName)));
            return;
        }
        if (!paused) AdvanceFrame();

        float lerp = timeInFrame / FrameDuration();
        int nextIndex = Mathf.Min(frameIndex + 1, data.frames.Count - 1);
        SimFrame frameA = data.frames[frameIndex];
        SimFrame frameB = data.frames[nextIndex];

        ApplyFrame(frameA, frameB, lerp);
    }

    public void Seek(int step)
    {
        if (data == null || data.frames.Count == 0) return;
        frameIndex = data.frames.FindLastIndex(frame => frame.t <= Mathf.Clamp(step, 0, LastStep));
        frameIndex = Mathf.Max(0, frameIndex);
        timeInFrame = 0;
        previousPoseTime = -1;
        ApplyFrame(data.frames[frameIndex], data.frames[Mathf.Min(frameIndex + 1, data.frames.Count - 1)], 0);
    }

    public void LoadExport(string json)
    {
        var loaded = JsonUtility.FromJson<SimRoot>(json);
        if (loaded == null || loaded.meta == null || loaded.frames == null || loaded.frames.Count == 0)
            throw new System.ArgumentException("El JSON no contiene una simulación válida.");
        if (loaded.meta.authoritative_positions && loaded.meta.navigation_version < 4)
            throw new System.ArgumentException("JSON antiguo sin la política actual de navegación y batería. Reinicia el servidor Python y genera otra corrida.");
        if (loaded.meta.n_agvs != 4) throw new System.ArgumentException("Se requieren cuatro AGV.");
        for (int i = 0; i < loaded.frames.Count; i++)
            if (loaded.frames[i].agvs == null || loaded.frames[i].agvs.Count != 4 ||
                (i > 0 && loaded.frames[i].t <= loaded.frames[i - 1].t))
                throw new System.ArgumentException("Frames inválidos o desordenados.");
        data = loaded;
        runtimeVisualsReady = false;
        frameIndex = 0;
        timeInFrame = 0;
        paused = true;
        loop = false;
        enabled = true;
        Seek(0);
        var cameras = GetComponent<SimulationCameraController>();
        if (cameras == null) cameras = gameObject.AddComponent<SimulationCameraController>();
        cameras.Initialize(this, CoordinateMapper.Instance, data.meta, Camera.main);
    }

    float FrameDuration()
    {
        int next = Mathf.Min(frameIndex + 1, data.frames.Count - 1);
        int steps = Mathf.Max(1, data.frames[next].t - data.frames[frameIndex].t);
        return Mathf.Max(0.001f, secondsPerFrame) * steps / Mathf.Max(1, data.meta.frame_sample);
    }

    void AdvanceFrame()
    {
        timeInFrame += Time.deltaTime * playbackSpeed;
        while (timeInFrame >= FrameDuration())
        {
            timeInFrame -= FrameDuration();
            if (frameIndex == data.frames.Count - 1)
            {
                if (loop) frameIndex = 0;
                else { timeInFrame = 0f; break; }
            }
            else frameIndex++;
            if (!loop && frameIndex == data.frames.Count - 1)
            {
                timeInFrame = 0f;
                break;
            }
        }
    }

    SimulationObstacles obstacles;
    float previousPoseTime = -1f;
    readonly Dictionary<string, AgvMechanics> mechanics = new Dictionary<string, AgvMechanics>();

    static string CarriedPallet(SimFrame frame, string agvId)
    {
        foreach (var mission in frame.missions)
            if (mission.assigned == agvId && mission.status == "ASIGNADA")
            {
                var pallet = FindPallet(frame, mission.pallet);
                if (pallet != null && pallet.state == "SIENDO_TRANSPORTADO") return pallet.id;
            }
        return null;
    }

    [System.NonSerialized] bool runtimeVisualsReady;

    void RestoreRuntimeVisuals()
    {
        if (runtimeVisualsReady) return;
        // Los diccionarios no sobreviven a la recarga de scripts, pero los GameObjects sí.
        // Retirar los visuales anteriores antes de reconstruir los índices.
        foreach (var root in gameObject.scene.GetRootGameObjects())
            if (System.Text.RegularExpressions.Regex.IsMatch(root.name, @"^(AGV_AGV-\d+|Pallet_P\d+)$"))
                RemoveRuntimeObject(root);
        foreach (var visual in agvVisuals.Values)
            if (visual != null) RemoveRuntimeObject(visual.gameObject);
        foreach (var visual in palletVisuals.Values)
            if (visual != null) RemoveRuntimeObject(visual.gameObject);
        agvVisuals.Clear();
        palletVisuals.Clear();
        mechanics.Clear();
        foreach (var old in GetComponents<SimulationObstacles>())
        {
            old.enabled = false;
            if (Application.isPlaying) Destroy(old); else DestroyImmediate(old);
        }
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            var child = transform.GetChild(i);
            if (child.name.StartsWith("Alarma_") || child.name.StartsWith("Peaton_"))
                RemoveRuntimeObject(child.gameObject);
        }
        obstacles = null;
        if (layout != null) layout.Apply(data, CoordinateMapper.Instance);
        IndexStationAnchors();
        runtimeVisualsReady = true;
    }

    static void RemoveRuntimeObject(GameObject value)
    {
        value.SetActive(false);
        if (Application.isPlaying) Destroy(value); else DestroyImmediate(value);
    }

    void ApplyFrame(SimFrame a, SimFrame b, float lerp)
    {
        RestoreRuntimeVisuals();
        var mapper = CoordinateMapper.Instance;
        float poseTime = Mathf.Lerp(a.t, b.t, lerp);
        bool reset = previousPoseTime < 0f || poseTime < previousPoseTime;
        float elapsed = reset ? 0f : Mathf.Max(0f, poseTime - previousPoseTime)
            * Mathf.Max(0.001f, secondsPerFrame) / Mathf.Max(1, data.meta.frame_sample);
        previousPoseTime = poseTime;
        var carriers = new Dictionary<string, AgvMechanics>();

        foreach (var agvA in a.agvs)
        {
            SimAgvFrame agvB = FindAgv(b, agvA.id) ?? agvA;
            Vector2 simPos = PlaybackPosition(ToV2(agvA.pos), ToV2(agvB.pos), lerp);
            Vector3 worldPos = mapper.SimToUnity(simPos);

            Transform visual = GetOrCreateAgv(agvA.id);
            Vector3 dir = mapper.SimToUnity(ToV2(agvB.pos)) - mapper.SimToUnity(ToV2(agvA.pos));
            if (reset && dir.sqrMagnitude < 0.0001f)
            {
                for (int i = data.frames.FindLastIndex(frame => frame.t < a.t); i >= 0; i--)
                {
                    var prior = FindAgv(data.frames[i], agvA.id);
                    if (prior == null) continue;
                    dir = worldPos - mapper.SimToUnity(ToV2(prior.pos));
                    if (dir.sqrMagnitude > 0.0001f) break;
                }
                if (dir.sqrMagnitude < 0.0001f) dir = mapper.Rotation * Vector3.forward;
            }
            string cargo = CarriedPallet(a, agvA.id);
            if (mechanics.TryGetValue(agvA.id, out var rig))
            {
                rig.Pose(worldPos, dir, cargo != null, elapsed, reset);
                if (cargo != null) carriers[cargo] = rig;
            }
            else
            {
                visual.position = worldPos;
                if (dir.sqrMagnitude > 0.0001f) visual.rotation = Quaternion.LookRotation(dir, Vector3.up);
            }

            TintAgv(visual, agvA.state);
        }

        foreach (var visual in palletVisuals.Values) visual.gameObject.SetActive(false);
        foreach (var pA in a.pallets)
        {
            if (pA.removed) continue;
            SimPalletFrame pB = FindPallet(b, pA.id) ?? pA;
            Vector2 simPos = pA.state == "SIENDO_TRANSPORTADO" && (pB.state == pA.state || pB.state == "ENTREGADO")
                ? PlaybackPosition(ToV2(pA.pos), ToV2(pB.pos), lerp) : ToV2(pA.pos);
            Transform visual = GetOrCreatePallet(pA.id);
            visual.gameObject.SetActive(true);
            if (carriers.TryGetValue(pA.id, out var carrier))
            {
                visual.position = carrier.CargoPosition;
                visual.rotation = carrier.CargoRotation;
            }
            else
            {
                if (layout != null && layout.TryGetStoragePose(pA.storage_zone, pA.storage_level, out var storage))
                {
                    visual.SetPositionAndRotation(storage.position, storage.rotation);
                }
                else
                {
                    visual.position = mapper.SimToUnity(simPos);
                    visual.rotation = Quaternion.identity;
                }
            }
        }

        foreach (var s in a.stations)
        {
            if (stationAnchors.TryGetValue(s.id, out var anchor) && anchor.stateRenderer != null)
                TintStation(anchor.stateRenderer, s.state);
        }

        if (obstacles == null)
        {
            obstacles = gameObject.AddComponent<SimulationObstacles>();
            obstacles.Initialize(layout, data.meta, mapper);
        }
        obstacles.Apply(a);
        CurrentAgvs = a.agvs;
        CurrentStep = a.t;
        CurrentStations = a.stations;
        CurrentMissions = a.missions;
        CurrentCompleted = a.completed;
    }

    bool warnedAboutSparseRoute;

    Vector2 PlaybackPosition(Vector2 from, Vector2 to, float fraction)
    {
        if (data.meta.authoritative_positions) return Vector2.Lerp(from, to, fraction);
        float clearance = data.meta.AgvClearance;
        foreach (var zone in data.zones)
        {
            if (!data.meta.IsObstacle(zone.name)) continue;
            if (!SimulationPlayback.IntersectsZone(from, to, zone, clearance)) continue;
            if (!warnedAboutSparseRoute)
            {
                Debug.LogWarning("[SimPlayer] Un tramo del JSON invade una zona. Se conserva la posición " +
                    "hasta la siguiente muestra para no atravesarlo. Usa m4_simulation.py con FRAME_SAMPLE = 1 y rutas libres.");
                warnedAboutSparseRoute = true;
            }
            return fraction >= 1f ? to : from;
        }
        return Vector2.Lerp(from, to, fraction);
    }

    Transform GetOrCreateAgv(string id)
    {
        if (agvVisuals.TryGetValue(id, out var existing)) return existing;

        Transform created = agvPrefabOverride != null
            ? Instantiate(agvPrefabOverride).transform
            : BuildAgvPlaceholder();
        var rig = created.GetComponent<AgvMechanics>();
        float diameter = data.meta.AgvDiameter * CoordinateMapper.Instance.scale;
        created = SimulationGeometry.PreparePlaybackVisual(created, diameter, rig != null ? rig.modelForwardYaw : 0f);
        if (rig != null)
        {
            rig.Initialize(created, diameter);
            mechanics[id] = rig;
        }
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(created.gameObject, gameObject.scene);
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
        created = SimulationGeometry.PreparePlaybackVisual(created, data.meta.PalletDiameter * CoordinateMapper.Instance.scale);
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(created.gameObject, gameObject.scene);
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
