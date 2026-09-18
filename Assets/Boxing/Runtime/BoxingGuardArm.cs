using UnityEngine;

namespace Hapbeat.Boxing
{
    public struct BoxingGuardArm
    {
        public Vector3 start,end;
        public float radius;
        public BoxingGuardArm(Vector3 start,Vector3 end,float radius)
        { this.start=start; this.end=end; this.radius=radius; }
        public float Distance(Vector3 point)
        {
            Vector3 d=end-start;
            float t=d.sqrMagnitude>1e-8f ? Mathf.Clamp01(Vector3.Dot(point-start,d)/d.sqrMagnitude) : 0;
            return Vector3.Distance(point,start+d*t);
        }
        public static bool Sweep(Vector3 from,Vector3 to,float fistRadius,BoxingGuardArm oldArm,BoxingGuardArm arm,
            out float time,out Vector3 targetFrom,out Vector3 targetTo)
        {
            time=float.PositiveInfinity; targetFrom=targetTo=default;
            float r=Mathf.Max(oldArm.radius,arm.radius);
            if(r<=0) return false;
            float length=Mathf.Max(Vector3.Distance(oldArm.start,oldArm.end),Vector3.Distance(arm.start,arm.end));
            int count=Mathf.Clamp(Mathf.CeilToInt(length/(r*.4f)),1,64);
            for(int i=0;i<=count;i++)
            {
                float u=i/(float)count;
                Vector3 a=Vector3.Lerp(oldArm.start,oldArm.end,u),b=Vector3.Lerp(arm.start,arm.end,u);
                if(BoxingCollision.Sweep(from,to,fistRadius,a,b,r,out float t) && t<time)
                {time=t; targetFrom=a; targetTo=b;}
            }
            return !float.IsPositiveInfinity(time);
        }
    }
}
