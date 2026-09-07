using System.IO;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Dibuja, SOLO en el editor (Scene view, como Gizmos), un "cuarto placeholder"
/// con la posicion y medida EXACTA de cada zona (racks/docks/produccion),
/// cargador y estacion de simulation_export.json, mas cajas de referencia
/// del tamaño de un AGV y un pallet. No crea ningun GameObject real ni toca
/// tu escena: es solo una guia visual para que muevas/escales tus propios
/// modelos (Rack, Caja, Camion1, Estacion Carga, Inbound Azul/Negro, AGV)
/// hasta que calcen con los recuadros.
///
/// Como usarlo:
/// 1) Agrega este componente a cualquier GameObject de la escena (puede ser
///    el mismo "SimulationManager").
/// 2) En la vista Scene (no Game) veras los recuadros. No hace falta darle
///    Play.
/// 3) Ajusta `scale`, `origin` y `rotationY` si los recuadros no quedan
///    alineados con tu cuarto real (ver los valores por defecto abajo: estan
///    calculados para calzar con "Cuarto (1)", el shape de ProBuilder que ya
///    tienes en SampleScene.unity).
/// 4) Cuando termines de acomodar tus modelos, puedes dejar este componente
///    (no afecta el juego, OnDrawGizmos no corre fuera del editor) o borrarlo.
/// </summary>
[ExecuteAlways]
public class ReferenceLayoutGizmo : MonoBehaviour
{
    [Header("Datos")]
    public string jsonFileName = "simulation_export.json";

    [Header("Ajuste al cuarto real")]
    [Tooltip("Unidades Unity por unidad de simulacion.\n" +
             "Valor por defecto calculado para que el layout (450x300 unidades de " +
             "simulacion) quepa DENTRO de \"Cuarto (1)\" (tamaño real medido: " +
             "87.43 x 55.24 unidades Unity, resultado de su Shape Size * su Transform " +
             "Scale) sin deformarse: se uso el eje mas restrictivo (Z).")]
    public float scale = 0.18415f;

    [Tooltip("Posicion de mundo donde cae la esquina (0,0) de la simulacion.\n" +
             "Valor por defecto calculado para que el layout quede centrado dentro " +
             "de \"Cuarto (1)\" (centro real: 6, 0, -9.3).")]
    public Vector3 origin = new Vector3(-35.43f, 0f, -36.92f);

    [Tooltip("Rotacion en Y de todo el layout de referencia, por si tu cuarto no " +
             "esta alineado a los ejes de mundo.")]
    public float rotationY = 0f;

    public float floorHeight = 0f;

    [Header("Que dibujar")]
    public bool showOverallBounds = true;
    public bool showZones = true;
    public bool showChargers = true;
    public bool showStations = true;
    public bool showAgvFootprints = true;
    public bool showPalletFootprint = true;
    public bool showLabels = true;

    // Mismo tamaño que los placeholders que ya genera SimPlayer.cs
    // (BuildAgvPlaceholder / BuildPalletPlaceholder), para que la referencia
    // sea consistente con lo que se ve al darle Play.
    static readonly Vector3 AgvFootprint = new Vector3(0.4f, 0.25f, 0.6f);
    static readonly Vector3 PalletFootprint = new Vector3(0.3f, 0.2f, 0.3f);

    SimRoot data;

    void OnEnable() => TryLoad();

    [ContextMenu("Recargar simulation_export.json")]
    void TryLoad()
    {
        string path = Path.Combine(Application.streamingAssetsPath, jsonFileName);
        if (!File.Exists(path))
        {
            Debug.LogWarning($"[ReferenceLayoutGizmo] No se encontro '{path}'.");
            data = null;
            return;
        }
        data = JsonUtility.FromJson<SimRoot>(File.ReadAllText(path));
    }

    Vector3 SimToWorld(float x, float y)
    {
        Vector3 local = new Vector3(x * scale, 0f, y * scale);
        Vector3 rotated = Quaternion.Euler(0f, rotationY, 0f) * local;
        return origin + rotated + Vector3.up * floorHeight;
    }

    void OnDrawGizmos()
    {
        if (data == null) TryLoad();
        if (data == null || data.meta == null) return;

        if (showOverallBounds) DrawOverallBounds();
        if (showZones && data.zones != null) DrawZones();
        if (showChargers && data.chargers != null) DrawChargers();
        if (showStations && data.stations != null) DrawStations();
        if (showAgvFootprints) DrawAgvFootprints();
        if (showPalletFootprint) DrawSamplePallet();
    }

