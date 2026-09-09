using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Iluminación de la presentación. Conserva todos los materiales y texturas de la maqueta real.</summary>
public class SimulationAppearance : MonoBehaviour
{

    public void Initialize()
    {
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.82f, 0.87f, 0.92f);
        RenderSettings.ambientEquatorColor = new Color(0.6f, 0.65f, 0.7f);
        RenderSettings.ambientGroundColor = new Color(0.34f, 0.39f, 0.43f);
        RenderSettings.reflectionIntensity = 0.5f;
        foreach (var light in FindObjectsByType<Light>())
            if (light.type == LightType.Directional)
            {
                light.transform.rotation = Quaternion.Euler(55f, -35f, 0f);
                light.color = new Color(1f, 0.97f, 0.92f);
                light.intensity = 1.15f;
                light.shadows = LightShadows.Soft;
                light.shadowStrength = 0.55f;
            }
        var fill = new GameObject("WarehouseFillLight").AddComponent<Light>();
        fill.transform.SetParent(transform, false);
        fill.type = LightType.Directional;
        fill.transform.rotation = Quaternion.Euler(45f, 145f, 0f);
        fill.color = new Color(0.8f, 0.9f, 1f);
        fill.intensity = 0.3f;
        fill.shadows = LightShadows.None;
        QualitySettings.shadowDistance = 150f;
        if (Camera.main != null)
        {
            Camera.main.clearFlags = CameraClearFlags.SolidColor;
            Camera.main.backgroundColor = new Color32(205, 216, 223, 255);
        }
    }

}
