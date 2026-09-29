using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Unity.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Hands.ProviderImplementation;
using UnityEngine.XR.OpenXR.Features.Interactions;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;

namespace Hapbeat.Boxing.Tests
{
    // Exercises the real XR Hands subsystem and real BoxingInput joint reader.
    public sealed class BoxingTestHandProvider : XRHandSubsystemProvider
    {
        public static bool leftTracked, rightTracked, palmMenu, wristOnly;
        public static bool pointMenu, pinch;
        public static Handedness pointerSide = Handedness.Right;
        public static Vector3 menuTarget;
        public override void Start() { }
        public override void Stop() { }
        public override void Destroy() { }
        public override void GetHandLayout(NativeArray<bool> joints) { for (int i = 0; i < joints.Length; i++) joints[i] = true; }
        public override XRHandSubsystem.UpdateSuccessFlags TryUpdateHands(XRHandSubsystem.UpdateType updateType,
            ref Pose leftRoot, NativeArray<XRHandJoint> left, ref Pose rightRoot, NativeArray<XRHandJoint> right)
        {
            Fill(left, Handedness.Left, leftTracked, palmMenu); Fill(right, Handedness.Right, rightTracked, false);
            leftRoot = new Pose(new Vector3(-0.2f, 1.4f, 0.3f), Quaternion.identity);
            rightRoot = new Pose(new Vector3(0.2f, 1.4f, 0.3f), Quaternion.identity);
            return (leftTracked ? XRHandSubsystem.UpdateSuccessFlags.LeftHandRootPose | XRHandSubsystem.UpdateSuccessFlags.LeftHandJoints : 0) |
                (rightTracked ? XRHandSubsystem.UpdateSuccessFlags.RightHandRootPose | XRHandSubsystem.UpdateSuccessFlags.RightHandJoints : 0);
        }
        private static void Fill(NativeArray<XRHandJoint> joints, Handedness side, bool tracked, bool open)
        {
            Vector3 palm = new Vector3(side == Handedness.Left ? -0.2f : 0.2f, 1.4f, 0.3f);
            Quaternion rotation = open ? Quaternion.FromToRotation(Vector3.down, (Vector3.up * 1.65f - palm).normalized) : Quaternion.identity;
            for (int i = 0; i < joints.Length; i++)
            {
                var id = XRHandJointIDUtility.FromIndex(i); Vector3 p = palm;
                if (id == XRHandJointID.Wrist) p += Vector3.back * 0.04f;
                if (id == XRHandJointID.MiddleProximal) p += Vector3.forward * 0.06f;
                if (id == XRHandJointID.MiddleTip || id == XRHandJointID.IndexTip) p += Vector3.forward * (open ? 0.16f : 0.02f);
                if (pointMenu && side == pointerSide)
                {
                    Vector3 direction = (menuTarget - palm).normalized;
                    if (id == XRHandJointID.IndexProximal) p = palm;
                    if (id == XRHandJointID.IndexTip) p = palm + direction * .10f;
                    if (id == XRHandJointID.ThumbTip) p = palm + direction * .10f + Vector3.right * (pinch ? .015f : .07f);
                    rotation=Quaternion.LookRotation(menuTarget-(palm+direction*.1f+Vector3.right*(pinch ? .0075f : .035f)));
                }
                bool valid = tracked && (!wristOnly || id == XRHandJointID.Wrist);
                joints[i] = XRHandProviderUtility.CreateJoint(side, valid ? XRHandJointTrackingState.Pose : XRHandJointTrackingState.None, id, new Pose(p, rotation));
            }
        }
    }

