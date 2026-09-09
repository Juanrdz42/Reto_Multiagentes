using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>Fachada continua del almacén, con huecos reales delante de los camiones.</summary>
public class DockWallVisual : MonoBehaviour
{
    public bool savedInScene;
    Transform facade;
    Material wallMaterial, trimMaterial;
    readonly List<Transform> parts = new List<Transform>();

    public void Apply(SimRoot data, CoordinateMapper mapper, List<SimulationLayout.Binding> bindings)
    {
        if (savedInScene) return;
        var docks = bindings.Where(b => b.model != null && b.simId.StartsWith("Dock"))
            .Select(b => SimulationGeometry.BoundsInFrame(b.model, mapper.Rotation))
            .OrderBy(b => b.min.z).ToList();
        if (docks.Count == 0) return;
        if (facade == null)
        {
            facade = new GameObject("WarehouseDockFacade").transform;
            facade.SetParent(transform, false);
            wallMaterial = MakeMaterial("DockFacade_Plaster", new Color32(222, 219, 210, 255));
            trimMaterial = MakeMaterial("DockFacade_Trim", new Color32(67, 73, 77, 255));
        }
        facade.SetPositionAndRotation(Vector3.zero, mapper.Rotation);
        facade.localScale = Vector3.one;
        float floor = docks[0].min.y;
        float top = docks.Max(b => b.max.y) + 2f;
        float front = docks[0].min.x;
        // Cube es el cuarto real, no una guía: quitar su pared exterior y
        // prolongaciones hasta el plano de los portones evita una doble pared.
        var room = gameObject.scene.GetRootGameObjects().FirstOrDefault(g => g.name == "Cube");
        if (room != null && room.GetComponent<MeshFilter>() != null)
        {
            var opening = room.GetComponent<RoomDockOpening>();
            if (opening == null) opening = room.AddComponent<RoomDockOpening>();
            opening.Apply(mapper.Rotation * new Vector3(front, floor, 0), mapper.Rotation * Vector3.right);
            top = Mathf.Max(top, opening.WallTop);
        }
        var inverse = Quaternion.Inverse(mapper.Rotation);
        float start = (inverse * mapper.SimToUnity(Vector2.zero)).z;
        float end = (inverse * mapper.SimToUnity(new Vector2(0, data.meta.height))).z;
        if (room != null && room.GetComponent<RoomDockOpening>() != null)
        {
            var roomBounds = SimulationGeometry.BoundsInFrame(room.transform, mapper.Rotation);
            start = Mathf.Min(start, roomBounds.min.z);
            end = Mathf.Max(end, roomBounds.max.z);
        }
        float wallStart = start;
        int index = 0;
        foreach (var dock in docks)
        {
            // Los paños entre puertas llegan al piso. Solo el dintel cruza cada abertura.
            Segment(ref index, "WallPier", front, start, dock.min.z, floor, top, wallMaterial);
            Segment(ref index, "DoorLintel", front, dock.min.z, dock.max.z,
                dock.max.y - 0.56f, top, wallMaterial);
            start = dock.max.z;
        }
        Segment(ref index, "WallEnd", front, start, end, floor, top, wallMaterial);
        Segment(ref index, "ContinuousTopTrim", front - 0.12f,
            wallStart, end, top - 0.12f, top, trimMaterial);
        for (int i = index; i < parts.Count; i++) parts[i].gameObject.SetActive(false);
    }

    void Segment(ref int index, string label, float x, float z0, float z1,
        float bottom, float top, Material material)
    {
        if (z1 <= z0 || top <= bottom) return;
        if (index == parts.Count)
        {
            var part = GameObject.CreatePrimitive(PrimitiveType.Cube).transform;
            part.SetParent(facade, false);
            parts.Add(part);
        }
        var segment = parts[index++];
        segment.gameObject.SetActive(true);
        segment.name = label;
        // Espesor hacia el exterior: la cara del almacén coincide con el portón.
        segment.localPosition = new Vector3(x + 0.12f, (bottom + top) / 2f, (z0 + z1) / 2f);
        segment.localRotation = Quaternion.identity;
        segment.localScale = new Vector3(0.24f, top - bottom, z1 - z0);
        segment.GetComponent<Renderer>().sharedMaterial = material;
    }

    static Material MakeMaterial(string label, Color color)
    {
        var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        material.name = label;
        material.SetColor("_BaseColor", color);
        material.SetFloat("_Smoothness", 0.12f);
        return material;
    }

    void OnDestroy()
    {
        if (savedInScene) return;
        if (wallMaterial != null) Release(wallMaterial);
        if (trimMaterial != null) Release(trimMaterial);
        if (facade != null) Release(facade.gameObject);
    }

    static void Release(Object value)
    {
        if (Application.isPlaying) Destroy(value); else DestroyImmediate(value);
    }
}
