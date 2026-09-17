using System.Collections;
using System.Collections.Generic;
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
                bool valid = tracked && (!wristOnly || id == XRHandJointID.Wrist);
                joints[i] = XRHandProviderUtility.CreateJoint(side, valid ? XRHandJointTrackingState.Pose : XRHandJointTrackingState.None, id, new Pose(p, rotation));
            }
        }
    }

    public sealed class BoxingHandInputTests : InputTestFixture
    {
        private XRHandSubsystem hands;
        private XRSimulatedHMD head;
        public override void Setup()
        {
            base.Setup();
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            BoxingTestHandProvider.leftTracked = BoxingTestHandProvider.rightTracked = BoxingTestHandProvider.palmMenu = false;
            BoxingTestHandProvider.wristOnly = false;
            InputSystem.RegisterLayout<XRSimulatedHMD>(); head = InputSystem.AddDevice<XRSimulatedHMD>();
        }
        public override void TearDown() { hands?.Destroy(); hands = null; base.TearDown(); }
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
        [UnityTest] public IEnumerator HandsCanArriveAfterStartupStartByGazeAndRecoverWithoutControllers()
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
            Assert.That(game.menu.IsOpen, Is.False, "Gaze must start without an A button or controller.");
            yield return Pump(0.4f, game);
            Assert.That(game.Round.Countdown, Is.LessThan(3));
            BoxingTestHandProvider.rightTracked = false;
            yield return Pump(0.2f, game); Assert.That(game.Paused, Is.True);
            BoxingTestHandProvider.rightTracked = true;
            yield return Pump(0.5f, game); Assert.That(game.Paused, Is.False, game.PauseReason);
            BoxingTestHandProvider.rightTracked = false; BoxingTestHandProvider.palmMenu = true;
            yield return Pump(1.0f, game);
            Assert.That(game.menu.IsOpen, Is.True, "Left palm opens the menu even if the other hand is occluded.");
            Assert.That(game.feedback.Sends, Is.Zero);
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
