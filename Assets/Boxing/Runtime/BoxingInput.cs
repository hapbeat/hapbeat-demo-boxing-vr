using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR;
using UnityEngine.XR.Hands;
using Unity.XR.CoreUtils;

namespace Hapbeat.Boxing
{
    [DefaultExecutionOrder(-200)]
    public sealed class BoxingInput : MonoBehaviour
    {
        public XROrigin origin;
        public Camera headCamera;
        public TrackedPoseDriver headDriver;
        [Tooltip("Scene start marker: X/Z position and Y rotation define where the player starts and faces. Head height remains tracked.")]
        public Transform startPoint;
        public Vector3 StartPosition => startPoint != null ? new Vector3(startPoint.position.x, 0, startPoint.position.z) : Vector3.zero;
        public float StartYaw => startPoint != null ? startPoint.eulerAngles.y : 0;
        public float ReferenceEyeHeight { get; private set; } = 1.65f;
        public event System.Action Recentered;
        public BoxingInputMode mode = BoxingInputMode.Hands;
        public BoxingInputMode ActiveMode => mode == BoxingInputMode.Hands && controllerFallback ? BoxingInputMode.Controllers : mode;
        private bool controllerFallback;
        [Header("Debug locomotion (disable for exhibition)")]
        public bool debugStickMovement = true;
        [Min(0)] public float debugMoveSpeed = 0.45f;
        [Min(0)] public float debugMoveRadius = 0.6f;
        public string TrackingStatus { get; private set; } = "WAITING FOR TRACKING";
        public Vector3 controllerOffset = new Vector3(0, -0.015f, 0.08f);
        public Vector3 controllerRotation = new Vector3(75, 0, 0);
        public bool HasTracking { get; private set; }
        public bool MenuPressed { get; private set; }
        public bool ConfirmPressed { get; private set; }
        public float Navigate { get; private set; }
        public BoxerPose Current { get; private set; }
        public bool HasOverride { get; private set; }
        public BoxingHandPointer LeftPointer { get; } = new BoxingHandPointer();
        public BoxingHandPointer RightPointer { get; } = new BoxingHandPointer();
        private BoxerPose testPose;
        private XRHandSubsystem hands;
        private readonly List<XRHandSubsystem> handSystems = new List<XRHandSubsystem>();
        private readonly List<XRInputSubsystem> inputSystems = new List<XRInputSubsystem>();
        private bool oldMenu, oldConfirm, aligned;
        private readonly BoxingHandMenuGesture handMenu = new BoxingHandMenuGesture();
        private string lastTrackingReport;
        private readonly BoxingHandVisual leftVisual = new BoxingHandVisual(), rightVisual = new BoxingHandVisual();
        private BoxingXrControls xrControls;
        private BoxingGame game;

        private void OnEnable() { xrControls = new BoxingXrControls(); aligned = false; }
        private void OnDisable() { xrControls?.Dispose(); xrControls = null; HasTracking = false; LeftPointer.Reset(); RightPointer.Reset(); }

