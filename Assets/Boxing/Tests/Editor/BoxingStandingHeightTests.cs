using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Hapbeat.Boxing.Tests
{
    // The opponent is sized from the player's standing eye height (BoxingInput.StandingEyeHeight), not from the posture at a
    // recenter, and the tutorial ring markers sit on the surfaces they mark. Silent: forceSilent, no Hapbeat output.
    public sealed class BoxingStandingHeightTests
    {
        private BoxingGame game;
        private BoxingTuning original;

        [SetUp] public void Setup()
        {
            EditorSceneManager.OpenScene(Editor.BoxingProject.ScenePath);
            game = Object.FindFirstObjectByType<BoxingGame>();
            original = game.tuning; game.tuning = Object.Instantiate(original);
            game.feedback.forceSilent = true; game.feedback.sdkRoot.SetActive(false); game.Initialize();
            game.input.SetTestPose(Standing(1.65f));
        }
        [TearDown] public void Cleanup()
        {
            Object.DestroyImmediate(game.tuning); game.tuning = original;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
        }

        private static BoxerPose Standing(float head) { var pose = BoxingSceneTests.Pose(); pose.head.y = head; pose.left.y += head - 1.65f; pose.right.y += head - 1.65f; return pose; }
        private void Run(float seconds, BoxerPose pose) { game.input.SetTestPose(pose); for (int i = 0; i < Mathf.CeilToInt(seconds * 100); i++) game.Simulate(.01f, pose); }
        private BoxingInput Input => game.input;

        [Test] public void TheEstimateStartsAtASaneDefaultBeforeTracking()
        {
            Assert.That(Input.StandingEyeHeight, Is.EqualTo(BoxingInput.StandingDefault));
            Assert.That(BoxingInput.StandingDefault, Is.InRange(BoxingInput.StandingMin, BoxingInput.StandingMax));
            // Untracked frames and less than a window of tracking leave it.
            for (int i = 0; i < 100; i++) Input.ObserveHead(false, 1.9f, .01f);
            for (int i = 0; i < 25; i++) Input.ObserveHead(true, 1.9f, .01f);
            Assert.That(Input.StandingEyeHeight, Is.EqualTo(BoxingInput.StandingDefault));
            for (int i = 0; i < 10; i++) Input.ObserveHead(true, 1.9f, .01f);
            Assert.That(Input.StandingEyeHeight, Is.EqualTo(1.9f).Within(1e-4f));
        }

        [Test] public void ShortSpikesAreIgnoredAndTheEstimateNeverShrinksAndStaysInRange()
        {
            for (int i = 0; i < 100; i++) Input.ObserveHead(true, 1.6f, .01f);
            Assert.That(Input.StandingEyeHeight, Is.EqualTo(1.6f).Within(1e-4f));
            // A 0.2 s rise (a tracking jump, a hop) does not count; a crouch never lowers it.
            for (int i = 0; i < 20; i++) Input.ObserveHead(true, 1.95f, .01f);
            for (int i = 0; i < 100; i++) Input.ObserveHead(true, 1.6f, .01f);
            Assert.That(Input.StandingEyeHeight, Is.EqualTo(1.6f).Within(1e-4f));
            for (int i = 0; i < 200; i++) Input.ObserveHead(true, 1.35f, .01f);
            Assert.That(Input.StandingEyeHeight, Is.EqualTo(1.6f).Within(1e-4f));
            for (int i = 0; i < 100; i++) Input.ObserveHead(true, 2.4f, .01f);
            Assert.That(Input.StandingEyeHeight, Is.EqualTo(BoxingInput.StandingMax));
        }

        [Test] public void ACrouchedStanceAtTheRecenterKeepsTheSizeAndTutorialAndRoundMatch()
        {
            // Standing 1.75 m until the tutorial starts by itself, then crouched 0.2 m lower in the boxing stance.
            Run(game.tuning.autoStartSeconds + 1, Standing(1.75f));
            Assert.That(game.Round.Phase, Is.EqualTo(BoxingPhase.Tutorial));
            Assert.That(game.SizeEyeHeight, Is.EqualTo(1.75f).Within(1e-4f));
            float tutorialStature = game.SizeOpponentStature;
            var crouched = Standing(1.55f);
            Run(1, crouched);
            game.RecenterPlayer();
            Assert.That(Input.ReferenceEyeHeight, Is.EqualTo(1.55f).Within(1e-4f), "The recenter reads the crouch...");
            Assert.That(game.Opponent.Head.y, Is.EqualTo(1.75f).Within(1e-4f), "...but the opponent keeps the standing size.");
            Assert.That(game.SizeOpponentStature, Is.EqualTo(tutorialStature).Within(1e-4f));
            game.StartRound();
            Assert.That(game.Opponent.Head.y, Is.EqualTo(1.75f).Within(1e-4f), "The round uses the tutorial's size.");
            Assert.That(game.SizeOpponentStature, Is.EqualTo(tutorialStature).Within(1e-4f));
            game.presentation.Render(game, crouched, true, .01f);
            StringAssert.Contains("EYE 1.75m (now 1.55m)", game.presentation.statusText.text);
            // Standing taller later does not resize the running round; the next start reads it.
            Run(1, Standing(1.85f));
            game.RecenterPlayer();
            Assert.That(game.Opponent.Head.y, Is.EqualTo(1.75f).Within(1e-4f));
            game.StartRound();
            Assert.That(game.Opponent.Head.y, Is.EqualTo(1.85f).Within(1e-4f));
        }

        // Steps 1-2 mark where a straight punch meets the guarding gloves at face height / the torso at belly height.
        [Test] public void TutorialRingsSitOnTheSurfacesTheyMark()
        {
            game.tuning.tutorialStepTimeoutSeconds = 1;
            Run(game.tuning.autoStartSeconds + .5f, Standing(1.65f));
            Assert.That(game.TutorialStep, Is.EqualTo(1));
            Run(.6f, Standing(1.65f));
            Assert.That(game.TutorialTarget(out var face), Is.True);
            float gloveGap = float.PositiveInfinity;
            foreach (var glove in new[] { game.Opponent.Left, game.Opponent.Right })
                gloveGap = Mathf.Min(gloveGap, Vector3.Distance(face, glove) - game.tuning.enemyGloveRadius);
            Assert.That(gloveGap, Is.InRange(0f, .01f), "Step 1: on the front of the guarding gloves");
            Assert.That(face.y, Is.EqualTo(game.Opponent.Head.y).Within(1e-4f));
            Assert.That(face.z, Is.LessThan(Mathf.Min(game.Opponent.Left.z, game.Opponent.Right.z)), "towards the player");

            Assert.That(RunUntil(() => game.TutorialStep == 2, 3), Is.True);
            Run(.3f, Standing(1.65f));
            Assert.That(game.TutorialTarget(out var belly), Is.True);
            var avatar = game.presentation.enemyAvatar;
            var head = new BoxingHitSphere[BoxingOpponentAvatar.HeadSpheres]; var torso = new BoxingHitSphere[BoxingOpponentAvatar.TorsoSpheres];
            avatar.GetHitShapes(head, torso);
            float shapeGap = float.PositiveInfinity;
            foreach (var s in torso) shapeGap = Mathf.Min(shapeGap, Vector3.Distance(belly, s.center) - s.radius);
            Assert.That(shapeGap, Is.InRange(0f, .01f), "Step 2: on the torso hit shape's front");
            float meshGap = BoxingHitShapeTests.MeshDistance(BoxingHitShapeTests.Triangles(avatar, avatar.spine, avatar.chest), belly);
            Assert.That(meshGap, Is.LessThan(.02f), $"Step 2: {meshGap:F3} m from the belly mesh");
            Assert.That(belly.y, Is.LessThan(game.Opponent.Body.y - .05f));
            Assert.That(game.feedback.Sends, Is.Zero);
        }
        private bool RunUntil(System.Func<bool> done, float seconds)
        {
            var pose = Standing(1.65f);
            for (int i = 0; i < seconds * 100 && !done(); i++) game.Simulate(.01f, pose);
            return done();
        }
    }
}
