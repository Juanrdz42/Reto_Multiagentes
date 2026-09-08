using System.IO;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>Guía de la geometría JSON usando el mismo CoordinateMapper que la reproducción.</summary>
[RequireComponent(typeof(CoordinateMapper))]
[ExecuteAlways]
public class ReferenceLayoutGizmo : MonoBehaviour
{
    [Header("Datos")]
    public string jsonFileName = "simulation_export.json";

    CoordinateMapper Mapper => GetComponent<CoordinateMapper>();
    float scale => Mapper.scale;
    float rotationY => Mapper.rotationY;

    [Header("Que dibujar")]
    public bool showOverallBounds = true;
    public bool showZones = true;
    public bool showChargers = true;
    public bool showStations = true;
    public bool showAgvFootprints = true;
    public bool showPalletFootprint = true;
    public bool showLabels = true;

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
        return Mapper.SimToUnity(new Vector2(x, y));
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
            Vector3 center = SimToWorld(a.pos[0], a.pos[1]);
            float radius = data.meta.AgvClearance * scale;
            Gizmos.DrawWireSphere(center, radius);
            if (showLabels) Label(center + Vector3.up * 0.45f, $"{a.id} (inicio), radio máximo {radius:0.00} u");
        }
    }

    void DrawSamplePallet()
    {
        if (data.zones == null || data.zones.Count == 0) return;
        var z = data.zones[0];
        Vector3 center = SimToWorld(z.x + z.width + data.meta.cell_size, z.y + z.height * 0.5f);
        float radius = data.meta.PalletDiameter * 0.5f * scale;
        Gizmos.color = new Color(0.82f, 0.41f, 0.12f);
        Gizmos.DrawWireSphere(center, radius);
        if (showLabels) Label(center + Vector3.up * 0.35f, $"Pallet (muestra), radio máximo {radius:0.00} u");
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
