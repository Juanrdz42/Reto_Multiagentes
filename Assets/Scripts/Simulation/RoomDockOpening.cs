using System.Collections.Generic;
using UnityEngine;

/// <summary>Recorta una copia del cuarto en el plano de los portones, conservando sus materiales y UV.</summary>
public class RoomDockOpening : MonoBehaviour
{
    public bool savedInScene;
    Mesh original, clipped;
    MeshFilter filter;
    MeshCollider meshCollider;
    Mesh originalCollider;
    public float WallTop { get; private set; }

    struct Vertex
    {
        public Vector3 p, n;
        public Vector2 uv;
        public Vector4 tangent;
        public Color color;
        public static Vertex Mix(Vertex a, Vertex b, float t) => new Vertex {
            p = Vector3.Lerp(a.p, b.p, t), n = Vector3.Lerp(a.n, b.n, t).normalized,
            uv = Vector2.Lerp(a.uv, b.uv, t), tangent = Vector4.Lerp(a.tangent, b.tangent, t),
            color = Color.Lerp(a.color, b.color, t)
        };
    }

    public void Apply(Vector3 point, Vector3 outward)
    {
        if (savedInScene) { WallTop = GetComponent<Renderer>().bounds.max.y; return; }
        if (filter == null)
        {
            filter = GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null) return;
            original = filter.sharedMesh;
            meshCollider = GetComponent<MeshCollider>();
            if (meshCollider != null) originalCollider = meshCollider.sharedMesh;
            WallTop = GetComponent<Renderer>().bounds.max.y;
        }
        var positions = original.vertices;
        var normals = original.normals;
        var uv = original.uv;
        var tangents = original.tangents;
        var colors = original.colors;
        var output = new List<Vertex>();
        var submeshes = new List<int[]>();
        Vertex Read(int i) => new Vertex {
            p = positions[i], n = normals.Length == positions.Length ? normals[i] : Vector3.up,
            uv = uv.Length == positions.Length ? uv[i] : Vector2.zero,
            tangent = tangents.Length == positions.Length ? tangents[i] : new Vector4(1, 0, 0, 1),
            color = colors.Length == positions.Length ? colors[i] : Color.white
        };
        float Distance(Vertex v) => Vector3.Dot(transform.TransformPoint(v.p) - point, outward);
        for (int sub = 0; sub < original.subMeshCount; sub++)
        {
            var triangles = original.GetTriangles(sub);
            var indices = new List<int>();
            for (int i = 0; i < triangles.Length; i += 3)
            {
                var polygon = new List<Vertex>();
                var previous = Read(triangles[i + 2]);
                float previousDistance = Distance(previous);
                for (int j = 0; j < 3; j++)
                {
                    var current = Read(triangles[i + j]);
                    float distance = Distance(current);
                    if ((distance <= 0) != (previousDistance <= 0))
                        polygon.Add(Vertex.Mix(previous, current, previousDistance / (previousDistance - distance)));
                    if (distance <= 0) polygon.Add(current);
                    previous = current;
                    previousDistance = distance;
                }
                for (int j = 1; j + 1 < polygon.Count; j++)
                {
                    indices.Add(output.Count); output.Add(polygon[0]);
                    indices.Add(output.Count); output.Add(polygon[j]);
                    indices.Add(output.Count); output.Add(polygon[j + 1]);
                }
            }
            submeshes.Add(indices.ToArray());
        }
        var mesh = new Mesh { name = original.name + "_DockOpening", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        mesh.SetVertices(output.ConvertAll(v => v.p));
        mesh.SetNormals(output.ConvertAll(v => v.n));
        mesh.SetUVs(0, output.ConvertAll(v => v.uv));
        mesh.SetTangents(output.ConvertAll(v => v.tangent));
        mesh.SetColors(output.ConvertAll(v => v.color));
        mesh.subMeshCount = submeshes.Count;
        for (int sub = 0; sub < submeshes.Count; sub++) mesh.SetTriangles(submeshes[sub], sub);
        mesh.RecalculateBounds();
        filter.sharedMesh = mesh;
        if (meshCollider != null) meshCollider.sharedMesh = mesh;
        if (clipped != null) Release(clipped);
        clipped = mesh;
    }

    void OnDestroy()
    {
        if (savedInScene) return;
        if (filter != null && original != null) filter.sharedMesh = original;
        if (meshCollider != null) meshCollider.sharedMesh = originalCollider;
        if (clipped != null) Release(clipped);
    }

    static void Release(Object value)
    {
        if (Application.isPlaying) Destroy(value); else DestroyImmediate(value);
    }
}
