using UnityEngine;

namespace Hapbeat.Boxing
{
    public sealed class BoxingHandMenuGesture
    {
        private float held;
        private bool latched;
        public void Reset() { held = 0; latched = false; }
        public bool Update(bool tracked, bool open, Vector3 palm, Vector3 palmNormal, Vector3 head, float dt)
        {
            Vector3 toHead = head - palm;
            bool pose = tracked && open && toHead.magnitude >= 0.15f && toHead.magnitude <= 0.7f &&
                palm.y >= head.y - 0.5f && Vector3.Dot(palmNormal.normalized, toHead.normalized) >= 0.7f;
            if (!pose || dt <= 0 || dt > 0.1f) { Reset(); return false; }
            held += dt;
            if (held < 0.8f || latched) return false;
            latched = true; return true;
        }
    }
}
