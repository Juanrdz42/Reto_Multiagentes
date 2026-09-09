using UnityEngine;

/// <summary>Una sola transformación para geometría, guías y posiciones del JSON.
/// Python (x,y) corresponde al suelo XZ de Unity. Las estaciones son puntos
/// de atención y no se usan para estimar la escala de los modelos.</summary>
[DisallowMultipleComponent]
public class CoordinateMapper : MonoBehaviour
{
    static CoordinateMapper instance;
    public static CoordinateMapper Instance
    {
        get { if (instance == null) instance = FindFirstObjectByType<CoordinateMapper>(); return instance; }
        private set => instance = value;
    }

    [Min(0.0001f), Tooltip("Unidades Unity por unidad Python; igual para ambos ejes.")]
    public float scale = 0.19f;
    [Tooltip("Esquina (0,0) de Python en Unity. Se usa X/Z; la altura se define abajo.")]
    public Vector3 origin = new Vector3(-37.75f, 0f, -38.5f);
    public float rotationY;
    public float floorHeight = -0.8351f;

    public bool Ready => scale > 0f;
    public Quaternion Rotation => Quaternion.Euler(0f, rotationY, 0f);

    void Awake() => Instance = this;
    void OnDestroy() { if (Instance == this) Instance = null; }

    public Vector3 SimToUnity(Vector2 simXY)
    {
        return new Vector3(origin.x, floorHeight, origin.z)
            + Rotation * new Vector3(simXY.x * scale, 0f, simXY.y * scale);
    }
}
