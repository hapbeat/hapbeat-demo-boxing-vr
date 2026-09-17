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
        private BoxerPose testPose;
        private XRHandSubsystem hands;
        private readonly List<XRHandSubsystem> handSystems = new List<XRHandSubsystem>();
        private readonly List<XRInputSubsystem> inputSystems = new List<XRInputSubsystem>();
        private bool oldMenu, oldConfirm, aligned;
        private float leftPunch, rightPunch;
        private readonly BoxingHandMenuGesture handMenu = new BoxingHandMenuGesture();
        private float desktopYaw, desktopPitch;
        private BoxingXrControls xrControls;
        private BoxingGame game;

        private void OnEnable() { xrControls = new BoxingXrControls(); aligned = false; }
        private void OnDisable() { xrControls?.Dispose(); xrControls = null; HasTracking = false; }

        public void SetTestPose(BoxerPose pose) { HasOverride = true; testPose = Current = pose; HasTracking = pose.valid; }
        public void ClearTestPose() => HasOverride = false;
        public void SelectMode(BoxingInputMode next)
        {
            mode = next; HasTracking = false; controllerFallback = false; handMenu.Reset();
        }
        public void Recenter()
        {
            if (headCamera == null || origin == null) return;
            if (mode == BoxingInputMode.Desktop) { desktopYaw = desktopPitch = 0; return; }
            origin.RotateAroundCameraUsingOriginUp(Mathf.DeltaAngle(headCamera.transform.eulerAngles.y, StartYaw));
            var p = headCamera.transform.position;
            origin.transform.position += StartPosition - new Vector3(p.x, 0, p.z);
            aligned = true;
            Recentered?.Invoke();
        }
        private void Update()
        {
            if (game == null) game = GetComponentInParent<BoxingGame>() ?? FindFirstObjectByType<BoxingGame>();
            MenuPressed = ConfirmPressed = false; Navigate = 0;
            if (headDriver != null) headDriver.enabled = mode != BoxingInputMode.Desktop && !HasOverride;
            if (HasOverride) { Current = testPose; HasTracking = testPose.valid; return; }
            BoxerPose frame = new BoxerPose { timestamp = Time.realtimeSinceStartupAsDouble };
            var previousMode = ActiveMode;
            if (mode == BoxingInputMode.Desktop) ReadDesktop(ref frame);
            else
            {
                var xr = xrControls.Read();
                bool headValid = xr.headTracked;
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
                    bool l = ReadHand(true, out frame.left, out frame.leftRotation, out frame.leftClosed);
                    bool r = ReadHand(false, out frame.right, out frame.rightRotation, out frame.rightClosed);
                    frame.valid = headValid && l && r;
                    TrackingStatus = !headValid ? "HMD NOT TRACKED" : hands == null || !hands.running ? "HAND SUBSYSTEM UNAVAILABLE - CHECK LINK DEVELOPER FEATURES" :
                        !l || !r ? "SHOW BOTH HANDS" : "HANDS READY";
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
            if (!hand.isTracked || !hand.GetJoint(XRHandJointID.Palm).TryGetPose(out var palm) ||
                !hand.GetJoint(XRHandJointID.MiddleTip).TryGetPose(out var tip) ||
                !hand.GetJoint(XRHandJointID.MiddleProximal).TryGetPose(out var knuckle) ||
                !hand.GetJoint(XRHandJointID.Wrist).TryGetPose(out var wrist)) return false;
            var space = headCamera.transform.parent;
            position = space.TransformPoint(knuckle.position);
            Vector3 forward = knuckle.position - wrist.position;
            if (forward.sqrMagnitude <= 0.00001f) return false;
            rotation = space.rotation * Quaternion.LookRotation(forward.normalized, palm.rotation * Vector3.up);
            // Scale against this hand's measured palm length, not a fixed hand size.
            closed = Vector3.Distance(tip.position, palm.position) < Vector3.Distance(knuckle.position, wrist.position) * 0.95f;
            return true;
        }
        private void ReadDesktop(ref BoxerPose frame)
        {
            var k = Keyboard.current; var mouse = Mouse.current;
            if (mouse != null && mouse.rightButton.isPressed)
            {
                desktopYaw += mouse.delta.ReadValue().x * 0.08f;
                desktopPitch = Mathf.Clamp(desktopPitch - mouse.delta.ReadValue().y * 0.08f, -45, 45);
            }
            float lean = k == null ? 0 : (k.dKey.isPressed ? 0.35f : 0) - (k.aKey.isPressed ? 0.35f : 0);
            float height = k != null && k.sKey.isPressed ? 1.2f : 1.65f;
            var startRotation = Quaternion.Euler(0, StartYaw, 0);
            frame.head = StartPosition + startRotation * new Vector3(lean, height, 0); frame.headRotation = Quaternion.Euler(desktopPitch, StartYaw + desktopYaw, 0);
            headCamera.transform.SetPositionAndRotation(frame.head, frame.headRotation);
            if (k != null && k.qKey.wasPressedThisFrame) leftPunch = 1;
            if (k != null && k.eKey.wasPressedThisFrame) rightPunch = 1;
            leftPunch = Mathf.Max(0, leftPunch - Time.unscaledDeltaTime * 3.5f);
            rightPunch = Mathf.Max(0, rightPunch - Time.unscaledDeltaTime * 3.5f);
            float l = Mathf.Sin(leftPunch * Mathf.PI) * 0.62f, r = Mathf.Sin(rightPunch * Mathf.PI) * 0.62f;
            bool guard = k != null && k.spaceKey.isPressed;
            frame.left = frame.head + startRotation * new Vector3(guard ? -0.12f : -0.23f, guard ? -0.03f : -0.25f, 0.32f + l);
            frame.right = frame.head + startRotation * new Vector3(guard ? 0.12f : 0.23f, guard ? -0.03f : -0.25f, 0.32f + r);
            frame.leftRotation = frame.rightRotation = startRotation;
            frame.leftClosed = frame.rightClosed = frame.valid = true;
        }
    }
}
