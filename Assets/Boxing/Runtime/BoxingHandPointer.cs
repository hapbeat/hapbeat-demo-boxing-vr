using UnityEngine;

namespace Hapbeat.Boxing
{
    // Only measured joints may drive UI. A held pinch after tracking loss never clicks.
    public sealed class BoxingHandPointer
    {
        public Ray Ray { get; private set; }
        public bool Valid { get; private set; }
        public bool Pressed { get; private set; }
        public bool Pinching { get; private set; }
        private bool armed;
        public void Reset() { Valid = Pressed = Pinching = armed = false; Ray = default; }
        public void Sample(bool valid, Vector3 origin, Vector3 direction, bool pinched, bool released)
        {
            Pressed = false;
            if (!valid || direction.sqrMagnitude < .5f) { Reset(); return; }
            Valid = true;
            if (released) { Pinching = false; armed = true; }
            // Aim is supplied by the runtime, not by the bending index finger.
            Ray = new Ray(origin, direction.normalized);
            if (pinched && !Pinching)
            {
                Pinching = true; Pressed = armed; armed = false;
            }
        }
    }
}
