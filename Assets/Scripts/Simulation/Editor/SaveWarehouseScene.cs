using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Guarda la geometría del almacén como objetos y assets normales de la escena.</summary>
public static class SaveWarehouseScene
{
    const string Folder = "Assets/SimulationWarehouse";
    [MenuItem("Simulation/Save warehouse geometry in scene")]
    public static void Run()
    {
        if (EditorApplication.isPlaying) throw new System.InvalidOperationException("Detén Play antes de guardar el almacén.");
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        var roots = scene.GetRootGameObjects();
        var layout = roots.SelectMany(r => r.GetComponentsInChildren<SimulationLayout>()).Single();
        var mapper = roots.SelectMany(r => r.GetComponentsInChildren<CoordinateMapper>()).Single();
        var data = JsonUtility.FromJson<SimRoot>(File.ReadAllText("Assets/StreamingAssets/simulation_export.json"));
        var existing = layout.GetComponent<DockWallVisual>();
        if (existing != null && existing.savedInScene)
        {
            Debug.Log("WAREHOUSE ALREADY SAVED");
            return;
        }
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", "SimulationWarehouse");
        layout.Apply(data, mapper);
        var room = roots.Single(r => r.name == "Cube");
        var filter = room.GetComponent<MeshFilter>();
        var mesh = Object.Instantiate(filter.sharedMesh);
        mesh.name = "RoomWithDockOpenings";
        AssetDatabase.CreateAsset(mesh, Folder + "/RoomWithDockOpenings.asset");
        room.GetComponent<RoomDockOpening>().savedInScene = true;
        // ProBuilder conserva su propia topología; retirarlo evita que restaure la pared antigua.
        foreach (var component in room.GetComponents<Component>())
            if (component != null && component.GetType().FullName == "UnityEngine.ProBuilder.Shapes.ProBuilderShape")
                Object.DestroyImmediate(component);
        foreach (var component in room.GetComponents<Component>())
            if (component != null && component.GetType().FullName == "UnityEngine.ProBuilder.ProBuilderMesh")
                Object.DestroyImmediate(component);
        filter.sharedMesh = mesh;
        room.GetComponent<MeshCollider>().sharedMesh = mesh;
        foreach (var truck in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<TruckDockVisual>()))
            truck.savedInScene = true;
        layout.GetComponent<DockWallVisual>().savedInScene = true;
        int number = 0;
        foreach (var renderer in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Renderer>(true)))
            foreach (var material in renderer.sharedMaterials)
                if (material != null && string.IsNullOrEmpty(AssetDatabase.GetAssetPath(material)))
                    AssetDatabase.CreateAsset(material, Folder + "/" + material.name.Replace("/", "_") + "_" + number++ + ".mat");
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new System.IO.IOException("No se pudo guardar la escena.");
        Debug.Log("WAREHOUSE SAVED IN EDIT MODE");
    }
}