    public sealed class BoxingHandInputTests : InputTestFixture
    {
        private XRHandSubsystem hands;
        private XRSimulatedHMD head;
        private MetaAimHand testAim;
        public override void Setup()
        {
            base.Setup();
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            BoxingTestHandProvider.leftTracked = BoxingTestHandProvider.rightTracked = BoxingTestHandProvider.palmMenu = false;
            BoxingTestHandProvider.wristOnly = false;
            BoxingTestHandProvider.pointMenu = BoxingTestHandProvider.pinch = false;
            BoxingTestHandProvider.pointerSide = Handedness.Right;
            InputSystem.RegisterLayout<XRSimulatedHMD>(); head = InputSystem.AddDevice<XRSimulatedHMD>();
        }
        public override void TearDown() { if(MetaAimHand.right==testAim) MetaAimHand.right=null; testAim=null; hands?.Destroy(); hands = null; base.TearDown(); }
        private void StartHands()
        {
            const string id = "Boxing.TestHands";
            var descriptors = new List<XRHandSubsystemDescriptor>(); SubsystemManager.GetSubsystemDescriptors(descriptors);
            if (!descriptors.Exists(d => d.id == id)) XRHandSubsystemDescriptor.Register(new XRHandSubsystemDescriptor.Cinfo { id = id, providerType = typeof(BoxingTestHandProvider) });
            SubsystemManager.GetSubsystemDescriptors(descriptors); hands = descriptors.Find(d => d.id == id).Create(); hands.Start();
        }
        private IEnumerator Pump(float seconds, BoxingGame game, bool aimAtStart = false)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end)
            {
                Quaternion rotation = aimAtStart ? Quaternion.LookRotation(game.menu.rows[0].transform.position - game.input.headCamera.transform.position) : Quaternion.identity;
                InputSystem.QueueStateEvent(head, new XRSimulatedHMDState { isTracked = true, trackingState = 3, centerEyePosition = Vector3.up * 1.65f, centerEyeRotation = rotation, deviceRotation = rotation });
                hands?.TryUpdateHands(XRHandSubsystem.UpdateType.Dynamic);
                yield return null;
            }
        }
        [UnityTest] public IEnumerator HandsCanArriveAfterStartupStartByPinchAndRecoverWithoutControllers()
        {
            yield return SceneManager.LoadSceneAsync("Boxing"); var game = Object.FindAnyObjectByType<BoxingGame>();
            game.SendMessage("FocusChanged", true); game.SendMessage("OnApplicationPause", false);
            Assert.That(game.input.mode, Is.EqualTo(BoxingInputMode.Hands));
            yield return Pump(0.4f, game); Assert.That(game.input.HasTracking, Is.False);
            StartHands(); BoxingTestHandProvider.leftTracked = BoxingTestHandProvider.rightTracked = true;
            yield return Pump(0.4f, game);
            Assert.That(game.input.HasTracking, Is.True, game.input.TrackingStatus);
            Assert.That(game.input.Current.leftClosed, Is.True); Assert.That(game.input.HasOverride, Is.False);
            yield return Pump(2.1f, game, true);
            Assert.That(game.menu.IsOpen, Is.True, "Looking alone must never select.");
            var ghosts=Object.FindObjectsByType<BoxingMenuHand>(FindObjectsSortMode.None);
            Assert.That(ghosts.Length,Is.EqualTo(2));
            foreach(var ghost in ghosts) Assert.That(ghost.renderers.Length>0 && ghost.renderers.All(r=>r.enabled),Is.True,"Tracked menu hands must be visible.");
            BoxingTestHandProvider.menuTarget = game.input.headCamera.transform.parent.InverseTransformPoint(game.menu.rows[0].transform.position);
            BoxingTestHandProvider.pointMenu = true;
            yield return Pump(.2f, game);
            BoxingTestHandProvider.pinch = true;
            yield return Pump(.2f, game);
            Assert.That(game.menu.IsOpen, Is.False, "Measured fingertip ray and pinch must start without controllers.");
            foreach(var ghost in ghosts) Assert.That(ghost.renderers.Any(r=>r.enabled),Is.False,"Ghost hands are menu-only.");
            BoxingTestHandProvider.pointMenu = false;
            yield return Pump(0.4f, game);
            Assert.That(game.Round.Countdown, Is.LessThan(3));
            BoxingTestHandProvider.rightTracked = false;
            yield return Pump(0.04f, game);
            Assert.That(game.input.HasTracking, Is.False, "Visual prediction must not restore combat tracking.");
            Assert.That(game.input.Current.visualValid, Is.True, "Brief loss keeps a render-only hand pose.");
            yield return Pump(0.2f, game); Assert.That(game.Paused, Is.True);
            Assert.That(game.input.Current.visualValid, Is.False, "Prediction expires instead of inventing a hidden swing.");
            BoxingTestHandProvider.rightTracked = true;
            yield return Pump(0.5f, game); Assert.That(game.Paused, Is.False, game.PauseReason);
            BoxingTestHandProvider.rightTracked = false; BoxingTestHandProvider.palmMenu = true;
            yield return Pump(1.0f, game);
            Assert.That(game.menu.IsOpen, Is.True, "Left palm opens the menu even if the other hand is occluded.");
            Assert.That(game.feedback.Sends, Is.Zero);
        }
        [UnityTest] public IEnumerator LeftPointerWorksAloneAndHeldPinchCannotRepeatOrClickAfterLoss()
        {
            yield return SceneManager.LoadSceneAsync("Boxing"); var game=Object.FindAnyObjectByType<BoxingGame>();
            game.feedback.forceSilent=true; game.feedback.sdkRoot.SetActive(false);
            StartHands(); BoxingTestHandProvider.leftTracked=true;
            yield return Pump(.4f,game);
            BoxingTestHandProvider.pointerSide=Handedness.Left;
            BoxingTestHandProvider.menuTarget=game.input.headCamera.transform.parent.InverseTransformPoint(game.menu.rows[3].transform.position);
            BoxingTestHandProvider.pointMenu=true;
            var original=game.tuning.impactMode;
            yield return Pump(.2f,game);
            BoxingTestHandProvider.pinch=true; yield return Pump(.2f,game);
            Assert.That(game.tuning.impactMode,Is.Not.EqualTo(original));
            var selected=game.tuning.impactMode;
            yield return Pump(.4f,game); Assert.That(game.tuning.impactMode,Is.EqualTo(selected));
            BoxingTestHandProvider.leftTracked=false; yield return Pump(.2f,game);
            BoxingTestHandProvider.leftTracked=true; yield return Pump(.2f,game);
            Assert.That(game.tuning.impactMode,Is.EqualTo(selected));
            BoxingTestHandProvider.pinch=false; yield return Pump(.2f,game);
            BoxingTestHandProvider.pinch=true; yield return Pump(.2f,game);
            Assert.That(game.tuning.impactMode,Is.EqualTo(original)); Assert.That(game.feedback.Sends,Is.Zero);
        }
        [UnityTest] public IEnumerator MetaAimDirectionAndPinchMidpointDriveMenuNotIndexDirection()
        {
            yield return SceneManager.LoadSceneAsync("Boxing"); var game=Object.FindAnyObjectByType<BoxingGame>();
            game.feedback.forceSilent=true; game.feedback.sdkRoot.SetActive(false);
            StartHands(); BoxingTestHandProvider.rightTracked=true;
            BoxingTestHandProvider.pointMenu=true; BoxingTestHandProvider.menuTarget=new Vector3(1,1.4f,.3f);
            InputSystem.RegisterLayout<MetaAimHand>(); testAim=InputSystem.AddDevice<MetaAimHand>(); MetaAimHand.right=testAim;
            Set(testAim.isTracked,1); Set(testAim.trackingState,3); Set(testAim.aimFlags,(int)(MetaAimFlags.Computed|MetaAimFlags.Valid));
            Set(testAim.deviceRotation,Quaternion.identity); Set(testAim.pinchStrengthIndex,0);
            yield return Pump(.4f,game);
            Assert.That(game.input.RightPointer.Valid,Is.True);
            Assert.That(Vector3.Dot(game.input.RightPointer.Ray.direction,game.input.headCamera.transform.parent.forward),Is.GreaterThan(.999f));
            var hand=hands.rightHand; hand.GetJoint(XRHandJointID.IndexTip).TryGetPose(out var tip); hand.GetJoint(XRHandJointID.ThumbTip).TryGetPose(out var thumb);
            Assert.That(Vector3.Distance(game.input.RightPointer.Ray.origin,game.input.headCamera.transform.parent.TransformPoint((tip.position+thumb.position)*.5f)),Is.LessThan(.001f));
            Set(testAim.aimFlags,(int)(MetaAimFlags.Computed|MetaAimFlags.Valid|MetaAimFlags.SystemGesture));
            yield return Pump(.2f,game); Assert.That(game.input.RightPointer.Valid,Is.False);
        }
        [UnityTest] public IEnumerator UnavailableMetaAimMustFallBackToMeasuredFingertipRayAndPinch()
        {
            yield return SceneManager.LoadSceneAsync("Boxing"); var game=Object.FindAnyObjectByType<BoxingGame>();
            game.feedback.forceSilent=true; game.feedback.sdkRoot.SetActive(false);
            StartHands(); BoxingTestHandProvider.rightTracked=true;
            BoxingTestHandProvider.pointMenu=true;
            BoxingTestHandProvider.menuTarget=game.input.headCamera.transform.parent.InverseTransformPoint(game.menu.rows[0].transform.position);
            InputSystem.RegisterLayout<MetaAimHand>(); testAim=InputSystem.AddDevice<MetaAimHand>(); MetaAimHand.right=testAim;
            Set(testAim.isTracked,1); Set(testAim.trackingState,3); Set(testAim.aimFlags,(int)MetaAimFlags.Computed);
            Set(testAim.deviceRotation,Quaternion.identity); Set(testAim.pinchStrengthIndex,0);
            yield return Pump(.4f,game);
            Assert.That(game.input.RightPointer.Valid,Is.True,
                "A present but unavailable Meta Aim source must not hide the measured fingertip ray.");
            BoxingTestHandProvider.pinch=true;
            yield return Pump(.2f,game);
            Assert.That(game.menu.IsOpen,Is.False,
                "The measured fingertip pinch must still select when Meta Aim is unavailable.");
            Assert.That(game.feedback.Sends,Is.Zero);
        }
        [UnityTest] public IEnumerator PickingUpControllersAfterHandlessStartupDoesNotRequireRestart()
        {
            yield return SceneManager.LoadSceneAsync("Boxing"); var game = Object.FindAnyObjectByType<BoxingGame>();
            game.SendMessage("FocusChanged", true); game.SendMessage("OnApplicationPause", false);
            yield return Pump(0.3f, game);
            Assert.That(game.input.HasTracking, Is.False);
            InputSystem.RegisterLayout<UnityEngine.XR.OpenXR.Input.HapticControl>();
            InputSystem.RegisterLayout<OculusTouchControllerProfile.OculusTouchController>();
            var left = InputSystem.AddDevice<OculusTouchControllerProfile.OculusTouchController>();
            var right = InputSystem.AddDevice<OculusTouchControllerProfile.OculusTouchController>();
            InputSystem.SetDeviceUsage(left, "LeftHand"); InputSystem.SetDeviceUsage(right, "RightHand");
            foreach (var device in new[] { left, right })
            {
                Set(device.isTracked, 1); Set(device.trackingState, 3);
                Set(device.devicePosition, new Vector3(device == left ? -0.2f : 0.2f, 1.3f, 0.3f));
                Set(device.deviceRotation, Quaternion.identity);
            }
            Set(right.primaryButton, 1);
            yield return Pump(0.4f, game);
            Assert.That(game.input.ActiveMode, Is.EqualTo(BoxingInputMode.Controllers));
            Assert.That(game.input.HasTracking, Is.True, game.input.TrackingStatus);
            Assert.That(game.menu.IsOpen, Is.False);
            Assert.That(game.Round.Countdown, Is.LessThan(3));
            Set(right.primaryButton, 0);
            StartHands(); BoxingTestHandProvider.leftTracked = BoxingTestHandProvider.rightTracked = true;
            Set(left.isTracked, 0); Set(right.isTracked, 0);
            yield return Pump(0.5f, game);
            Assert.That(game.input.mode, Is.EqualTo(BoxingInputMode.Hands));
            Assert.That(game.input.ActiveMode, Is.EqualTo(BoxingInputMode.Hands));
            Assert.That(game.input.HasTracking, Is.True); Assert.That(game.Paused, Is.False, game.PauseReason);
            Assert.That(game.feedback.Sends, Is.Zero);
        }
        [UnityTest] public IEnumerator WristTrackingWithoutFingersMustNotStopGloveGameplay()
        {
            yield return SceneManager.LoadSceneAsync("Boxing"); var game = Object.FindAnyObjectByType<BoxingGame>();
            game.SendMessage("FocusChanged", true); game.SendMessage("OnApplicationPause", false);
            StartHands(); BoxingTestHandProvider.leftTracked = BoxingTestHandProvider.rightTracked = true;
            BoxingTestHandProvider.wristOnly = true;
            yield return Pump(.4f, game);
            Assert.That(game.input.HasTracking, Is.True, "GloveBall accepts these exact wrist poses; Boxing must not require finger tips.");
            Vector3 left=game.input.Current.left; Quaternion rotation=game.input.Current.leftRotation;
            BoxingTestHandProvider.wristOnly=false; BoxingTestHandProvider.palmMenu=false;
            yield return Pump(.2f,game);
            Assert.That(Vector3.Distance(left,game.input.Current.left),Is.LessThan(.001f));
            Assert.That(Quaternion.Angle(rotation,game.input.Current.leftRotation),Is.LessThan(.1f));
            BoxingTestHandProvider.wristOnly=true;
            game.StartRound(); yield return Pump(.6f, game);
            Assert.That(game.Round.Countdown, Is.LessThan(3));
            Assert.That(game.feedback.Sends, Is.Zero);
        }
        [UnityTest] public IEnumerator LookingAtInputRowWithoutControllersNeverEntersDesktop()
        {
            yield return SceneManager.LoadSceneAsync("Boxing"); var game = Object.FindAnyObjectByType<BoxingGame>();
            yield return Pump(.4f, game);
            float end = Time.realtimeSinceStartup + 3;
            while (Time.realtimeSinceStartup < end)
            {
                var q = Quaternion.LookRotation(game.menu.rows[2].transform.position - game.input.headCamera.transform.position);
                InputSystem.QueueStateEvent(head, new XRSimulatedHMDState { isTracked = true, trackingState = 3, centerEyePosition = Vector3.up * 1.65f, centerEyeRotation = q, deviceRotation = q });
                yield return null;
            }
            Assert.That(game.input.mode.ToString(), Is.Not.EqualTo("Desktop"));
            Assert.That(game.input.HasTracking, Is.False, "No synthetic keyboard gloves may replace missing XR tracking.");
        }
    }
}
