using System.Collections.Generic;
using Hapbeat.DemoSwitch;
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
        // Head height above the game floor at the last recenter (for the [Boxing Size] log only; nothing is sized from it).
        public float ReferenceEyeHeight { get; private set; } = 1.65f;
        // The head height above the game floor last seen (BoxingGame.Simulate), for the status line.
        public float HeadAboveFloor { get; set; }
        // The game's floor: the XR Origin's height when the scene started (the arena is authored on it). Fixed: the Quest's
        // floor drifts (boundary off, a new venue), so the eye height is forced above this floor instead (ApplyEyeHeight).
        public float GameFloor { get { CaptureFloor(); return gameFloor; } }
        private float gameFloor;
        private bool floorCaptured;
        private void CaptureFloor() { if (floorCaptured || origin == null) return; gameFloor = origin.transform.position.y; floorCaptured = true; }
        private void Awake() => CaptureFloor();
        // The forced eye height (Demo Switch settings Eye Height, 1.65 m by default): the opponent is sized for it.
        public static float EyeHeight => DemoEyeHeight.Target;
        // Moves the XR Origin vertically so the head is target above the game floor, whatever the headset's floor. Called by the
        // Demo Switch (IDemoSessionHeight: 視線をリセット, a system recenter, the first tracked frame) and Boxing's own
        // RECENTER; called again it changes nothing (no accumulation). The round / tutorial start recenter does not call it.
        public void ApplyEyeHeight(float target)
        {
            if (headCamera == null || origin == null) return;
            origin.transform.position += Vector3.up * DemoEyeHeight.Correction(headCamera.transform.position.y - GameFloor, target);
        }
        public float ReferenceFloorHeight { get; private set; }
        public bool HeadTracked { get; private set; }
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
        private string lastTrackingReport;
        private readonly BoxingHandVisual leftVisual = new BoxingHandVisual(), rightVisual = new BoxingHandVisual();
        private BoxingXrControls xrControls;
        private BoxingGame game;

        private void OnEnable() { xrControls = new BoxingXrControls(); aligned = false; }
        private void OnDisable() { xrControls?.Dispose(); xrControls = null; HasTracking = HeadTracked = false; LeftPointer.Reset(); RightPointer.Reset(); }

        // Startup recenter (視線リセット once): after launch the visitor stands still; once the HMD has been tracked and
        // nearly still (head below StartupSpeed and yaw below StartupYawRate for StartupStillSeconds), or after
        // StartupTimeout seconds of tracking anyway, BoxingGame recenters once (yaw and horizontal position).
        // The former recenter on the first tracked frame came too early (the headset still settling on the head).
        public bool StartupRecentered { get; private set; }
        public const float StartupSpeed = .15f, StartupYawRate = 20, StartupStillSeconds = .5f, StartupTimeout = 5;
        private float startupTracked, startupStill, startupYaw;
        private Vector3 startupHead;
        private bool startupHave;
        // Each simulated frame until the startup recenter is due; true once, on the frame it is due.
        public bool TickStartup(bool tracked, Vector3 head, Quaternion rotation, float dt)
        {
            if (StartupRecentered || dt <= 0) return false;
            if (!tracked || !(Quaternion.Dot(rotation, rotation) > .5f)) { startupHave = false; startupStill = 0; return false; }
            float yaw = rotation.eulerAngles.y;
            if (startupHave)
            {
                bool still = Vector3.Distance(head, startupHead) / dt < StartupSpeed && Mathf.Abs(Mathf.DeltaAngle(startupYaw, yaw)) / dt < StartupYawRate;
                startupStill = still ? startupStill + dt : 0;
            }
            startupHave = true; startupHead = head; startupYaw = yaw; startupTracked += dt;
            if (startupStill < StartupStillSeconds - 1e-4f && startupTracked < StartupTimeout) return false;
            StartupRecentered = true; return true;
        }
        // A tutorial or round started before the startup recenter recenters itself; no startup recenter after it.
        public void CompleteStartup() => StartupRecentered = true;
        public void SetTestPose(BoxerPose pose) { HasOverride = true; testPose = Current = pose; HasTracking = pose.valid; HeadTracked=pose.valid; }
        public void ClearTestPose() => HasOverride = false;
        public void SelectMode(BoxingInputMode next)
        {
            mode = next; HasTracking = false; controllerFallback = false;
        }
        public void Recenter()
        {
            if (headCamera == null || origin == null) return;
            if(!HasOverride)
            {
                origin.RotateAroundCameraUsingOriginUp(Mathf.DeltaAngle(headCamera.transform.eulerAngles.y, StartYaw));
                var p = headCamera.transform.position;
                origin.transform.position += StartPosition - new Vector3(p.x, 0, p.z);
            }
            // Yaw and horizontal position only: the height is the forced eye height (ApplyEyeHeight), the floor the game's.
            ReferenceFloorHeight=GameFloor;
            float eye=(HasOverride ? Current.head.y : headCamera.transform.position.y)-ReferenceFloorHeight;
            ReferenceEyeHeight=Mathf.Max(.2f,eye);
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
                HeadTracked=headValid;
                FindHands();
                ReadPointer(true, headValid && mode == BoxingInputMode.Hands, LeftPointer);
                ReadPointer(false, headValid && mode == BoxingInputMode.Hands, RightPointer);
                // Head transform is driven by the template's Input System TrackedPoseDriver (Update + BeforeRender).
                if (headValid && !aligned)
                {
                    SubsystemManager.GetSubsystems(inputSystems);
                    foreach (var system in inputSystems) system.TrySetTrackingOriginMode(TrackingOriginModeFlags.Floor);
                    // The startup recenter waits until the visitor stands still (TickStartup, BoxingGame).
                    aligned = true;
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
                    frame.valid = headValid && l && r; frame.headTracked = headValid; frame.leftTracked = l; frame.rightTracked = r;
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
                    frame.headTracked = headValid; frame.leftTracked = l; frame.rightTracked = r;
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
            if (previousMode != ActiveMode && game != null) { game.ResetHistory(); game.feedback.StopImpacts(); }
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
                !hand.GetJoint(XRHandJointID.Wrist).TryGetPose(out var wrist) ||
                !hand.GetJoint(XRHandJointID.ThumbTip).TryGetPose(out var thumb)) { pointer.Reset(); return; }
            var space = headCamera.transform.parent;
            var aim = left ? MetaAimHand.left : MetaAimHand.right;
            float gap = Vector3.Distance(tip.position,thumb.position);
            Quaternion rotation=wrist.rotation;
            bool pinched=gap<.025f, released=gap>.045f;
            if(aim!=null && aim.added)
            {
                var flags=(MetaAimFlags)aim.aimFlags.ReadValue();
                bool aimValid=aim.isTracked.isPressed && (flags&MetaAimFlags.Valid)!=0;
                // Quest can expose the Meta Aim device before it has produced a valid aim
                // pose. The joint poses are still measured, so keep the menu usable with its
                // fingertip ray until Meta Aim becomes valid.
                if((flags&MetaAimFlags.SystemGesture)!=0) { pointer.Reset(); return; }
                if(aimValid)
                {
                    rotation=aim.deviceRotation.ReadValue();
                    pinched=aim.indexPressed.isPressed; released=aim.pinchStrengthIndex.ReadValue()<.5f;
                }
            }
            // XRI PinchPointFollow uses the midpoint; Meta Aim supplies a stable UI direction.
            pointer.Sample(true, space.TransformPoint((tip.position+thumb.position)*.5f),
                space.rotation*rotation*Vector3.forward, pinched, released);
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
