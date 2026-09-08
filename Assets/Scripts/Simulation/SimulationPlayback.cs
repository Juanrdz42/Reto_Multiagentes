using UnityEngine;

/// <summary>Detecta segmentos que recortan el interior de una zona, incluyendo la huella visual.</summary>
public static class SimulationPlayback
{
    public static bool IntersectsZone(Vector2 from, Vector2 to, SimZone zone, float clearance)
    {
        float enter = 0f, exit = 1f;
        Vector2 min = new Vector2(zone.x - clearance, zone.y - clearance);
        Vector2 max = new Vector2(zone.x + zone.width + clearance, zone.y + zone.height + clearance);
        Vector2 delta = to - from;
        for (int axis = 0; axis < 2; axis++)
        {
            if (Mathf.Abs(delta[axis]) < 0.00001f)
            {
                if (from[axis] <= min[axis] || from[axis] >= max[axis]) return false;
                continue;
            }
            float a = (min[axis] - from[axis]) / delta[axis];
            float b = (max[axis] - from[axis]) / delta[axis];
            enter = Mathf.Max(enter, Mathf.Min(a, b));
            exit = Mathf.Min(exit, Mathf.Max(a, b));
            if (enter >= exit) return false;
        }
        return enter < exit;
    }
}
