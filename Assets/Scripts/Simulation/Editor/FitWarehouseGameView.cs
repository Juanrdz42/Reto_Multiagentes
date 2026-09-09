using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>Quita la ampliación del editor que recorta la imagen de Game.</summary>
[InitializeOnLoad]
public static class FitWarehouseGameView
{
    static FitWarehouseGameView()
    {
        EditorApplication.delayCall += () => {
            if (SessionState.GetBool("WarehouseGameViewFitted", false)) return;
            Fit();
            SessionState.SetBool("WarehouseGameViewFitted", true);
        };
    }

    [MenuItem("Simulation/Fit Game view")]
    public static void Fit()
    {
        var type = typeof(Editor).Assembly.GetType("UnityEditor.GameView");
        if (type == null) return;
        foreach (var window in Resources.FindObjectsOfTypeAll(type))
        {
            var field = type.GetField("m_ZoomArea", BindingFlags.Instance | BindingFlags.NonPublic);
            var zoom = field == null ? null : field.GetValue(window);
            if (zoom == null) continue;
            var method = zoom.GetType().GetMethod("SetTransform", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, new[] { typeof(Vector2), typeof(Vector2) }, null);
            if (method == null) continue;
            method.Invoke(zoom, new object[] { Vector2.zero, Vector2.one });
            ((EditorWindow)window).Repaint();
        }
    }
}
