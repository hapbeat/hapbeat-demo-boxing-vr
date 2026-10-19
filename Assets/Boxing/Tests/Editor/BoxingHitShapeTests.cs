using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Hapbeat.Boxing.Tests
{
    // The opponent's head / torso hit shapes (BoxingOpponentAvatar.GetHitShapes) against its skinned mesh, and the reported
    // impact point on the target's surface. Silent: forceSilent, no Hapbeat output.
    public sealed class BoxingHitShapeTests
    {
        private const float Tolerance = .015f;
        private BoxingGame game;
        private BoxingOpponentAvatar Avatar => game.presentation.enemyAvatar;

        [SetUp] public void Setup()
        {
            EditorSceneManager.OpenScene(Editor.BoxingProject.ScenePath);
            game = Object.FindFirstObjectByType<BoxingGame>();
            // The shapes and guards are fitted at size 1; the size probe (BoxingTuning.opponentSizeScale) is checked elsewhere.
            original = game.tuning; game.tuning = Object.Instantiate(original); game.tuning.opponentSizeScale = 1;
            game.feedback.forceSilent = true; game.feedback.sdkRoot.SetActive(false); game.Initialize();
            game.input.SetTestPose(BoxingSceneTests.Pose());
        }
        [TearDown] public void Cleanup()
        {
            Object.DestroyImmediate(game.tuning); game.tuning = original;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
        }
        private BoxingTuning original;

        // The skinned mesh as rendered now (linear blend skinning, as the GPU does), triangles of the given bones only.
        private static List<Vector3> Triangles(BoxingOpponentAvatar avatar, params Transform[] parts)
        {
            var mesh = avatar.skin.sharedMesh; var source = mesh.vertices; var weights = mesh.boneWeights; var bones = avatar.skin.bones; var binds = mesh.bindposes;
            var matrices = new Matrix4x4[bones.Length];
            for (int i = 0; i < bones.Length; i++) matrices[i] = bones[i].localToWorldMatrix * binds[i];
            var world = new Vector3[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                var w = weights[i]; var v = source[i];
                world[i] = matrices[w.boneIndex0].MultiplyPoint3x4(v) * w.weight0 + matrices[w.boneIndex1].MultiplyPoint3x4(v) * w.weight1 +
                    matrices[w.boneIndex2].MultiplyPoint3x4(v) * w.weight2 + matrices[w.boneIndex3].MultiplyPoint3x4(v) * w.weight3;
            }
            var keep = new HashSet<int>(); foreach (var part in parts) keep.Add(System.Array.IndexOf(bones, part));
            var triangles = mesh.triangles; var result = new List<Vector3>();
            for (int i = 0; i < triangles.Length; i += 3)
            {
                int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                if (keep.Contains(weights[a].boneIndex0) && keep.Contains(weights[b].boneIndex0) && keep.Contains(weights[c].boneIndex0))
                { result.Add(world[a]); result.Add(world[b]); result.Add(world[c]); }
            }
            return result;
        }
        private static float MeshDistance(List<Vector3> triangles, Vector3 point)
        {
            float best = float.PositiveInfinity;
            for (int i = 0; i < triangles.Count; i += 3) best = Mathf.Min(best, Vector3.Distance(point, Closest(point, triangles[i], triangles[i + 1], triangles[i + 2])));
            return best;
        }
        // Closest point on a triangle (Ericson, Real-Time Collision Detection 5.1.5).
        private static Vector3 Closest(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 ab = b - a, ac = c - a, ap = p - a;
            float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap); if (d1 <= 0 && d2 <= 0) return a;
            Vector3 bp = p - b; float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp); if (d3 >= 0 && d4 <= d3) return b;
            float vc = d1 * d4 - d3 * d2; if (vc <= 0 && d1 >= 0 && d3 <= 0) return a + ab * (d1 / (d1 - d3));
            Vector3 cp = p - c; float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp); if (d6 >= 0 && d5 <= d6) return c;
            float vb = d5 * d2 - d1 * d6; if (vb <= 0 && d2 >= 0 && d6 <= 0) return a + ac * (d2 / (d2 - d6));
            float va = d3 * d6 - d5 * d4; if (va <= 0 && d4 - d3 >= 0 && d5 - d6 >= 0) return b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)));
            float denom = 1 / (va + vb + vc); return a + ab * (vb * denom) + ac * (vc * denom);
        }
        // A glove moving from start to end against static spheres: its centre at the first contact.
        private static bool Contact(BoxingHitSphere[] shapes, Vector3 start, Vector3 end, float radius, out Vector3 glove, out BoxingHitSphere touched)
        {
            float earliest = float.PositiveInfinity; touched = default;
            foreach (var s in shapes)
                if (BoxingCollision.Sweep(start, end, radius, s.center, s.center, s.radius, out float t) && t < earliest) { earliest = t; touched = s; }
            glove = Vector3.Lerp(start, end, earliest);
            return !float.IsPositiveInfinity(earliest);
        }

        private void Pose(string pose)
        {
            var o = game.Opponent; o.Reset(1.65f); Avatar.ResetReaction();
            if (pose == "guard") { o.ScriptedGuard(BoxingGuard.HeadStraight); for (int i = 0; i < 100; i++) o.Tick(.01f, new Vector3(0, 1.65f, .2f), Quaternion.identity, true); }
            if (pose == "bodyReaction") Avatar.React(true, 1);
            if (pose == "headReaction") Avatar.React(false, 1);
            Avatar.Render(o, 0);
        }

        [TestCase("rest")] [TestCase("guard")] [TestCase("bodyReaction")] [TestCase("headReaction")]
        public void GloveMeetsTheMeshSurfaceOfFaceChestBellyAndFlank(string pose)
        {
            Pose(pose);
            var head = new BoxingHitSphere[BoxingOpponentAvatar.HeadSpheres]; var torso = new BoxingHitSphere[BoxingOpponentAvatar.TorsoSpheres];
            Avatar.GetHitShapes(head, torso);
            var headMesh = Triangles(Avatar, Avatar.head); var torsoMesh = Triangles(Avatar, Avatar.spine, Avatar.chest);
            float r = game.tuning.gloveRadius, s = Avatar.transform.localScale.x;
            Vector3 front = Avatar.chest.rotation * Vector3.forward, side = Avatar.chest.rotation * Vector3.right;
            Vector3 chest = Avatar.chest.position + Avatar.chest.rotation * (new Vector3(0, .065f, .012f) * s);
            Vector3 belly = Avatar.spine.position + Avatar.spine.rotation * (new Vector3(0, .035f, .002f) * s);
            var cases = new (string name, BoxingHitSphere[] shapes, List<Vector3> mesh, Vector3 target, Vector3 direction)[] {
                ("face", head, headMesh, Avatar.HeadCenter, -(Avatar.head.rotation * Vector3.forward)),
                ("chest", torso, torsoMesh, chest, -front), ("belly", torso, torsoMesh, belly, -front),
                ("flank", torso, torsoMesh, chest, side), ("other flank", torso, torsoMesh, chest, -side) };
            foreach (var c in cases)
            {
                // The glove travels along `direction` (towards the target) from .6 m away.
                Vector3 start = c.target - c.direction * .6f;
                Assert.That(Contact(c.shapes, start, c.target, r, out var glove, out var touched), Is.True, c.name);
                float gap = MeshDistance(c.mesh, glove + c.direction * r);
                Assert.That(gap, Is.LessThanOrEqualTo(Tolerance), $"{pose} {c.name}: glove surface {gap:F3} m from the mesh");
                Vector3 point = touched.center + (glove - touched.center).normalized * touched.radius;
                Assert.That(MeshDistance(c.mesh, point), Is.LessThanOrEqualTo(Tolerance), $"{pose} {c.name}: contact point off the mesh");
            }
        }

        [Test] public void ShapesFollowTheAvatarScaleAndStayAllocationFree()
        {
            var head = new BoxingHitSphere[BoxingOpponentAvatar.HeadSpheres]; var torso = new BoxingHitSphere[BoxingOpponentAvatar.TorsoSpheres];
            game.Opponent.Reset(1.3f); Avatar.Render(game.Opponent, 0); Avatar.GetHitShapes(head, torso);
            float small = torso[0].radius, smallScale = Avatar.transform.localScale.x;
            game.Opponent.Reset(1.9f); Avatar.Render(game.Opponent, 0);
            long before = System.GC.GetAllocatedBytesForCurrentThread();
            Avatar.GetHitShapes(head, torso);
            Assert.That(System.GC.GetAllocatedBytesForCurrentThread() - before, Is.Zero);
            Assert.That(torso[0].radius / small, Is.EqualTo(Avatar.transform.localScale.x / smallScale).Within(.001f));
            Assert.That(Avatar.transform.localScale.x, Is.GreaterThan(smallScale * 1.3f));
        }

        // A punch through the game: the reported impact lies on the opponent's mesh where the glove met it.
        [TestCase(true)] [TestCase(false)]
        public void ImpactPointLiesOnTheSurfaceItHit(bool face)
        {
            game.StartRound(); game.Opponent.Scripted = true;
            var pose = BoxingSceneTests.Pose();
            for (int i = 0; i < 350; i++) game.Simulate(.01f, pose);
            Assert.That(game.Round.Phase, Is.EqualTo(BoxingPhase.Fighting));
            float gap = float.NaN; bool body = false;
            game.feedback.Reported += impact =>
            {
                if (!impact.attack || impact.surface != ImpactSurface.Body || !float.IsNaN(gap)) return;
                body = true; gap = MeshDistance(Triangles(Avatar, face ? new[] { Avatar.head } : new[] { Avatar.spine, Avatar.chest }), impact.point);
            };
            Vector3 target = face ? game.Opponent.Head + Vector3.up * .07f : Avatar.spine.position + Avatar.spine.rotation * (new Vector3(0, .035f, 0) * Avatar.transform.localScale.x);
            Vector3 start = target + Vector3.back * .55f;
            pose.left = start; for (int i = 0; i < 20; i++) game.Simulate(.01f, pose);
            for (int i = 1; i <= 25 && !body; i++) { pose.left = Vector3.Lerp(start, target, i / 25f); game.Simulate(.01f, pose); }
            Assert.That(body, Is.True, "The punch landed.");
            Assert.That(gap, Is.LessThanOrEqualTo(Tolerance), $"impact {gap:F3} m from the mesh");
            Assert.That(game.feedback.Sends, Is.Zero);
        }
    }
}
