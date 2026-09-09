using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Networking;

public static class PullIntegrationValidation
{
    static UnityWebRequest request;
    static UnityWebRequestAsyncOperation operation;
    public static void Run()
    {
        var args = Environment.GetCommandLineArgs();
        int index = Array.IndexOf(args, "--export-url");
        if (index < 0) throw new ArgumentException("--export-url requerido");
        request = UnityWebRequest.Get(args[index + 1]);
        request.timeout = 60;
        operation = request.SendWebRequest();
        EditorApplication.update += Check;
    }
    static void Check()
    {
        if (!operation.isDone) return;
        EditorApplication.update -= Check;
        try
        {
            if (request.result != UnityWebRequest.Result.Success) throw new Exception(request.error);
            EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
            var player = UnityEngine.Object.FindFirstObjectByType<SimPlayer>();
            player.LoadExport(request.downloadHandler.text);
            if (player.Results == null || player.Results.metrics.Count != 13) throw new Exception("Faltan métricas de Python");
            var json = JsonUtility.FromJson<SimRoot>(request.downloadHandler.text);
            foreach (int step in new[] { 0, 138, json.meta.total_steps, 60, 0, 138 })
            {
                player.Seek(step);
                if (player.CurrentStep != step) throw new Exception("Seek incorrecto");
                var frame = json.frames.Single(f => f.t == step);
                foreach (var agv in frame.agvs)
                {
                    if (!player.TryGetAgvVisual(agv.id, out var visual)) throw new Exception("Falta AGV");
                    var expected = CoordinateMapper.Instance.SimToUnity(new Vector2(agv.pos[0], agv.pos[1]));
                    if (Vector3.Distance(visual.position, expected) > 0.002f) throw new Exception("Posición distinta a Python");
                }
                int actual = player.gameObject.scene.GetRootGameObjects().Count(r => r.activeSelf && r.name.StartsWith("Pallet_P"));
                if (actual != frame.pallets.Count(p => !p.removed)) throw new Exception("Cajas incorrectas al retroceder");
            }
            player.LoadExport(request.downloadHandler.text);
            if (player.gameObject.scene.GetRootGameObjects().Count(r => r.activeSelf && r.name.StartsWith("AGV_AGV-")) != 4)
                throw new Exception("Recargar duplica AGVs");
            player.paused = true;
            typeof(SimPlayer).GetMethod("Update", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(player, null);
            if (player.CurrentStep != 0) throw new Exception("La pausa avanzó la simulación");
            Debug.Log("PULL INTEGRATION PASSED: HTTP JSON, 13 metrics, rewind, pause, reload, exact Python positions.");
            EditorApplication.Exit(0);
        }
        catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
        finally { request.Dispose(); }
    }
}
