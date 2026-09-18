using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Hapbeat.Boxing.Tests
{
    public sealed class BoxingPunchTests
    {
        private BoxingTuning tuning;
        private BoxingPunch punch;
        [SetUp] public void Setup() { tuning = ScriptableObject.CreateInstance<BoxingTuning>(); punch = new BoxingPunch(); }
        [TearDown] public void Cleanup() => Object.DestroyImmediate(tuning);
        private void Rest(Vector3 p) { for (int i = 0; i < 12; i++) punch.Sample(p, 0.01f, tuning); }
        private float Stroke(Vector3 start, Vector3 end, int frames = 20)
        {
            Rest(start);
            for (int i = 1; i <= frames; i++) punch.Sample(Vector3.Lerp(start, end, i / (float)frames), 0.01f, tuning);
            return punch.Consume(end, tuning);
        }
        [Test] public void FastWristSnapIsWeakButLongStrokeIsStrong()
        {
            Assert.That(Stroke(Vector3.forward * 0.2f, Vector3.forward * 0.25f, 1), Is.Zero);
            punch.Reset(); Assert.That(Stroke(Vector3.forward * 0.2f, Vector3.forward * 0.7f), Is.EqualTo(1));
        }
        [Test] public void FrontGuardQuarterMetrePunchIsHardWithoutRearWindup()
        {
            Assert.That(Stroke(Vector3.forward*.38f,Vector3.forward*.63f),Is.GreaterThanOrEqualTo(tuning.punchHardStrength));
        }
        [Test] public void DistanceNotSpeedControlsDamage()
        {
            float fast = Stroke(Vector3.forward * 0.1f, Vector3.forward * 0.4f, 5);
            punch.Reset(); float slow = Stroke(Vector3.forward * 0.1f, Vector3.forward * 0.4f, 40);
            Assert.That(fast, Is.EqualTo(slow).Within(0.0001f)); Assert.That(fast, Is.GreaterThan(0));
        }
        [Test] public void FullThreeDimensionalDisplacementSupportsHooks()
        {
            Assert.That(Stroke(new Vector3(-0.4f, -0.2f, 0.2f), new Vector3(0, 0, 0.4f)), Is.GreaterThan(0.95f));
        }
        [Test] public void RepeatedContactConsumesStrokeUntilReturnToBody()
        {
            Assert.That(Stroke(Vector3.forward * 0.2f, Vector3.forward * 0.7f), Is.EqualTo(1));
            Assert.That(Stroke(Vector3.forward * 0.65f, Vector3.forward * 0.7f), Is.Zero);
            Assert.That(Stroke(Vector3.forward * 0.2f, Vector3.forward * 0.7f), Is.EqualTo(1));
        }
        [Test] public void ZigzagPathCannotAccumulateDistance()
        {
            Vector3 start = Vector3.forward * 0.2f; Rest(start);
            for (int i = 0; i < 80; i++) punch.Sample(start + Vector3.right * (i % 2 == 0 ? 0.04f : -0.04f), 0.01f, tuning);
            Assert.That(punch.Consume(start + Vector3.right * 0.04f, tuning), Is.Zero);
        }
        [Test] public void ResetAndLongPauseCannotRetainStoredPunch()
        {
            Rest(Vector3.zero); punch.Sample(Vector3.forward * 0.3f, 0.01f, tuning);
            punch.Reset(); Assert.That(punch.Consume(Vector3.forward * 0.6f, tuning), Is.Zero);
        }
        [Test] public void RearPreparationHasNoBonusAndExpiryIsBounded()
        {
            float front = Stroke(Vector3.forward * 0.1f, Vector3.forward * 0.4f);
            punch.Reset(); float rear = Stroke(Vector3.back * 0.2f, Vector3.forward * 0.1f);
            Assert.That(rear, Is.EqualTo(front).Within(0.0001f));
            punch.Reset(); Rest(Vector3.zero);
            for (int i = 1; i <= 130; i++) punch.Sample(Vector3.forward * (i * 0.006f), 0.01f, tuning);
            Assert.That(punch.Consume(Vector3.forward * 0.78f, tuning), Is.Zero);
        }
        [Test] public void TapsAndFullPunchesHaveIndependentDamageAndFeedback()
        {
            var tap = new BoxingImpact(ImpactZone.LeftGlove, 10, Vector3.zero, tuning, ImpactSurface.Body, 0);
            var full = new BoxingImpact(ImpactZone.LeftGlove, 1, Vector3.zero, tuning, ImpactSurface.Body, 1);
            Assert.That(tap.damage, Is.EqualTo(0.5f)); Assert.That(tap.hard, Is.False);
            Assert.That(full.damage, Is.EqualTo(20)); Assert.That(full.hard, Is.True); Assert.That(full.gain, Is.GreaterThan(tap.gain));
        }
        [Test] public void AuthoredStartIsInComfortableReachAndRecenterResetsDebugOffset()
        {
            EditorSceneManager.OpenScene(Editor.BoxingProject.ScenePath);
            try
            {
                var game = Object.FindFirstObjectByType<BoxingGame>();
                game.feedback.forceSilent = true; game.feedback.sdkRoot.SetActive(false); game.Initialize();
                var input = game.input; input.mode = BoxingInputMode.Controllers;
                input.headCamera.transform.position = new Vector3(0.3f, 1.65f, -0.4f);
                input.headCamera.transform.rotation = Quaternion.Euler(0, 65, 0);
                game.RecenterPlayer();
                Assert.That(input.headCamera.transform.position.z, Is.EqualTo(0.2f).Within(0.001f));
                Assert.That(Mathf.DeltaAngle(input.headCamera.transform.eulerAngles.y, 0), Is.EqualTo(0).Within(0.01f));
                float reach = game.Opponent.Head.z - input.StartPosition.z - game.tuning.enemyHeadRadius - game.tuning.gloveRadius;
                Assert.That(reach, Is.InRange(0.55f, 0.7f));
                Vector3 before = input.headCamera.transform.position;
                Assert.That(input.ApplyDebugMove(Vector2.right, 0.1f, game.Opponent.Root).x, Is.GreaterThan(0));
                Assert.That(input.headCamera.transform.position.x, Is.GreaterThan(before.x));
                game.RecenterPlayer(); Assert.That(input.headCamera.transform.position.x, Is.EqualTo(input.StartPosition.x).Within(0.001f));
                input.debugStickMovement = false;
                Assert.That(input.ApplyDebugMove(Vector2.up, 0.1f, game.Opponent.Root), Is.EqualTo(Vector3.zero));
            }
            finally { EditorSceneManager.NewScene(NewSceneSetup.EmptyScene); }
        }
        [Test] public void HandsAreTheAuthoredDefaultWithoutNeedingAControllerToSelectThem()
        {
            EditorSceneManager.OpenScene(Editor.BoxingProject.ScenePath);
            try { Assert.That(Object.FindFirstObjectByType<BoxingInput>().mode, Is.EqualTo(BoxingInputMode.Hands)); }
            finally { EditorSceneManager.NewScene(NewSceneSetup.EmptyScene); }
        }
        [Test] public void ReturnRequiresProximityToHeadNotJustShallowForwardDepth()
        {
            Assert.That(Stroke(new Vector3(.8f,0,.1f),new Vector3(.45f,0,.1f)),Is.Zero);
            punch.Reset(); Assert.That(Stroke(new Vector3(.35f,-.2f,.2f),new Vector3(0,-.2f,.5f)),Is.GreaterThan(.95f));
        }
        [TestCase(-.3f)] [TestCase(0f)] [TestCase(.3f)]
        public void IdenticalHeadRelativeStrokeHasSameStrengthWhilePlayerMoves(float movement)
        {
            Vector3 head=new Vector3(0,1.65f,movement);
            Vector3 start=head+new Vector3(.1f,-.15f,.3f); Rest(start-head);
            Vector3 end=default;
            for(int i=1;i<=20;i++)
            {
                head.z+=movement/20;
                end=head+new Vector3(.1f,-.15f,.3f+i*.015f);
                punch.Sample(end-head,.01f,tuning);
            }
            Assert.That(punch.Consume(end-head,tuning),Is.EqualTo(tuning.PunchStrength(.3f)).Within(.001f));
        }
    }
}
