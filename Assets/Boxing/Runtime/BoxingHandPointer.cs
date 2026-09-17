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
        public void Sample(bool valid, Vector3 tip, Vector3 knuckle, Vector3 thumb)
        {
            Pressed = false;
            if (!valid || (tip - knuckle).sqrMagnitude < .0001f) { Reset(); return; }
            float gap = Vector3.Distance(tip, thumb);
            Valid = true;
            if (gap > .045f) { Pinching = false; armed = true; }
            // Keep the last pointing direction while the index bends into a pinch.
            if (!Pinching && gap > .032f) Ray = new Ray(tip, (tip - knuckle).normalized);
            if (gap < .025f && !Pinching)
            {
                Pinching = true; Pressed = armed; armed = false;
            }
            Valid = Ray.direction.sqrMagnitude > .5f;
        }
    }
}
