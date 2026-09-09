using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Vincula explícitamente los modelos de la escena a la geometría del JSON.
/// Los cambios al iniciar Play no alteran los prefabs ni la escena guardada.</summary>
[DisallowMultipleComponent]
public class SimulationLayout : MonoBehaviour
{
    [Serializable]
    public class Binding
    {
        public string simId;
        public Transform model;
    }

    public List<Binding> bindings = new List<Binding>();
    [Tooltip("Cubos que se usaban como guías y deben ocultarse al reproducir.")]
    public GameObject[] referenceObjects = Array.Empty<GameObject>();

    readonly Dictionary<Transform, Quaternion> originalRotations = new Dictionary<Transform, Quaternion>();

    readonly Dictionary<string, Pose> storagePoses = new Dictionary<string, Pose>();
    readonly Dictionary<Transform, Transform[]> shelves = new Dictionary<Transform, Transform[]>();

    public bool TryGetStoragePose(string zone, out Pose pose)
    {
        pose = default;
        return !string.IsNullOrEmpty(zone) && storagePoses.TryGetValue(zone, out pose);
    }

    public bool TryGetStoragePose(string zone, int level, out Pose pose)
    {
        if (!string.IsNullOrEmpty(zone) && zone.StartsWith("Rack"))
            return storagePoses.TryGetValue(zone + "#" + Mathf.Clamp(level, 0, 1), out pose);
        return TryGetStoragePose(zone, out pose);
    }

    public void Apply(SimRoot data, CoordinateMapper mapper)
    {
        foreach (var reference in referenceObjects)
            if (reference != null) reference.SetActive(false);

        foreach (var binding in bindings)
        {
            if (binding.model == null) continue;
            var zone = data.zones.Find(z => z.name == binding.simId);
            var charger = data.chargers.Find(c => c.name == binding.simId);
            if (zone == null && charger == null)
            {
                Debug.LogError($"[SimulationLayout] No existe '{binding.simId}' en el JSON.");
                continue;
            }

            // Los modelos originales están orientados a 0/90/180 grados.
            // Medir en el marco del layout permite ajustar también Rack D.
            if (!originalRotations.TryGetValue(binding.model, out var originalRotation))
            {
                originalRotation = binding.model.rotation;
                originalRotations.Add(binding.model, originalRotation);
            }
            binding.model.rotation = mapper.Rotation * originalRotation;
            Bounds bounds = SimulationGeometry.BoundsInFrame(binding.model, mapper.Rotation);
            if (zone != null)
            {
                float fx = zone.width * mapper.scale / Mathf.Max(bounds.size.x, 0.0001f);
                float fz = zone.height * mapper.scale / Mathf.Max(bounds.size.z, 0.0001f);
                Quaternion relative = Quaternion.Inverse(mapper.Rotation) * binding.model.rotation;
                Vector3 factors = new Vector3(
                    AxisFactor(relative * Vector3.right, fx, fz),
                    AxisFactor(relative * Vector3.up, fx, fz),
                    AxisFactor(relative * Vector3.forward, fx, fz));
                binding.model.localScale = Vector3.Scale(binding.model.localScale, factors);
                bounds = SimulationGeometry.BoundsInFrame(binding.model, mapper.Rotation);
            }
            Vector2 point = zone != null
                ? new Vector2(zone.x + zone.width / 2f, zone.y + zone.height / 2f)
                : new Vector2(charger.pos[0], charger.pos[1]);
            Vector3 bottomCenter = mapper.Rotation * new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            binding.model.position += mapper.SimToUnity(point) - bottomCenter;

            if (zone != null && zone.name.StartsWith("Dock"))
            {
                var truck = binding.model.GetComponent<TruckDockVisual>();
                if (truck == null) truck = binding.model.gameObject.AddComponent<TruckDockVisual>();
                truck.Build(SimulationGeometry.BoundsInFrame(binding.model, mapper.Rotation), mapper, zone.name);
            }

            // El ancla representa ahora el punto de atención, separado del centro visual.
            var station = data.stations.Find(s => s.name == binding.simId);
            Vector2 service = station != null ? new Vector2(station.pos[0], station.pos[1]) : point;
            var anchor = binding.model.GetComponent<ZoneAnchor>();
            if (anchor == null) anchor = binding.model.gameObject.AddComponent<ZoneAnchor>();
            anchor.simId = binding.simId;
            anchor.servicePoint = mapper.SimToUnity(service);
        }
        // Una repisa real sostiene la caja entre los marcos del rack.
        foreach (var binding in bindings)
        {
            if (binding.model == null || !binding.simId.StartsWith("Rack")) continue;
            Bounds bounds = SimulationGeometry.BoundsInFrame(binding.model, mapper.Rotation);
            if (!shelves.TryGetValue(binding.model, out var tiers))
            {
                tiers = new Transform[2];
                var material = binding.model.GetComponentInChildren<Renderer>().sharedMaterial;
                for (int level = 0; level < 2; level++)
                {
                    tiers[level] = binding.model.Find(level == 0 ? "StorageShelf_Lower" : "StorageShelf_Upper");
                    if (tiers[level] != null) continue;
                    tiers[level] = GameObject.CreatePrimitive(PrimitiveType.Cube).transform;
                    tiers[level].name = level == 0 ? "StorageShelf_Lower" : "StorageShelf_Upper";
                    tiers[level].GetComponent<Renderer>().sharedMaterial = material;
                }
                shelves.Add(binding.model, tiers);
            }
            for (int level = 0; level < 2; level++)
            {
                var shelf = tiers[level];
                shelf.SetParent(null, true);
                shelf.SetPositionAndRotation(mapper.Rotation * new Vector3(bounds.center.x,
                    bounds.min.y + bounds.size.y * (level == 0 ? 0.12f : 0.88f), bounds.center.z), mapper.Rotation);
                shelf.localScale = new Vector3(bounds.size.x * 0.9f, 0.08f, bounds.size.z * 0.9f);
                shelf.SetParent(binding.model, true);
            }
        }
        var dockWall = GetComponent<DockWallVisual>();
        if (dockWall == null) dockWall = gameObject.AddComponent<DockWallVisual>();
        dockWall.Apply(data, mapper, bindings);
        Physics.SyncTransforms();
        storagePoses.Clear();
        foreach (var binding in bindings)
        {
            if (binding.model == null || data.zones.Find(z => z.name == binding.simId) == null) continue;
            if (shelves.TryGetValue(binding.model, out var levels))
                for (int level = 0; level < levels.Length; level++)
                {
                    var tierBounds = SimulationGeometry.BoundsInFrame(levels[level], mapper.Rotation);
                    var top = mapper.Rotation * new Vector3(tierBounds.center.x, tierBounds.max.y, tierBounds.center.z);
                    storagePoses[binding.simId + "#" + level] = new Pose(top, mapper.Rotation);
                }
            Bounds bounds = SimulationGeometry.BoundsInFrame(binding.model, mapper.Rotation);
            Vector3 center = mapper.Rotation * bounds.center;
            Vector3 support = center;
            bool found = false;
            // Muestrear también junto al centro evita huecos entre los rodillos.
            foreach (float dx in new[] { 0f, -0.15f, 0.15f })
            foreach (float dz in new[] { 0f, -0.15f, 0.15f })
            {
                var ray = new Ray(new Vector3(center.x + dx, bounds.max.y + 1f, center.z + dz), Vector3.down);
                foreach (var collider in binding.model.GetComponentsInChildren<Collider>())
                    if (collider.enabled && collider.Raycast(ray, out var hit, bounds.size.y + 2f)
                        && (!found || hit.point.y > support.y))
                    {
                        found = true;
                        support = hit.point;
                    }
            }
            if (!found)
            {
                Debug.LogError($"[SimulationLayout] No hay superficie de apoyo en {binding.simId}.");
                continue;
            }
            storagePoses[binding.simId] = new Pose(support, mapper.Rotation);
        }
        Debug.Log($"[SimulationLayout] {bindings.Count} modelos alineados con el JSON; escala uniforme {mapper.scale}.");
    }

