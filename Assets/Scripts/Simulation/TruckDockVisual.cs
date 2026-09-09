using System.Collections.Generic;
using UnityEngine;

/// <summary>Caja de camión abierta hacia -X de Python, dentro de la huella del dock.</summary>
public class TruckDockVisual : MonoBehaviour
{
    readonly List<Material> materials = new List<Material>();
    [SerializeField] Transform shell, apron;
    public bool savedInScene;
    public Vector3 CargoFloor { get; private set; }

    public void Build(Bounds bounds, CoordinateMapper mapper, string id)
    {
        if (shell != null)
        {
            CargoFloor = shell.TransformPoint(new Vector3(0, 0.14f, 0));
            PositionApron(bounds, mapper);
            return;
        }
        foreach (var renderer in GetComponentsInChildren<MeshRenderer>()) renderer.enabled = false;
        foreach (var collider in GetComponentsInChildren<Collider>()) collider.enabled = false;

        var panel = Finish("TruckPanel", new Color32(222, 219, 210, 255), 0.15f);
        var frame = Finish("TruckFrame", new Color32(67, 73, 77, 255), 0.55f);
        var rubber = Finish("TruckFloor", new Color32(38, 40, 41, 255), 0f);
        float depth = bounds.size.x, width = bounds.size.z;
        float height = Mathf.Min(bounds.size.y, width * 0.9f);
        const float wall = 0.12f, deck = 0.14f;
        shell = new GameObject("TruckCargoBody").transform;
        shell.SetPositionAndRotation(mapper.Rotation * new Vector3(bounds.center.x, bounds.min.y, bounds.center.z), mapper.Rotation);
        // Construcción en unidades mundo y posterior parentado, para conservar las medidas.
        Part(shell, "CargoFloor", new Vector3(0, deck / 2f, 0), new Vector3(depth, deck, width), rubber);
        Part(shell, "BackWall", new Vector3(depth / 2f - wall / 2f, height / 2f, 0), new Vector3(wall, height, width), panel);
        for (int side = -1; side <= 1; side += 2)
        {
            float z = side * (width / 2f - wall / 2f);
            Part(shell, "SideWall", new Vector3(0, height / 2f, z), new Vector3(depth, height, wall), panel);
            Part(shell, "TopRail", new Vector3(0, height - 0.07f, z), new Vector3(depth, 0.14f, wall), frame);
            Part(shell, "DoorPost", new Vector3(-depth / 2f + wall / 2f, height / 2f, z), new Vector3(wall, height, wall), frame);
            Part(shell, "WallRubRail", new Vector3(0, height * 0.3f, z - side * wall / 2f), new Vector3(depth - wall, 0.08f, 0.025f), frame);
        }
        float front = -depth / 2f + wall / 2f;
        Part(shell, "DoorHeader", new Vector3(front, height - 0.28f, 0), new Vector3(wall, 0.56f, width - wall * 2f), panel);
        // La persiana queda recogida; la abertura permite ver la carga en el interior.
        for (int i = 0; i < 4; i++)
            Part(shell, "ShutterSlat", new Vector3(front - 0.025f, height - 0.1f - i * 0.09f, 0), new Vector3(0.025f, 0.025f, width - 0.6f), frame);
        var sign = new GameObject("DockId").AddComponent<TextMesh>();
        sign.transform.SetParent(shell, false);
        sign.transform.localPosition = new Vector3(-depth / 2f - 0.01f, height - 0.58f, 0);
        sign.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
        sign.text = id.Replace("Dock ", "D");
        sign.anchor = TextAnchor.MiddleCenter;
        sign.alignment = TextAlignment.Center;
        sign.characterSize = 0.14f;
        sign.fontSize = 64;
        sign.color = new Color32(40, 45, 50, 255);
        CargoFloor = shell.TransformPoint(new Vector3(0, deck, 0));
        shell.SetParent(transform, true);

        // Tapete transitable, fuera de la huella de paredes del camión.
        apron = new GameObject("LoadingMat_" + id).transform;
        PositionApron(bounds, mapper);
        var mat = Part(apron, "RubberMat", Vector3.zero, new Vector3(1.4f, 0.035f, width * 0.78f), rubber);
        mat.GetComponent<Collider>().enabled = false;
    }

    void PositionApron(Bounds bounds, CoordinateMapper mapper)
    {
        if (apron != null)
            apron.SetPositionAndRotation(mapper.Rotation * new Vector3(bounds.min.x - 0.7f, bounds.min.y + 0.025f, bounds.center.z), mapper.Rotation);
    }

    Material Finish(string name, Color color, float metallic)
    {
        var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        material.name = name;
        material.SetColor("_BaseColor", color);
        material.SetFloat("_Metallic", metallic);
        material.SetFloat("_Smoothness", metallic > 0 ? 0.3f : 0.05f);
        materials.Add(material);
        return material;
    }

    static GameObject Part(Transform parent, string name, Vector3 position, Vector3 scale, Material material)
    {
        var part = GameObject.CreatePrimitive(PrimitiveType.Cube);
        part.name = name;
        part.transform.SetParent(parent, false);
        part.transform.localPosition = position;
        part.transform.localScale = scale;
        part.GetComponent<Renderer>().sharedMaterial = material;
        return part;
    }

    void OnDestroy()
    {
        if (savedInScene) return;
        if (apron != null) Release(apron.gameObject);
        foreach (var material in materials) if (material != null) Release(material);
    }

    static void Release(Object value)
    {
        if (Application.isPlaying) Destroy(value); else DestroyImmediate(value);
    }
}