    void DrawOverallBounds()
    {
        Vector3 center = SimToWorld(data.meta.width * 0.5f, data.meta.height * 0.5f);
        Vector3 size = new Vector3(data.meta.width * scale, 0.02f, data.meta.height * scale);
        Gizmos.color = Color.white;
        DrawRotatedWireCube(center, size);
        Label(center + Vector3.up * 0.5f,
            $"Almacen completo: {data.meta.width * scale:0.00} x {data.meta.height * scale:0.00} u");
    }

    void DrawZones()
    {
        foreach (var z in data.zones)
        {
            Vector3 center = SimToWorld(z.x + z.width * 0.5f, z.y + z.height * 0.5f);
            Vector3 size = new Vector3(z.width * scale, 0.15f, z.height * scale);
            Gizmos.color = ColorFromName(z.color);
            DrawRotatedWireCube(center, size);
            if (showLabels)
                Label(center, $"{z.name}\n{z.width * scale:0.00} x {z.height * scale:0.00} u");
        }
    }

    void DrawChargers()
    {
        foreach (var c in data.chargers)
        {
            Vector3 pos = SimToWorld(c.pos[0], c.pos[1]);
            Gizmos.color = new Color(1f, 0.84f, 0f);
            Gizmos.DrawWireSphere(pos + Vector3.up * 0.2f, 0.15f);
            if (showLabels) Label(pos + Vector3.up * 0.45f, c.name);
        }
    }

    void DrawStations()
    {
        foreach (var s in data.stations)
        {
            Vector3 pos = SimToWorld(s.pos[0], s.pos[1]);
            Gizmos.color = s.kind == "DOCK" ? new Color(0.4f, 0.8f, 0.4f) : new Color(0.9f, 0.6f, 0.3f);
            Gizmos.DrawWireSphere(pos + Vector3.up * 0.05f, 0.08f);
            if (showLabels) Label(pos + Vector3.up * 0.25f, s.name);
        }
    }

    void DrawAgvFootprints()
    {
        if (data.frames == null || data.frames.Count == 0) return;
        Gizmos.color = Color.cyan;
        foreach (var a in data.frames[0].agvs)
        {
            Vector3 center = SimToWorld(a.pos[0], a.pos[1]) + Vector3.up * (AgvFootprint.y * 0.5f);
            DrawRotatedWireCube(center, AgvFootprint);
            if (showLabels)
                Label(center + Vector3.up * 0.45f,
                    $"{a.id} (inicio)\n{AgvFootprint.x:0.00}x{AgvFootprint.y:0.00}x{AgvFootprint.z:0.00} u");
        }
    }

    void DrawSamplePallet()
    {
        if (data.zones == null || data.zones.Count == 0) return;
        var z = data.zones[0];
        Vector3 center = SimToWorld(z.x - 10f, z.y + z.height * 0.5f) + Vector3.up * (PalletFootprint.y * 0.5f);
        Gizmos.color = new Color(0.82f, 0.41f, 0.12f);
        DrawRotatedWireCube(center, PalletFootprint);
        if (showLabels)
            Label(center + Vector3.up * 0.35f,
                $"Pallet (muestra)\n{PalletFootprint.x:0.00}x{PalletFootprint.y:0.00}x{PalletFootprint.z:0.00} u");
    }

    void DrawRotatedWireCube(Vector3 center, Vector3 size)
    {
        Matrix4x4 prev = Gizmos.matrix;
        Gizmos.matrix = Matrix4x4.TRS(center, Quaternion.Euler(0f, rotationY, 0f), Vector3.one);
        Gizmos.DrawWireCube(Vector3.zero, size);
        Gizmos.matrix = prev;
    }

    static Color ColorFromName(string name)
    {
        switch (name)
        {
            case "cornflowerblue": return new Color(0.39f, 0.58f, 0.93f);
            case "sandybrown": return new Color(0.96f, 0.64f, 0.38f);
            case "yellowgreen": return new Color(0.6f, 0.8f, 0.2f);
            default: return Color.gray;
        }
    }

    void Label(Vector3 pos, string text)
    {
#if UNITY_EDITOR
        if (!showLabels) return;
        var style = new GUIStyle { fontSize = 10 };
        style.normal.textColor = Color.white;
        Handles.Label(pos, text, style);
#endif
    }
}
