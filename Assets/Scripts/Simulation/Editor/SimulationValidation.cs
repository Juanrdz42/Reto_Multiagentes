using System;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

public static class SimulationValidation
{
    [MenuItem("Simulation/Validate JSON and scene")]
    public static void Run()
    {
        var scene = EditorSceneManager.OpenPreviewScene("Assets/Scenes/SampleScene.unity");
        try
        {
            var roots = scene.GetRootGameObjects();
            var mapper = roots.SelectMany(r => r.GetComponentsInChildren<CoordinateMapper>()).Single();
            var layout = roots.SelectMany(r => r.GetComponentsInChildren<SimulationLayout>()).Single();
            var data = JsonUtility.FromJson<SimRoot>(File.ReadAllText("Assets/StreamingAssets/simulation_export.json"));
            Require(layout.bindings.Count == data.zones.Count + data.chargers.Count, "Faltan modelos vinculados.");
            Require(layout.bindings.All(b => b.model != null), "Hay referencias a modelos sin resolver.");
            Require(layout.bindings.Select(b => b.simId).Distinct().Count() == layout.bindings.Count, "Hay vínculos duplicados.");
            layout.Apply(data, mapper);
            CheckLayout(data, layout, mapper);
            // Volver a aplicar no debe acumular traslación, rotación ni escala.
            layout.Apply(data, mapper);
            CheckLayout(data, layout, mapper);
            mapper.rotationY = 30f;
            layout.Apply(data, mapper);
            CheckLayout(data, layout, mapper);
            mapper.rotationY = 0f;
            layout.Apply(data, mapper);
            CheckLayout(data, layout, mapper);

            int blocked = 0;
            foreach (var pair in data.frames.Zip(data.frames.Skip(1), (a, b) => (a, b)))
            {
                Require(pair.b.t > pair.a.t, "Los tiempos no están ordenados.");
                foreach (var agv in pair.a.agvs)
                {
                    var next = pair.b.agvs.Single(a => a.id == agv.id);
                    var from = new Vector2(agv.pos[0], agv.pos[1]);
                    var to = new Vector2(next.pos[0], next.pos[1]);
                    Require(from.x >= 0 && from.x <= data.meta.width && from.y >= 0 && from.y <= data.meta.height,
                        "Un AGV sale del entorno Python.");
                    foreach (var zone in data.zones.Where(z => data.meta.IsObstacle(z.name)))
                        if (SimulationPlayback.IntersectsZone(from, to, zone, data.meta.AgvClearance)) blocked++;
                }
            }
            Require(blocked == 0, $"{blocked} segmentos necesitan saltarse la interpolación: exporta todos los pasos o revisa Python.");
            CheckPlayback(roots.SelectMany(r => r.GetComponentsInChildren<SimPlayer>()).Single(), data, mapper);
            CheckMechanics(data.meta.AgvDiameter * mapper.scale);
            CheckVisual("Assets/AGV.prefab", data.meta.AgvDiameter * mapper.scale);
            CheckVisual("Assets/Caja.prefab", data.meta.PalletDiameter * mapper.scale);

            var rack = new SimZone { x = 150, y = 200, width = 100, height = 22 };
            Require(SimulationPlayback.IntersectsZone(new Vector2(242.5f, 197.5f), new Vector2(252.5f, 202.5f), rack, 0f),
                "No se detectó el recorte de esquina del JSON anterior.");
            Require(!SimulationPlayback.IntersectsZone(new Vector2(242.5f, 197.5f), new Vector2(252.5f, 197.5f), rack, 2f),
                "Un pasillo libre fue marcado como obstáculo.");
            Debug.Log($"M4 VALIDATION PASSED: {layout.bindings.Count} modelos, {data.frames.Count} frames; " +
                "huellas, anclas, rotación, pivotes y trayectorias comprobados.");
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }

    static void CheckLayout(SimRoot data, SimulationLayout layout, CoordinateMapper mapper)
    {
        foreach (var binding in layout.bindings)
        {
            var zone = data.zones.Find(z => z.name == binding.simId);
            var charger = data.chargers.Find(c => c.name == binding.simId);
            Bounds bounds = SimulationGeometry.BoundsInFrame(binding.model, mapper.Rotation);
            Vector2 center = zone == null ? new Vector2(charger.pos[0], charger.pos[1])
                : new Vector2(zone.x + zone.width / 2f, zone.y + zone.height / 2f);
            Vector3 actual = mapper.Rotation * new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            Require(Vector3.Distance(actual, mapper.SimToUnity(center)) < 0.002f, binding.simId + ": centro/piso incorrecto.");
            if (zone != null)
            {
                Require(Mathf.Abs(bounds.size.x - zone.width * mapper.scale) < 0.002f, binding.simId + ": ancho incorrecto.");
                Require(Mathf.Abs(bounds.size.z - zone.height * mapper.scale) < 0.002f, binding.simId + ": fondo incorrecto.");
            }
            if (binding.simId.StartsWith("Rack"))
            {
                Require(layout.TryGetStoragePose(binding.simId, 0, out var lower), "Falta repisa inferior.");
                Require(layout.TryGetStoragePose(binding.simId, 1, out var upper), "Falta repisa superior.");
                Require(upper.position.y - lower.position.y > data.meta.PalletDiameter * mapper.scale,
                    "La caja no cabe entre los niveles del rack.");
            }
            var station = data.stations.Find(s => s.name == binding.simId);
            if (station != null)
                Require(Vector3.Distance(binding.model.GetComponent<ZoneAnchor>().servicePoint,
                    mapper.SimToUnity(new Vector2(station.pos[0], station.pos[1]))) < 0.002f, "Punto de atención incorrecto.");
        }
    }

    static void CheckPlayback(SimPlayer player, SimRoot data, CoordinateMapper mapper)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var type = typeof(SimPlayer);
        var instance = typeof(CoordinateMapper).GetProperty("Instance");
        var previous = CoordinateMapper.Instance;
        var agvs = (Dictionary<string, Transform>)type.GetField("agvVisuals", flags).GetValue(player);
        var pallets = (Dictionary<string, Transform>)type.GetField("palletVisuals", flags).GetValue(player);
        try
        {
            instance.GetSetMethod(true).Invoke(null, new object[] { mapper });
            type.GetField("data", flags).SetValue(player, data);
            var apply = type.GetMethod("ApplyFrame", flags);
            for (int i = 0; i < data.frames.Count; i++)
            {
                var a = data.frames[i];
                var b = data.frames[Mathf.Min(i + 1, data.frames.Count - 1)];
                apply.Invoke(player, new object[] { a, b, 0.5f });
                var events = player.GetComponent<SimulationObstacles>();
                Require(events != null && events.VisiblePeople == (a.pedestrians == null ? 0 : a.pedestrians.Count),
                    "Los peatones visibles no coinciden con el JSON.");
                Require(events.VisibleAlarms == a.stations.Count(station => station.state == "OUT_OF_SERVICE"),
                    "Las alarmas visibles no coinciden con el JSON.");
                foreach (var pallet in a.pallets)
                {
                    if (pallet.removed)
                    {
                        Require(!pallets.ContainsKey(pallet.id) || !pallets[pallet.id].gameObject.activeSelf,
                            "Una caja despachada sigue visible.");
                        continue;
                    }
                    if (pallet.state == "SIENDO_TRANSPORTADO") continue;
                    Require(player.layout.TryGetStoragePose(pallet.storage_zone, pallet.storage_level, out var pose),
                        "Falta superficie para " + pallet.storage_zone);
                    var bounds = SimulationGeometry.BoundsInFrame(pallets[pallet.id], Quaternion.identity);
                    Require(Mathf.Abs(bounds.min.y - pose.position.y) < 0.002f,
                        "La caja no descansa sobre la superficie de " + pallet.storage_zone);
                    Require(Vector2.Distance(new Vector2(bounds.center.x, bounds.center.z),
                        new Vector2(pose.position.x, pose.position.z)) < 0.002f, "La caja está desplazada de su estación.");
                    Require(pose.position.y > mapper.floorHeight + 0.02f, "La caja sigue en el suelo.");
                }
                foreach (var mission in a.missions)
                {
                    var pallet = a.pallets.Find(p => p.id == mission.pallet);
                    if (mission.status != "ASIGNADA" || pallet == null || pallet.state != "SIENDO_TRANSPORTADO") continue;
                    var vehicle = agvs[mission.assigned];
                    var rig = vehicle.GetComponentInChildren<AgvMechanics>();
                    Require(Vector3.Distance(pallets[pallet.id].position, rig.CargoPosition) < 0.001f,
                        "La carga no sigue al AGV asignado.");
                    var boxBounds = SimulationGeometry.BoundsInFrame(pallets[pallet.id], Quaternion.identity);
                    float envelope = Vector2.Distance(new Vector2(boxBounds.center.x, boxBounds.center.z),
                        new Vector2(vehicle.position.x, vehicle.position.z)) + new Vector2(boxBounds.extents.x, boxBounds.extents.z).magnitude;
                    Require(envelope <= data.meta.AgvClearance * mapper.scale, "La caja excede el espacio reservado por Python.");
                }
                foreach (var agv in a.agvs)
                {
                    var next = b.agvs.Single(v => v.id == agv.id);
                    var midpoint = new Vector2((agv.pos[0] + next.pos[0]) / 2f, (agv.pos[1] + next.pos[1]) / 2f);
                    Require(Vector3.Distance(agvs[agv.id].position, mapper.SimToUnity(midpoint)) < 0.002f,
                        $"{agv.id}: interpolación incorrecta en {a.t}.");
                }
            }
            // Volver al comienzo debe ocultar pallets de la ejecución anterior.
            apply.Invoke(player, new object[] { data.frames[0], data.frames[0], 0f });
            foreach (var pallet in pallets)
                Require(pallet.Value.gameObject.activeSelf == data.frames[0].pallets.Any(p => p.id == pallet.Key && !p.removed),
                    "Un pallet de la vuelta anterior permanece visible.");

            CheckCameras(player, data.meta, mapper);

            player.loop = false;
            type.GetField("frameIndex", flags).SetValue(player, data.frames.Count - 2);
            type.GetField("timeInFrame", flags).SetValue(player, player.secondsPerFrame * 1.01f);
            type.GetMethod("AdvanceFrame", flags).Invoke(player, null);
            Require((int)type.GetField("frameIndex", flags).GetValue(player) == data.frames.Count - 1,
                "La reproducción sin loop no alcanza el último frame.");
        }
        finally
        {
            foreach (var visual in agvs.Values.Concat(pallets.Values))
                if (visual != null) UnityEngine.Object.DestroyImmediate(visual.gameObject);
            agvs.Clear();
            pallets.Clear();
            instance.GetSetMethod(true).Invoke(null, new object[] { previous });
        }
    }