        public void SetTestPose(BoxerPose pose) { HasOverride = true; testPose = Current = pose; HasTracking = pose.valid; }
        public void ClearTestPose() => HasOverride = false;
        public void SelectMode(BoxingInputMode next)
        {
            mode = next; HasTracking = false; controllerFallback = false; handMenu.Reset();
        }
        public void Recenter()
        {
            if (headCamera == null || origin == null) return;
            origin.RotateAroundCameraUsingOriginUp(Mathf.DeltaAngle(headCamera.transform.eulerAngles.y, StartYaw));
            var p = headCamera.transform.position;
            origin.transform.position += StartPosition - new Vector3(p.x, 0, p.z);
            ReferenceEyeHeight = Mathf.Clamp(headCamera.transform.position.y,.8f,2.2f);
            leftVisual.Reset(); rightVisual.Reset();
            aligned = true;
            Recentered?.Invoke();
        }
        private void Update()
        {
            if (game == null) game = GetComponentInParent<BoxingGame>() ?? FindFirstObjectByType<BoxingGame>();
            MenuPressed = ConfirmPressed = false; Navigate = 0;
            if (headDriver != null) headDriver.enabled = !HasOverride;
            if (HasOverride) { Current = testPose; HasTracking = testPose.valid; return; }
            BoxerPose frame = new BoxerPose { timestamp = Time.realtimeSinceStartupAsDouble };
            var previousMode = ActiveMode;
            {
                var xr = xrControls.Read();
                bool headValid = xr.headTracked;
                FindHands();
                ReadPointer(true, headValid && mode == BoxingInputMode.Hands, LeftPointer);
                ReadPointer(false, headValid && mode == BoxingInputMode.Hands, RightPointer);
                // Head transform is driven by the template's Input System TrackedPoseDriver (Update + BeforeRender).
                if (headValid && !aligned)
                {
                    SubsystemManager.GetSubsystems(inputSystems);
                    foreach (var system in inputSystems) system.TrySetTrackingOriginMode(TrackingOriginModeFlags.Floor);
                    Recenter();
                }
                frame.head = headCamera.transform.position; frame.headRotation = headCamera.transform.rotation;
                controllerFallback = false;
                if (mode == BoxingInputMode.Hands)
                {
                    FindHands();
                    var wide=BoxingWideMotionFeature.Active;
                    wide?.Prepare();
                    bool l = ReadHand(true, out frame.left, out frame.leftRotation, out frame.leftClosed);
                    bool r = ReadHand(false, out frame.right, out frame.rightRotation, out frame.rightClosed);
                    frame.valid = headValid && l && r;
                    bool lv = leftVisual.Sample(l,ref frame.left,ref frame.leftRotation,frame.timestamp);
                    bool rv = rightVisual.Sample(r,ref frame.right,ref frame.rightRotation,frame.timestamp);
                    bool lw=ReadWideVisual(wide,true,l,ref frame.left,ref frame.leftRotation);
                    bool rw=ReadWideVisual(wide,false,r,ref frame.right,ref frame.rightRotation);
                    if(lw) { lv=true; leftVisual.Reset(); }
                    if(rw) { rv=true; rightVisual.Reset(); }
                    frame.visualValid = headValid && lv && rv;
                    TrackingStatus = !headValid ? "HMD NOT TRACKED" : hands == null || !hands.running ? "WAITING FOR HAND SUBSYSTEM" :
                        !l || !r ? $"WRIST TRACKING: LEFT {(l ? "OK" : "LOST")} / RIGHT {(r ? "OK" : "LOST")}" : "HANDS READY";
                    if(headValid && (lw || rw)) TrackingStatus="WMM ESTIMATED - COMBAT PAUSED";
                    bool controllerIntent = xr.confirm || xr.menu || Mathf.Abs(xr.navigate) > 0.6f;
                    controllerFallback = !frame.valid && headValid && xr.leftTracked && xr.rightTracked &&
                        (previousMode == BoxingInputMode.Controllers || controllerIntent);
                    if (!controllerFallback) MenuPressed = ReadHandMenu(headValid, frame.head);
                }
                if (ActiveMode == BoxingInputMode.Controllers)
                {
                    bool l = xr.leftTracked, r = xr.rightTracked;
                    var lp = xr.left; var rp = xr.right;
                    var space = headCamera.transform.parent;
                    frame.left = space.TransformPoint(lp.position + lp.rotation * controllerOffset);
                    frame.right = space.TransformPoint(rp.position + rp.rotation * controllerOffset);
                    frame.leftRotation = space.rotation * lp.rotation * Quaternion.Euler(controllerRotation);
                    frame.rightRotation = space.rotation * rp.rotation * Quaternion.Euler(controllerRotation);
                    frame.valid = headValid && l && r; frame.leftClosed = frame.rightClosed = true;
                    TrackingStatus = !headValid ? "HMD NOT TRACKED" : !l || !r ? "CONTROLLERS NOT TRACKED" : "CONTROLLERS READY";
                    if (frame.valid && game != null && !game.menu.IsOpen && !game.Paused)
                    {
                        Vector3 shift = ApplyDebugMove(xr.move, Time.unscaledDeltaTime, game.Opponent.Root);
                        if (shift.sqrMagnitude > 0)
                        {
                            frame.head += shift; frame.left += shift; frame.right += shift;
                            frame.leftClosed = frame.rightClosed = false; game.ResetPunches();
                        }
                    }
                }
                MenuPressed |= xr.menu && !oldMenu; ConfirmPressed = xr.confirm && !oldConfirm;
                oldMenu = xr.menu; oldConfirm = xr.confirm; Navigate = xr.navigate;
            }
            if (Keyboard.current != null)
            {
                MenuPressed |= Keyboard.current.escapeKey.wasPressedThisFrame;
                ConfirmPressed |= Keyboard.current.enterKey.wasPressedThisFrame;
                if (Keyboard.current.upArrowKey.wasPressedThisFrame) Navigate = 1;
                if (Keyboard.current.downArrowKey.wasPressedThisFrame) Navigate = -1;
            }
            if (previousMode != ActiveMode && game != null) { game.ResetHistory(); game.feedback.StopImpacts(); handMenu.Reset(); }
            Current = frame; HasTracking = frame.valid;
            string report = $"source={ActiveMode} valid={frame.valid} status={TrackingStatus}";
            if (report != lastTrackingReport)
            {
                Debug.Log("[Boxing Input] " + report); lastTrackingReport = report;
            }
        }
        public Vector3 ApplyDebugMove(Vector2 stick, float dt, Vector3 opponentPosition)
        {
            if (!debugStickMovement || ActiveMode != BoxingInputMode.Controllers || origin == null || headCamera == null ||
                dt <= 0 || dt > 0.1f || stick.magnitude < 0.2f) return Vector3.zero;
            Vector3 head = Vector3.ProjectOnPlane(headCamera.transform.position, Vector3.up);
            Vector3 delta = Quaternion.Euler(0, StartYaw, 0) * new Vector3(stick.x, 0, stick.y);
            Vector3 wanted = head + Vector3.ClampMagnitude(delta, 1) * (debugMoveSpeed * dt);
            wanted = StartPosition + Vector3.ClampMagnitude(wanted - StartPosition, debugMoveRadius);
            Vector3 enemy = Vector3.ProjectOnPlane(opponentPosition, Vector3.up);
            if (Vector3.Distance(wanted, enemy) < 0.5f) return Vector3.zero;
            delta = wanted - head; origin.transform.position += delta;
            return delta;
        }
        private void FindHands()
        {
            if (hands != null && hands.running) return;
            SubsystemManager.GetSubsystems(handSystems);
            hands = handSystems.Find(s => s.running);
        }
        private bool ReadHandMenu(bool headValid, Vector3 head)
        {
            if (!headValid || hands == null || !hands.running || !hands.leftHand.isTracked ||
                !hands.leftHand.GetJoint(XRHandJointID.Palm).TryGetPose(out var palm) ||
                !hands.leftHand.GetJoint(XRHandJointID.Wrist).TryGetPose(out var wrist) ||
                !hands.leftHand.GetJoint(XRHandJointID.MiddleProximal).TryGetPose(out var knuckle) ||
                !hands.leftHand.GetJoint(XRHandJointID.MiddleTip).TryGetPose(out var middle) ||
                !hands.leftHand.GetJoint(XRHandJointID.IndexTip).TryGetPose(out var index))
            { handMenu.Reset(); return false; }
            float palmLength = Vector3.Distance(knuckle.position, wrist.position);
            bool open = Vector3.Distance(middle.position, palm.position) > palmLength * 1.15f &&
                Vector3.Distance(index.position, palm.position) > palmLength;
            var space = headCamera.transform.parent;
            return handMenu.Update(true, open, space.TransformPoint(palm.position), space.rotation * palm.rotation * Vector3.down, head, Time.unscaledDeltaTime);
        }
        private bool ReadHand(bool left, out Vector3 position, out Quaternion rotation, out bool closed)
        {
            position = Vector3.zero; rotation = Quaternion.identity; closed = false;
            if (hands == null || !hands.running) return false;
            XRHand hand = left ? hands.leftHand : hands.rightHand;
            if (!hand.isTracked || !hand.GetJoint(XRHandJointID.Wrist).TryGetPose(out var wrist)) return false;
            var space = headCamera.transform.parent;
            // XR Hands converts OpenXR to Unity: +Z points towards the fingers.
            // A rigid glove must not depend on articulated/occluded fingertip poses.
            position = space.TransformPoint(wrist.position + wrist.rotation * new Vector3(0, 0, .09f));
            rotation = space.rotation * wrist.rotation;
            closed = true;
            return true;
        }
        private void ReadPointer(bool left, bool enabled, BoxingHandPointer pointer)
        {
            if (!enabled || hands == null || !hands.running) { pointer.Reset(); return; }
            XRHand hand = left ? hands.leftHand : hands.rightHand;
            if (!hand.isTracked || !hand.GetJoint(XRHandJointID.IndexTip).TryGetPose(out var tip) ||
                !hand.GetJoint(XRHandJointID.IndexProximal).TryGetPose(out var knuckle) ||
                !hand.GetJoint(XRHandJointID.ThumbTip).TryGetPose(out var thumb)) { pointer.Reset(); return; }
            var space = headCamera.transform.parent;
            pointer.Sample(true, space.TransformPoint(tip.position), space.TransformPoint(knuckle.position), space.TransformPoint(thumb.position));
        }
        private bool ReadWideVisual(BoxingWideMotionFeature wide,bool left,bool measured,ref Vector3 position,ref Quaternion rotation)
        {
            if(measured || wide==null || !wide.TryGetVisual(left,out var wrist)) return false;
            var space=headCamera.transform.parent;
            position=space.TransformPoint(wrist.position+wrist.rotation*new Vector3(0,0,.09f));
            rotation=space.rotation*wrist.rotation;
            return true;
        }
    }
}
