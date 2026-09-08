using UnityEngine;

/// <summary>Identifica una estación para el color de estado. SimulationLayout
/// coloca el modelo por su zona y calcula el punto de atención por separado.</summary>
[DisallowMultipleComponent]
public class ZoneAnchor : MonoBehaviour
{
    public string simId;
    public Renderer stateRenderer;
    [System.NonSerialized] public Vector3 servicePoint;
}