    static void CheckCameras(SimPlayer player, SimMeta meta, CoordinateMapper mapper)
    {
        var cameraObject = new GameObject("CameraValidation");
        var camera = cameraObject.AddComponent<Camera>();
        camera.aspect = 16f / 9f;
        var controller = player.gameObject.AddComponent<SimulationCameraController>();
        var keyboard = InputSystem.AddDevice<Keyboard>();
        try
        {
            controller.Initialize(player, mapper, meta, camera);
            Require(controller.SelectedView == 5 && camera.orthographic, "La vista inicial no es cenital.");
            foreach (var entry in new[] { (Key.Digit1, 1), (Key.Digit2, 2), (Key.Digit3, 3), (Key.Digit4, 4), (Key.Digit5, 5), (Key.Numpad1, 1), (Key.Numpad2, 2), (Key.Numpad3, 3), (Key.Numpad4, 4), (Key.Numpad5, 5) })
            {
                InputSystem.Update();
                InputState.Change(keyboard, new KeyboardState(entry.Item1));
                keyboard.MakeCurrent();
                typeof(SimulationCameraController).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(controller, null);
                controller.Refresh(0f);
                Require(controller.SelectedView == entry.Item2, "La tecla " + entry.Item1 + " no selecciona la cámara correcta.");
                Require(camera.orthographic == (entry.Item2 == 5), "Proyección incorrecta.");
                if (entry.Item2 != 5)
                {
                    Require(player.TryGetAgvVisual("AGV-" + entry.Item2, out var agv), "Falta el objetivo de seguimiento.");
                    Vector3 focus = agv.position + Vector3.up * meta.AgvDiameter * mapper.scale * 0.4f;
                    Require(Vector3.Dot(camera.transform.forward, (focus - camera.transform.position).normalized) > 0.999f,
                        "La cámara no mira al AGV seleccionado.");
                }
                InputState.Change(keyboard, new KeyboardState());
                InputSystem.Update();
            }
            foreach (float aspect in new[] { 16f / 9f, 1f, 9f / 16f })
            {
                camera.aspect = aspect;
                controller.Refresh(0f);
                foreach (float x in new[] { 0f, (float)meta.width })
                foreach (float y in new[] { 0f, (float)meta.height })
                {
                    Vector3 point = camera.WorldToViewportPoint(mapper.SimToUnity(new Vector2(x, y)));
                    Require(point.x >= 0f && point.x <= 1f && point.y >= 0f && point.y <= 1f && point.z > 0f,
                        "La vista superior recorta el almacén.");
                }
            }
            Require(Vector3.Dot(camera.transform.up, mapper.Rotation * Vector3.forward) > 0.999f,
                "El eje Y de Python no apunta hacia arriba en pantalla.");
        }
        finally
        {
            InputSystem.RemoveDevice(keyboard);
            UnityEngine.Object.DestroyImmediate(controller);
            UnityEngine.Object.DestroyImmediate(cameraObject);
        }
    }

