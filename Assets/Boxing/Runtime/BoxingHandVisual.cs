using UnityEngine;

namespace Hapbeat.Boxing
{
    // Render-only prediction. Never used as a valid combat sample.
    public sealed class BoxingHandVisual
    {
        private Vector3 last, velocity;
        private Quaternion lastRotation;
        private double lastTime;
        private bool initialized;
        public void Reset() => initialized=false;
        public bool Sample(bool tracked, ref Vector3 position, ref Quaternion rotation, double time)
        {
            double gap=time-lastTime;
            if(tracked)
            {
                velocity=initialized && gap>0 && gap<.1 ? Vector3.ClampMagnitude((position-last)/(float)gap,1.5f) : Vector3.zero;
                last=position; lastRotation=rotation; lastTime=time; initialized=true; return true;
            }
            if(!initialized || gap<0 || gap>.12) { initialized=false; return false; }
            position=last+Vector3.ClampMagnitude(velocity*Mathf.Min((float)gap,.06f),.05f);
            rotation=lastRotation; return true;
        }
    }
}
