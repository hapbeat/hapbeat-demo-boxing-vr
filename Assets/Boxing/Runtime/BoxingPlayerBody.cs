using UnityEngine;

namespace Hapbeat.Boxing
{
    // The player's torso proxy: a vertical capsule from the chest to the belly, set back from the HMD along the
    // head's heading. Its front never passes the vertical plane through the eyes (Back > Radius), so forearms held
    // against the belly or the flanks stay in front of / beside it and take a body strike first.
    public static class BoxingPlayerBody
    {
        // Capsule axis ends below the HMD, the set-back of the axis behind the eyes and the radius, in metres.
        // The front lies Back - Radius = 1 cm behind the eye plane.
        public const float Top = .25f, Bottom = .60f, Back = .14f, Radius = .13f;
        public const int Samples = 13;

        // Horizontal heading of the face: pitch and roll do not move the torso. A gaze within ~0.6 deg of vertical takes
        // the heading from the head's up axis (ahead of the face when looking down, behind it when looking up). A rotation
        // that is not a valid quaternion (head never tracked) uses the arena forward +Z, the direction of the opponent.
        public static Vector3 Facing(Quaternion headRotation)
        {
            if (!(Quaternion.Dot(headRotation, headRotation) > .5f)) return Vector3.forward;
            Vector3 forward = headRotation * Vector3.forward;
            Vector3 heading = new Vector3(forward.x, 0, forward.z);
            if (heading.sqrMagnitude < 1e-4f) { heading = headRotation * Vector3.up * -Mathf.Sign(forward.y); heading.y = 0; }
            return heading.sqrMagnitude > 1e-8f ? heading.normalized : Vector3.forward;
        }
        public static Vector3 Center(Vector3 head, Quaternion headRotation, int sample) =>
            head - Facing(headRotation) * Back + Vector3.down * Mathf.Lerp(Top, Bottom, sample / (float)(Samples - 1));
        // Body strikes aim at the axis of the torso at mid height, as head strikes aim at the centre of the head: the fist
        // meets the front (Radius before the end of the strike) while still fast. Arms held against the body stop them,
        // open arms do not.
        public static Vector3 Aim(Vector3 head, Quaternion headRotation) =>
            head - Facing(headRotation) * Back + Vector3.down * ((Top + Bottom) * .5f);

        // Overlapping swept spheres approximate the capsule within 1 mm, as for BoxingForearm.
        public static bool Sweep(Vector3 from, Vector3 to, float radius,
            Vector3 oldHead, Quaternion oldRotation, Vector3 head, Quaternion rotation,
            out float time, out Vector3 targetFrom, out Vector3 targetTo)
        {
            time = float.PositiveInfinity; targetFrom = targetTo = default;
            for (int i = 0; i < Samples; i++)
            {
                Vector3 a = Center(oldHead, oldRotation, i), b = Center(head, rotation, i);
                if (BoxingCollision.Sweep(from, to, radius, a, b, Radius, out float t) && t < time)
                { time = t; targetFrom = a; targetTo = b; }
            }
            return !float.IsPositiveInfinity(time);
        }
    }
}