    static float AxisFactor(Vector3 axis, float fx, float fz)
        => Mathf.Abs(axis.x) * fx + Mathf.Abs(axis.y) + Mathf.Abs(axis.z) * fz;
}

public static class SimulationGeometry
{
    // Mesh bounds transformados: no dependen de que el renderer ya haya actualizado su AABB.
    public static Bounds BoundsInFrame(Transform root, Quaternion rotation)
    {
        Quaternion inverse = Quaternion.Inverse(rotation);
        Bounds result = new Bounds(inverse * root.position, Vector3.zero);
        bool first = true;
        foreach (var filter in root.GetComponentsInChildren<MeshFilter>())
        {
            if (filter.sharedMesh == null) continue;
            var renderer = filter.GetComponent<Renderer>();
            if (renderer != null && !renderer.enabled) continue;
            Bounds b = filter.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 p = b.center + Vector3.Scale(b.extents,
                    new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                p = inverse * filter.transform.TransformPoint(p);
                if (first) { result = new Bounds(p, Vector3.zero); first = false; }
                else result.Encapsulate(p);
            }
        }
        return result;
    }

    // Un padre centrado en el suelo elimina el desplazamiento del pivote del prefab.
    // La diagonal respeta el diámetro que Python usa para planear las rutas.
    public static Transform PreparePlaybackVisual(Transform model, float maxDiameter, float forwardYaw = 0f)
    {
        model.position = Vector3.zero;
        model.rotation = Quaternion.Euler(0f, forwardYaw, 0f);
        Bounds b = BoundsInFrame(model, Quaternion.identity);
        float diagonal = new Vector2(b.size.x, b.size.z).magnitude;
        if (diagonal > 0.0001f) model.localScale *= maxDiameter / diagonal;
        b = BoundsInFrame(model, Quaternion.identity);
        var root = new GameObject(model.name + "_Playback").transform;
        model.SetParent(root, true);
        model.localPosition -= new Vector3(b.center.x, b.min.y, b.center.z);
        foreach (var body in model.GetComponentsInChildren<Rigidbody>())
        { body.isKinematic = true; body.useGravity = false; }
        foreach (var collider in model.GetComponentsInChildren<Collider>()) collider.enabled = false;
        return root;
    }
}
