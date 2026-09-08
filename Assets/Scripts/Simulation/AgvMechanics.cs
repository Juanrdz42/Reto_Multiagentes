using System.Collections.Generic;
using UnityEngine;

/// <summary>Articulaciones del prefab; la trayectoria sigue siendo la del JSON.</summary>
public class AgvMechanics : MonoBehaviour
{
    public Transform forks;
    public Transform[] wheels;
    public float modelForwardYaw = -90f; // El frente del modelo original es +X.

    readonly List<Transform> axles = new List<Transform>();
    readonly List<float> radii = new List<float>();
    readonly List<float> sides = new List<float>();
    Transform playbackRoot;
    Vector3 forksRest;
    Vector3 cargoRest;
    float lift, liftHeight;
    bool positioned;
    public int WheelCount => axles.Count;
    public float Lift => lift;
    public Vector3 CargoPosition => playbackRoot.TransformPoint(cargoRest + Vector3.up * lift);
    public Quaternion CargoRotation => playbackRoot.rotation;

    public void Initialize(Transform root, float diameter)
    {
        playbackRoot = root;
        liftHeight = diameter * 0.16f;
        if (forks != null)
        {
            forksRest = forks.localPosition;
            Bounds bounds = SimulationGeometry.BoundsInFrame(forks, Quaternion.identity);
            cargoRest = root.InverseTransformPoint(bounds.center);
            // La parte delantera de la malla es la superficie horizontal de las horquillas.
            float surface = float.NegativeInfinity;
            foreach (var mesh in forks.GetComponentsInChildren<MeshFilter>())
                foreach (var vertex in mesh.sharedMesh.vertices)
                {
                    Vector3 point = root.InverseTransformPoint(mesh.transform.TransformPoint(vertex));
                    if (point.z >= cargoRest.z) surface = Mathf.Max(surface, point.y);
                }
            cargoRest.y = float.IsNegativeInfinity(surface) ? bounds.max.y : surface;
            cargoRest.z += bounds.size.z * 0.1f;
        }
        else cargoRest = new Vector3(0f, diameter * 0.1f, diameter * 0.25f);

        if (wheels == null) return;
        foreach (var wheel in wheels)
        {
            if (wheel == null) continue;
            Bounds bounds = SimulationGeometry.BoundsInFrame(wheel, Quaternion.identity);
            // ProBuilder puede tener el pivote lejos del centro de la rueda.
            var axle = new GameObject(wheel.name + "_Axle").transform;
            axle.SetParent(wheel.parent, false);
            axle.position = bounds.center;
            wheel.SetParent(axle, true);
            axles.Add(axle);
            radii.Add(Mathf.Max(0.001f, bounds.size.y / 2f));
            sides.Add(root.InverseTransformPoint(bounds.center).x);
        }
    }

    public void Pose(Vector3 position, Vector3 direction, bool carrying, float seconds, bool reset)
    {
        Vector3 oldPosition = playbackRoot.position;
        float oldYaw = playbackRoot.eulerAngles.y;
        if (direction.sqrMagnitude > 0.00001f)
        {
            Quaternion heading = Quaternion.LookRotation(direction.normalized, Vector3.up);
            playbackRoot.rotation = !positioned || reset ? heading
                : Quaternion.RotateTowards(playbackRoot.rotation, heading, 720f * seconds);
        }
        playbackRoot.position = position;
        if (positioned && !reset)
        {
            float travel = Vector3.Dot(position - oldPosition, playbackRoot.forward);
            float yaw = Mathf.DeltaAngle(oldYaw, playbackRoot.eulerAngles.y) * Mathf.Deg2Rad;
            for (int i = 0; i < axles.Count; i++)
                axles[i].Rotate(playbackRoot.right, (travel - yaw * sides[i]) / radii[i] * Mathf.Rad2Deg, Space.World);
        }
        if (reset) lift = 0f;
        lift = Mathf.MoveTowards(lift, carrying ? liftHeight : 0f, seconds * liftHeight / 0.3f);
        if (forks != null)
            forks.localPosition = forksRest + forks.parent.InverseTransformVector(Vector3.up * lift);
        positioned = true;
    }
}
