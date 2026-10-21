using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Hapbeat.Boxing.Tests
{
    // The forced eye height (BoxingInput.ApplyEyeHeight, the Demo Switch's IDemoSessionHeight): the camera at 1.65 m above the
    // game floor after 視線をリセット whatever the Quest's floor, the opponent sized for it; and the tutorial ring markers in
    // front of the body. Silent: forceSilent, no Hapbeat output.
    public sealed class BoxingEyeHeightTests
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

        // The Quest's floor off by err (its tracking floor err below the real one: the head reads err higher): 視線をリセット
        // puts the camera 1.65 m above the game floor, and the opponent's head is at the fixed 1.65 m.
        [TestCase(.3f)] [TestCase(-.3f)]
        public void TheResetForcesTheEyeHeightWhateverTheQuestFloor(float err)
        {
            float floor = Input.GameFloor;
            Input.ClearTestPose();
            Input.headCamera.transform.position = new Vector3(0, floor + 1.65f + err, .2f);
            game.RecenterPlayer();
            Assert.That(Input.headCamera.transform.position.y - floor, Is.EqualTo(BoxingInput.EyeHeight).Within(1e-4f));
            Assert.That(BoxingInput.EyeHeight, Is.EqualTo(1.65f));
            Assert.That(game.Opponent.Head.y - floor, Is.EqualTo(1.65f).Within(1e-4f));
            Assert.That(game.presentation.arena.position.y, Is.EqualTo(floor).Within(1e-4f), "The arena stays on the game floor.");
        }

        [Test] public void ACrouchAtTheResetDoesNotChangeTheOpponentAndRepeatedCallsDoNotAccumulate()
        {
            float floor = Input.GameFloor;
            Input.ClearTestPose();
            Input.headCamera.transform.position = new Vector3(0, floor + 1.45f, .2f);
            game.RecenterPlayer();
            float stature = game.SizeOpponentStature;
            Assert.That(game.Opponent.Head.y - floor, Is.EqualTo(1.65f).Within(1e-4f), "The opponent keeps its fixed size.");
            Assert.That(Input.headCamera.transform.position.y - floor, Is.EqualTo(1.65f).Within(1e-4f));
            float originY = Input.origin.transform.position.y;
            var host = game.gameObject.AddComponent<BoxingSessionHost>();
            for (int i = 0; i < 5; i++) host.ApplyEyeHeight(BoxingInput.EyeHeight);
            Assert.That(Input.origin.transform.position.y, Is.EqualTo(originY).Within(1e-5f), "Repeated calls do not accumulate.");
            // Crouching later and starting a round or the tutorial (their own recenter) moves nothing vertically.
            Input.headCamera.transform.position += Vector3.down * .25f; originY = Input.origin.transform.position.y;
            game.StartRound();
            Assert.That(Input.origin.transform.position.y, Is.EqualTo(originY).Within(1e-5f));
            game.StartTutorial();
            Assert.That(Input.origin.transform.position.y, Is.EqualTo(originY).Within(1e-5f));
            Assert.That(game.Opponent.Head.y - floor, Is.EqualTo(1.65f).Within(1e-4f));
            Assert.That(game.SizeOpponentStature, Is.EqualTo(stature).Within(1e-4f));
        }

        // The Demo Switch's 視線をリセット goes through the host: its horizontal recenter, then the host's eye height.
        [Test] public void TheDemoSwitchResetUsesBoxingsHeight()
        {
            Assert.That(Hapbeat.DemoSwitch.DemoSessionDescriptor.TryParse(System.IO.File.ReadAllText("Assets/StreamingAssets/hapbeat-demo-session.json"), out var descriptor, out var error), Is.True, error);
            Hapbeat.DemoSwitch.DemoSession.ResetForTests(null, "boxing", descriptor);
            try
            {
                var host = game.gameObject.AddComponent<BoxingSessionHost>();
                Hapbeat.DemoSwitch.DemoSession.RegisterHost(host);
                float floor = Input.GameFloor;
                Input.ClearTestPose();
                Input.headCamera.transform.position = new Vector3(.3f, floor + 1.95f, .5f);
                Hapbeat.DemoSwitch.DemoRecenter.ResetView();
                Assert.That(Input.headCamera.transform.position.y - floor, Is.EqualTo(1.65f).Within(1e-4f));
                Assert.That(game.Opponent.Head.y - floor, Is.EqualTo(1.65f).Within(1e-4f));
            }
            finally { Hapbeat.DemoSwitch.DemoSession.ResetForTests(); }
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
