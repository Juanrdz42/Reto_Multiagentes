using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Calcula y aplica la transformación (rotación + escala uniforme + traslación)
/// que convierte una posición 2D de la simulación (x, y) en una posición de
/// mundo de Unity (x, altura fija, z).
///
/// La transformación se ajusta automáticamente por mínimos cuadrados a partir
/// de los ZoneAnchor presentes en la escena: cada ancla conoce su nombre de
/// estación (simId) y su posición real en Unity; este componente busca esa
/// misma estación en simulation_export.json y resuelve la mejor rotación +
/// escala + traslación que hace coincidir ambos conjuntos de puntos.
///
/// Se necesitan al menos 2 anclas; con 4 o más (bien repartidas por el mapa,
/// no todas en línea) el ajuste es más preciso.
/// </summary>
[DisallowMultipleComponent]
public class CoordinateMapper : MonoBehaviour
{
    public static CoordinateMapper Instance { get; private set; }

    [Tooltip("Altura fija (eje Y de Unity) a la que se colocan AGVs y pallets")]
    public float floorHeight = 0f;

    // Transformación como número complejo: destino = a * origen + b
    float aRe = 1f, aIm = 0f, bRe = 0f, bIm = 0f;
    public bool Ready { get; private set; }

    void Awake()
    {
        Instance = this;
    }

    public void Fit(List<ZoneAnchor> anchors, Dictionary<string, Vector2> simPositionsById)
    {
        var srcs = new List<Vector2>();
        var dsts = new List<Vector2>();

        foreach (var anchor in anchors)
        {
            if (anchor == null || string.IsNullOrEmpty(anchor.simId)) continue;
            if (!simPositionsById.TryGetValue(anchor.simId, out var simPos))
            {
                Debug.LogWarning($"[CoordinateMapper] '{anchor.simId}' (en {anchor.name}) no existe en simulation_export.json. Revisa el nombre.");
                continue;
            }

            var p = anchor.transform.position;
            srcs.Add(simPos);
            dsts.Add(new Vector2(p.x, p.z));
        }

        if (srcs.Count < 2)
        {
            Debug.LogError($"[CoordinateMapper] Solo hay {srcs.Count} ZoneAnchor válidos en la escena. " +
                            "Se necesitan al menos 2 (recomendado 4+) para calcular la transformación. Usando identidad (posiciones probablemente incorrectas).");
            aRe = 1f; aIm = 0f; bRe = 0f; bIm = 0f;
            Ready = true;
            return;
        }

        Vector2 muSrc = Average(srcs);
        Vector2 muDst = Average(dsts);

        float numRe = 0f, numIm = 0f, den = 0f;
        for (int i = 0; i < srcs.Count; i++)
        {
            float sx = srcs[i].x - muSrc.x, sy = srcs[i].y - muSrc.y;
            float dx = dsts[i].x - muDst.x, dy = dsts[i].y - muDst.y;

            // conj(s) * d, para resolver por minimos cuadrados el escalar
            // complejo "a" (rotacion+escala) que minimiza sum |d - a*s|^2
            numRe += sx * dx + sy * dy;
            numIm += sx * dy - sy * dx;
            den += sx * sx + sy * sy;
        }

        if (den < 1e-6f)
        {
            Debug.LogWarning("[CoordinateMapper] Los puntos de referencia están casi en el mismo lugar; no se puede calcular escala/rotación. Usando escala 1.");
            aRe = 1f; aIm = 0f;
        }
        else
        {
            aRe = numRe / den;
            aIm = numIm / den;
        }

        bRe = muDst.x - (aRe * muSrc.x - aIm * muSrc.y);
        bIm = muDst.y - (aRe * muSrc.y + aIm * muSrc.x);

        Ready = true;

        float scale = Mathf.Sqrt(aRe * aRe + aIm * aIm);
        float angleDeg = Mathf.Atan2(aIm, aRe) * Mathf.Rad2Deg;
        Debug.Log($"[CoordinateMapper] Ajustado con {srcs.Count} puntos de referencia. Escala={scale:F4}, Rotación={angleDeg:F1}°");
    }

    public Vector3 SimToUnity(Vector2 simXY)
    {
        float x = aRe * simXY.x - aIm * simXY.y + bRe;
        float z = aRe * simXY.y + aIm * simXY.x + bIm;
        return new Vector3(x, floorHeight, z);
    }

    static Vector2 Average(List<Vector2> pts)
    {
        Vector2 sum = Vector2.zero;
        foreach (var p in pts) sum += p;
        return sum / pts.Count;
    }
}
