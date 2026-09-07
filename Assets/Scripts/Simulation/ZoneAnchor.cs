using UnityEngine;

/// <summary>
/// Marca un objeto ya colocado en la escena (un Rack, un Dock/Inbound, una
/// Estacion de Carga) como el punto físico que corresponde a una estación o
/// cargador de la simulación. El CoordinateMapper usa estas parejas
/// (posición en la simulación 2D <-> posición real en Unity) para calcular
/// automáticamente cómo convertir cualquier posición de la simulación a
/// coordenadas de mundo, sin necesidad de escribir escala/rotación a mano.
/// </summary>
[DisallowMultipleComponent]
public class ZoneAnchor : MonoBehaviour
{
    [Tooltip("Debe coincidir EXACTO con un nombre de simulation_export.json: " +
             "Rack A, Rack B, Rack C, Rack D, Rack E, Dock 1, Dock 2, Dock 3, Dock 4, " +
             "Charger 1, Charger 2")]
    public string simId;

    [Tooltip("Opcional: renderer que se tiñe según el estado de la estación " +
             "(disponible/reservada/ocupada). Déjalo vacío si no lo quieres.")]
    public Renderer stateRenderer;
}