    static void CheckMechanics(float diameter)
    {
        var model = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/AGV.prefab"));
        var rig = model.GetComponent<AgvMechanics>();
        Require(rig != null, "El AGV no tiene articulaciones configuradas.");
        var root = SimulationGeometry.PreparePlaybackVisual(model.transform, diameter, rig.modelForwardYaw);
        try
        {
            rig.Initialize(root, diameter);
            Require(rig.WheelCount == 4, "Deben girar exactamente cuatro ruedas.");
            Require(Vector3.Dot(model.transform.right, root.forward) > 0.999f, "Las horquillas no miran al frente.");
            rig.Pose(Vector3.zero, Vector3.forward, false, 0f, true);
            var wheel = rig.wheels[0];
            Quaternion before = wheel.rotation;
            float forkHeight = rig.forks.position.y;
            Vector3 cargoBefore = rig.CargoPosition;
            rig.Pose(Vector3.forward * 0.2f, Vector3.forward, true, 0.15f, false);
            Require(Quaternion.Angle(before, wheel.rotation) > 1f, "La rueda no gira al avanzar.");
            Require(rig.forks.position.y > forkHeight, "Las horquillas no suben al cargar.");
            Require(rig.CargoPosition.y > cargoBefore.y, "La caja no sube con las horquillas.");
            Require(root.InverseTransformPoint(rig.CargoPosition).z > 0f, "La caja no está delante del AGV.");
            before = wheel.rotation;
            rig.Pose(root.position, Vector3.zero, true, 0.15f, false);
            Require(Quaternion.Angle(before, wheel.rotation) < 0.01f, "Las ruedas giran estando detenido.");
            rig.Pose(root.position, Vector3.zero, false, 0.3f, false);
            Require(Mathf.Abs(rig.Lift) < 0.0001f, "Las horquillas no bajan después de entregar.");
            before = wheel.rotation;
            rig.Pose(Vector3.one * 20f, Vector3.forward, false, 0f, true);
            Require(Quaternion.Angle(before, wheel.rotation) < 0.01f, "El reinicio se interpreta como distancia rodada.");
        }
        finally { UnityEngine.Object.DestroyImmediate(root.gameObject); }
    }

    static void CheckVisual(string path, float diameter)
    {
        var model = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path));
        var root = SimulationGeometry.PreparePlaybackVisual(model.transform, diameter);
        try
        {
            var bounds = SimulationGeometry.BoundsInFrame(root, Quaternion.identity);
            Require(new Vector2(bounds.center.x, bounds.center.z).magnitude < 0.001f, path + ": pivote desplazado.");
            Require(Mathf.Abs(bounds.min.y) < 0.001f, path + ": base fuera del suelo.");
            Require(Mathf.Abs(new Vector2(bounds.size.x, bounds.size.z).magnitude - diameter) < 0.001f, path + ": tamaño incorrecto.");
        }
        finally { UnityEngine.Object.DestroyImmediate(root.gameObject); }
    }

    static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
