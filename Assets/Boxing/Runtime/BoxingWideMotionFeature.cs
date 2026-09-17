using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using AOT;
using UnityEngine;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;

namespace Hapbeat.Boxing
{
#if UNITY_EDITOR
    [UnityEditor.XR.OpenXR.Features.OpenXRFeature(UiName="Boxing Wide Motion (visual only)",
        BuildTargetGroups=new[]{UnityEditor.BuildTargetGroup.Android}, Company="Hapbeat", Version="1.0.0",
        Desc="Optional Quest WMM glove continuity; ordinary XR Hands still owns combat.",
        OpenxrExtensionStrings=Extension+" XR_EXT_hand_tracking", FeatureId="com.hapbeat.boxing.wide-motion")]
#endif
    public sealed class BoxingWideMotionFeature : OpenXRFeature
    {
        public const string Extension="XR_META_hand_tracking_wide_motion_mode";
        public const string BodyPermission="com.oculus.permission.BODY_TRACKING";
        public static BoxingWideMotionFeature Active { get; private set; }
        public string Status { get; private set; }="WMM UNAVAILABLE";
        [StructLayout(LayoutKind.Sequential)] public struct FrameState
        { public int type; public IntPtr next; public long predictedTime, period; public uint shouldRender; }
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetProc(ulong instance, IntPtr name, out IntPtr function);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int WaitFrame(ulong session, IntPtr info, IntPtr state);
        private static GetProc originalGetProc;
        private static WaitFrame originalWaitFrame;
        private static readonly GetProc getProcHook=GetProcHook;
        private static readonly WaitFrame waitFrameHook=WaitFrameHook;
        private static long predictedTime, sampledAt;
        private BoxingWideMotionTracker tracker;
        private ulong session;
        private bool running, attempted, permissionRequested;
#if UNITY_ANDROID && !UNITY_EDITOR
        private UnityEngine.Android.PermissionCallbacks permissionCallbacks;
#endif
        protected override IntPtr HookGetInstanceProcAddr(IntPtr func)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            originalGetProc=Marshal.GetDelegateForFunctionPointer<GetProc>(func);
            return Marshal.GetFunctionPointerForDelegate(getProcHook);
#else
            return func;
#endif
        }
        [MonoPInvokeCallback(typeof(GetProc))] private static int GetProcHook(ulong instance, IntPtr name, out IntPtr function)
        {
            int result=originalGetProc(instance,name,out function);
            if(result==0 && function!=IntPtr.Zero && Marshal.PtrToStringAnsi(name)=="xrWaitFrame")
            {
                originalWaitFrame=Marshal.GetDelegateForFunctionPointer<WaitFrame>(function);
                function=Marshal.GetFunctionPointerForDelegate(waitFrameHook);
            }
            return result;
        }
        [MonoPInvokeCallback(typeof(WaitFrame))] private static int WaitFrameHook(ulong xrSession, IntPtr info, IntPtr state)
        {
            int result=originalWaitFrame(xrSession,info,state);
            if(result==0)
            {
                var frame=Marshal.PtrToStructure<FrameState>(state);
                Interlocked.Exchange(ref predictedTime,frame.predictedTime);
                Interlocked.Exchange(ref sampledAt,Stopwatch.GetTimestamp());
            }
            return result;
        }
        protected override bool OnInstanceCreate(ulong instance)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if(!OpenXRRuntime.IsExtensionEnabled(Extension)) return false;
            var get=Marshal.GetDelegateForFunctionPointer<GetProc>(xrGetInstanceProcAddr);
            var create=Resolve<BoxingWideMotionTracker.Create>(get,instance,"xrCreateHandTrackerEXT");
            var locate=Resolve<BoxingWideMotionTracker.Locate>(get,instance,"xrLocateHandJointsEXT");
            var destroy=Resolve<BoxingWideMotionTracker.Destroy>(get,instance,"xrDestroyHandTrackerEXT");
            if(create==null || locate==null || destroy==null) return false;
            tracker=new BoxingWideMotionTracker(create,locate,destroy); Active=this;
            Status="WMM WAITING FOR SESSION"; return true;
#else
            return false;
#endif
        }
        private static T Resolve<T>(GetProc get,ulong instance,string name) where T:Delegate
        {
            IntPtr text=Marshal.StringToHGlobalAnsi(name);
            try { return get(instance,text,out var fn)==0 && fn!=IntPtr.Zero ? Marshal.GetDelegateForFunctionPointer<T>(fn) : null; }
            finally { Marshal.FreeHGlobal(text); }
        }
        protected override void OnSessionCreate(ulong value) { session=value; attempted=permissionRequested=false; }
        protected override void OnSessionBegin(ulong value) { running=true; }
        protected override void OnSessionEnd(ulong value) { running=false; tracker?.Dispose(); attempted=false; ClearFrame(); }
        protected override void OnSessionDestroy(ulong value) { running=false; tracker?.Dispose(); session=0; ClearFrame(); }
        protected override void OnInstanceDestroy(ulong value) { tracker?.Dispose(); tracker=null; Active=null; ClearFrame(); }
        private static void ClearFrame() { Interlocked.Exchange(ref predictedTime,0); Interlocked.Exchange(ref sampledAt,0); }

        // Main thread only. Permission denial leaves ordinary tracking untouched.
        public void Prepare()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if(!running || tracker==null) return;
            if(!UnityEngine.Android.Permission.HasUserAuthorizedPermission(BodyPermission))
            {
                Status="WMM BODY PERMISSION NOT GRANTED";
                if(!permissionRequested)
                {
                    permissionRequested=true;
                    permissionCallbacks=new UnityEngine.Android.PermissionCallbacks();
                    permissionCallbacks.PermissionGranted+=_=>{ attempted=false; };
                    UnityEngine.Android.Permission.RequestUserPermission(BodyPermission,permissionCallbacks);
                }
                return;
            }
            if(!attempted)
            {
                attempted=true;
                Status=tracker.Start(session)?"WMM READY (VISUAL ONLY)":"WMM UNAVAILABLE: "+tracker.LastResult;
                UnityEngine.Debug.Log("[Boxing WMM] "+Status);
            }
#endif
        }
        public bool TryGetVisual(bool left,out Pose wrist)
        {
            wrist=default;
#if UNITY_ANDROID && !UNITY_EDITOR
            if(!running || tracker==null || !UnityEngine.Android.Permission.HasUserAuthorizedPermission(BodyPermission)) return false;
            long stamp=Interlocked.Read(ref sampledAt), time=Interlocked.Read(ref predictedTime);
            if(stamp==0 || (Stopwatch.GetTimestamp()-stamp)/(double)Stopwatch.Frequency>.12) return false;
            return tracker.TryGet(left,GetCurrentAppSpace(),time,out wrist);
#else
            return false;
#endif
        }
    }
}
