using System.Collections.Generic;
using UnityEngine;

/// <summary>Representa los eventos del JSON sin añadir obstáculos a la simulación.</summary>
public class SimulationObstacles : MonoBehaviour
{
    readonly List<Transform> people = new List<Transform>();
    readonly Dictionary<string, GameObject> alarms = new Dictionary<string, GameObject>();
    CoordinateMapper mapper;
    SimMeta meta;
    Material orange, red, dark;
    public int VisiblePeople { get; private set; }
    public int VisibleAlarms { get; private set; }

    public void Initialize(SimulationLayout layout, SimMeta metadata, CoordinateMapper coordinates)
    {
        mapper = coordinates;
        meta = metadata;
        orange = MaterialFor(new Color(1f, 0.55f, 0.05f));
        red = MaterialFor(new Color(1f, 0.06f, 0.04f));
        dark = MaterialFor(new Color(0.12f, 0.16f, 0.22f));
        if (layout == null) return;
        foreach (var binding in layout.bindings)
        {
            if (binding.model == null) continue;
            var bounds = SimulationGeometry.BoundsInFrame(binding.model, Quaternion.identity);
            var light = new GameObject("Alarma_" + binding.simId);
            light.transform.SetParent(transform, false);
            light.transform.position = new Vector3(bounds.center.x, bounds.max.y + 1f, bounds.center.z);
            var mesh = new Mesh { name = "WarningTriangle" };
            mesh.vertices = new[] { new Vector3(-0.75f, -0.5f, 0), new Vector3(0, 0.8f, 0), new Vector3(0.75f, -0.5f, 0) };
            mesh.triangles = new[] { 0, 1, 2 };
            mesh.RecalculateNormals();
            light.AddComponent<MeshFilter>().sharedMesh = mesh;
            light.AddComponent<MeshRenderer>().sharedMaterial = orange;
            Primitive(light.transform, PrimitiveType.Cube, "Exclamation", new Vector3(0, 0.1f, -0.04f), new Vector3(0.13f, 0.48f, 0.03f), dark);
            Primitive(light.transform, PrimitiveType.Cube, "Dot", new Vector3(0, -0.28f, -0.04f), new Vector3(0.13f, 0.13f, 0.03f), dark);
            light.SetActive(false);
            alarms[binding.simId] = light;
        }
    }

    void LateUpdate()
    {
        var camera = Camera.main;
        if (camera == null) return;
        foreach (var marker in alarms.Values)
            if (marker.activeSelf) marker.transform.rotation = camera.transform.rotation;
    }

    public void Apply(SimFrame frame)
    {
        VisiblePeople = frame.pedestrians == null ? 0 : frame.pedestrians.Count;
        while (people.Count < VisiblePeople) people.Add(CreatePerson());
        for (int i = 0; i < people.Count; i++)
        {
            people[i].gameObject.SetActive(i < VisiblePeople);
            if (i < VisiblePeople)
                people[i].position = mapper.SimToUnity(new Vector2(frame.pedestrians[i].pos[0], frame.pedestrians[i].pos[1]));
        }
        VisibleAlarms = 0;
        foreach (var marker in alarms.Values) marker.SetActive(false);
        foreach (var station in frame.stations)
            if (station.state == "OUT_OF_SERVICE" && alarms.TryGetValue(station.id, out var marker))
            {
                marker.SetActive(true);
                VisibleAlarms++;
            }
    }

    Transform CreatePerson()
    {
        var root = new GameObject("Peaton_" + (people.Count + 1)).transform;
        root.SetParent(transform, false);
        float unit = mapper.scale * 6f;
        Primitive(root, PrimitiveType.Capsule, "Chaleco", Vector3.up * unit,
            new Vector3(0.65f, 0.55f, 0.45f) * unit, orange);
        Primitive(root, PrimitiveType.Sphere, "Casco", Vector3.up * unit * 1.8f, Vector3.one * unit * 0.48f, orange);
        for (int side = -1; side <= 1; side += 2)
            Primitive(root, PrimitiveType.Capsule, "Pierna", new Vector3(side * 0.18f, 0.35f, 0f) * unit,
                new Vector3(0.22f, 0.35f, 0.22f) * unit, dark);
        var ring = root.gameObject.AddComponent<LineRenderer>();
        ring.sharedMaterial = orange;
        ring.useWorldSpace = false;
        ring.loop = true;
        ring.widthMultiplier = 0.06f;
        ring.positionCount = 64;
        float radius = meta.pedestrian_radius * mapper.scale;
        for (int i = 0; i < ring.positionCount; i++)
        {
            float angle = i * Mathf.PI * 2f / ring.positionCount;
            ring.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, 0.04f, Mathf.Sin(angle) * radius));
        }
        return root;
    }

    static GameObject Primitive(Transform parent, PrimitiveType type, string name, Vector3 position, Vector3 size, Material material)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        go.transform.localScale = size;
        go.GetComponent<Collider>().enabled = false;
        go.GetComponent<Renderer>().sharedMaterial = material;
        return go;
    }

    static Material MaterialFor(Color color)
    {
        var material = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default"));
        material.color = color;
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        return material;
    }

    void OnDestroy()
    {
        foreach (var marker in alarms.Values)
            if (marker != null)
            {
                var mesh = marker.GetComponent<MeshFilter>().sharedMesh;
                if (Application.isPlaying) Destroy(mesh); else DestroyImmediate(mesh);
            }
        foreach (var material in new[] { orange, red, dark })
            if (material != null)
            {
                if (Application.isPlaying) Destroy(material);
                else DestroyImmediate(material);
            }
    }
}
