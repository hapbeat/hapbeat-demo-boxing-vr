using UnityEngine;

namespace Hapbeat.Boxing
{
    // A wrist-aligned guard proxy, not an estimate of the real elbow.
    public static class BoxingForearm
    {
        public const float Length = .28f, Radius = .045f, WristOffset = .10f;
        public const int Samples = 13;
        public static Vector3 Center(int sample) => Vector3.back *
            (WristOffset + Radius + (Length - 2 * Radius) * sample / (Samples - 1));

        // Overlapping swept spheres approximate the visible capsule within 1 mm.
        // Uses the same relative-motion collision system as the gloves, including rotation.
        public static bool Sweep(Vector3 from, Vector3 to, float radius,
            Vector3 oldHand, Quaternion oldRotation, Vector3 hand, Quaternion rotation,
            out float time, out Vector3 targetFrom, out Vector3 targetTo)
        {
            time = float.PositiveInfinity; targetFrom = targetTo = default;
            for (int i = 0; i < Samples; i++)
            {
                Vector3 a = oldHand + oldRotation * Center(i), b = hand + rotation * Center(i);
                if (BoxingCollision.Sweep(from, to, radius, a, b, Radius, out float t) && t < time)
                { time = t; targetFrom = a; targetTo = b; }
            }
            return !float.IsPositiveInfinity(time);
        }
    }
}
