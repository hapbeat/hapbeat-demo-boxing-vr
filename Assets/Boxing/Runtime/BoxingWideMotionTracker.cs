using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Hapbeat.Boxing
{
    // Separate WMM tracker: never changes XR Hands' ordinary tracker or combat samples.
    // ABI: Khronos XR_EXT_hand_tracking and Meta's meta_hand_tracking_wide_motion_mode.h.
    public sealed class BoxingWideMotionTracker : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)] public struct CreateInfo { public int type; public IntPtr next; public int hand, jointSet; }
        [StructLayout(LayoutKind.Sequential)] public struct WideInfo { public int type; public IntPtr next; public int mode; }
        [StructLayout(LayoutKind.Sequential)] public struct LocateInfo { public int type; public IntPtr next; public ulong space; public long time; }
        [StructLayout(LayoutKind.Sequential)] public struct Locations { public int type; public IntPtr next; public uint active, count; public IntPtr joints; }
        [StructLayout(LayoutKind.Sequential)] public struct Joint { public ulong flags; public Quaternion rotation; public Vector3 position; public float radius; }
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate int Create(ulong session, ref CreateInfo info, out ulong tracker);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate int Locate(ulong tracker, ref LocateInfo info, ref Locations locations);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate int Destroy(ulong tracker);
        private readonly Create create;
        private readonly Locate locate;
        private readonly Destroy destroy;
        private ulong left, right;
        private IntPtr buffer;
        public int LastResult { get; private set; }
        public bool Ready => left != 0 && right != 0;

        public BoxingWideMotionTracker(Create create, Locate locate, Destroy destroy)
        { this.create=create; this.locate=locate; this.destroy=destroy; }

        public bool Start(ulong session)
        {
            Dispose();
            IntPtr wide=Marshal.AllocHGlobal(Marshal.SizeOf<WideInfo>());
            try
            {
                Marshal.StructureToPtr(new WideInfo { type=1000539000, mode=1 },wide,false);
                var info=new CreateInfo { type=1000051001, next=wide, hand=1, jointSet=1 };
                LastResult=create(session,ref info,out left);
                if(LastResult!=0 || left==0) { Dispose(); return false; }
                info.hand=2; LastResult=create(session,ref info,out right);
                if(LastResult!=0 || right==0) { Dispose(); return false; }
                buffer=Marshal.AllocHGlobal(26*Marshal.SizeOf<Joint>());
                return true;
            }
            finally { Marshal.FreeHGlobal(wide); }
        }

        public bool TryGet(bool isLeft, ulong space, long time, out Pose wrist)
        {
            wrist=default;
            if(!Ready || space==0 || time<=0) return false;
            var info=new LocateInfo {type=1000051002,space=space,time=time};
            var locations=new Locations {type=1000051003,count=26,joints=buffer};
            LastResult=locate(isLeft?left:right,ref info,ref locations);
            if(LastResult!=0 || locations.active==0 || locations.count!=26) return false;
            // XR_HAND_JOINT_WRIST_EXT = 1. VALID flags, not TRACKED, allow WMM estimates.
            var joint=Marshal.PtrToStructure<Joint>(IntPtr.Add(buffer,Marshal.SizeOf<Joint>()));
            if((joint.flags&3)!=3 || !Finite(joint.position.x) || !Finite(joint.position.y) || !Finite(joint.position.z) ||
                !Finite(joint.rotation.x) || !Finite(joint.rotation.y) || !Finite(joint.rotation.z) || !Finite(joint.rotation.w) ||
                Quaternion.Dot(joint.rotation,joint.rotation)<.5f) return false;
            // OpenXR right-handed -> Unity left-handed, same convention as XR Hands.
            wrist=new Pose(new Vector3(joint.position.x,joint.position.y,-joint.position.z),
                new Quaternion(-joint.rotation.x,-joint.rotation.y,joint.rotation.z,joint.rotation.w).normalized);
            return true;
        }
        private static bool Finite(float value)=>!float.IsNaN(value)&&!float.IsInfinity(value);
        public void Dispose()
        {
            if(left!=0) { destroy(left); left=0; }
            if(right!=0) { destroy(right); right=0; }
            if(buffer!=IntPtr.Zero) { Marshal.FreeHGlobal(buffer); buffer=IntPtr.Zero; }
        }
    }
}
