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

        // Steps 1-2: the rings stand before the guarding gloves at face height / the belly, the whole rim in front of the skinned
        // mesh (no ring point behind it) and at most a few centimetres away.
        [Test] public void TutorialRingsStayInFrontOfTheBody()
        {
            game.tuning.tutorialStepTimeoutSeconds = 1;
            Run(game.tuning.autoStartSeconds + 1, Standing(1.65f));
            Assert.That(game.TutorialStep, Is.EqualTo(1));
            Run(.6f, Standing(1.65f));
            game.presentation.Render(game, Standing(1.65f), true, 0);
            Assert.That(game.TutorialTarget(out var face), Is.True);
            Assert.That(face.y, Is.EqualTo(game.Opponent.Head.y).Within(1e-4f));
            Assert.That(face.z, Is.LessThan(Mathf.Min(game.Opponent.Left.z, game.Opponent.Right.z)), "towards the player");
            float faceGap = AssertRimInFront("step 1");
            Assert.That(faceGap, Is.InRange(.015f, .08f), "Step 1: just before the gloves");

            Assert.That(RunUntil(() => game.TutorialStep == 2, 3), Is.True);
            Run(.3f, Standing(1.65f));
            game.presentation.Render(game, Standing(1.65f), true, 0);
            Assert.That(game.TutorialTarget(out var belly), Is.True);
            Assert.That(belly.y, Is.LessThan(game.Opponent.Body.y - .05f));
            float bellyGap = AssertRimInFront("step 2");
            Assert.That(bellyGap, Is.InRange(.015f, .08f), "Step 2: just before the belly");
            Assert.That(game.feedback.Sends, Is.Zero);
        }
        // Every point of the rendered ring against the skinned mesh along the player's line of sight (+Z): returns the
        // smallest gap from the ring's centre line to the mesh behind it.
        private float AssertRimInFront(string label)
        {
            var avatar = game.presentation.enemyAvatar; var marker = game.presentation.TutorialMarker;
            Assert.That(marker != null && marker.gameObject.activeSelf, Is.True, label);
            var triangles = BoxingHitShapeTests.Triangles(avatar, avatar.skin.bones);
            float half = marker.widthMultiplier * .5f, closest = float.PositiveInfinity;
            for (int i = 0; i < marker.positionCount; i++)
            {
                Vector3 local = marker.GetPosition(i), point = marker.transform.TransformPoint(local);
                // The line's outer edge as well as its centre.
                foreach (var p in new[] { point, marker.transform.TransformPoint(local * (1 + half / local.magnitude)) })
                {
                    float mesh = MeshFrontZ(triangles, p.x, p.y);
                    if (float.IsPositiveInfinity(mesh)) continue;
                    Assert.That(p.z, Is.LessThan(mesh), $"{label}: ring point {i} behind the mesh ({p.z:F3} vs {mesh:F3})");
                    closest = Mathf.Min(closest, mesh - p.z);
                }
            }
            Assert.That(closest, Is.LessThan(float.PositiveInfinity), label + ": the ring is over the body");
            return closest;
        }
        // The nearest mesh surface along +Z through (x, y) (Moller-Trumbore), +inf if none.
        private static float MeshFrontZ(System.Collections.Generic.List<Vector3> t, float x, float y)
        {
            float best = float.PositiveInfinity; Vector3 o = new Vector3(x, y, -10), d = Vector3.forward;
            for (int i = 0; i < t.Count; i += 3)
            {
                Vector3 e1 = t[i + 1] - t[i], e2 = t[i + 2] - t[i], p = Vector3.Cross(d, e2); float det = Vector3.Dot(e1, p);
                if (Mathf.Abs(det) < 1e-9f) continue;
                float inv = 1 / det; Vector3 s = o - t[i]; float u = Vector3.Dot(s, p) * inv; if (u < 0 || u > 1) continue;
                Vector3 q = Vector3.Cross(s, e1); float v = Vector3.Dot(d, q) * inv; if (v < 0 || u + v > 1) continue;
                float dist = Vector3.Dot(e2, q) * inv; if (dist > 0) best = Mathf.Min(best, o.z + dist);
            }
            return best;
        }
        private bool RunUntil(System.Func<bool> done, float seconds)
        {
            var pose = Standing(1.65f);
            for (int i = 0; i < seconds * 100 && !done(); i++) game.Simulate(.01f, pose);
            return done();
        }
    }
}
