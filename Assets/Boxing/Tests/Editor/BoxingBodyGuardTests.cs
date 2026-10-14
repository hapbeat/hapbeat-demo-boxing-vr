using System.Collections.Generic;
using Hapbeat.Boxing.Editor;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Hapbeat.Boxing.Tests
{
    // The player covers the torso with the forearms: the game's own sweep must take the forearm before the body.
    public sealed class BoxingBodyGuardTests
    {
        private BoxingGame game;
        private BoxingTuning original;
        private readonly List<(BoxingAttack attack, bool left, ImpactZone zone, ImpactPart part)> received = new List<(BoxingAttack, bool, ImpactZone, ImpactPart)>();
        [SetUp] public void Setup()
        {
            EditorSceneManager.OpenScene(BoxingProject.ScenePath);
            game = Object.FindAnyObjectByType<BoxingGame>();
            original = game.tuning; game.tuning = Object.Instantiate(original);
            game.feedback.forceSilent = true; game.feedback.sdkRoot.SetActive(false);
            game.Initialize(); BoxingSceneTests.SeedOpponent(game, 21); game.Round.Start(90, 10000);
            received.Clear();
            game.feedback.Reported += impact => { if (!impact.attack) received.Add((game.Opponent.Attack, game.Opponent.AttackLeft, impact.zone, impact.part)); };
        }
        [TearDown] public void Cleanup()
        { Object.DestroyImmediate(game.tuning); game.tuning = original; EditorSceneManager.NewScene(NewSceneSetup.EmptyScene); }

        // Gloves at the chest, forearms hanging straight down from them (the wrist points up).
        private static BoxerPose Cover(Vector3 left, Vector3 right)
        {
            var pose = BoxingSceneTests.Pose(); pose.left = pose.head + left; pose.right = pose.head + right;
            pose.leftRotation = pose.rightRotation = Quaternion.LookRotation(Vector3.up, Vector3.back);
            return pose;
        }
        private void Advance(BoxerPose pose) { for (int i = 0; i < 95 * 90; i++) game.Simulate(1f / 90, pose); }
        private static void AssertForearmOutsideBody(BoxerPose pose, Vector3 hand, Quaternion rotation)
        {
            for (int i = 0; i < BoxingForearm.Samples; i++)
            {
                Vector3 c = hand + rotation * BoxingForearm.Center(i);
                float nearest = float.PositiveInfinity;
                for (int j = 0; j < BoxingPlayerBody.Samples; j++)
                    nearest = Mathf.Min(nearest, Vector3.Distance(c, BoxingPlayerBody.Center(pose.head, pose.headRotation, j)));
                Assert.That(nearest, Is.GreaterThanOrEqualTo(BoxingPlayerBody.Radius + BoxingForearm.Radius - .005f), "The forearm rests on the torso, not inside it.");
            }
        }
        private int Count(BoxingAttack attack, System.Func<(BoxingAttack attack, bool left, ImpactZone zone, ImpactPart part), bool> match)
        { int n = 0; foreach (var r in received) if (r.attack == attack && match(r)) n++; return n; }
        private string Summary() { var b = new System.Text.StringBuilder(); foreach (var r in received) b.Append(r.attack).Append(r.left ? "L>" : "R>").Append(r.zone).Append('/').Append(r.part).Append(' '); return b.ToString(); }
        private static bool Guard((BoxingAttack attack, bool left, ImpactZone zone, ImpactPart part) r) => r.zone == ImpactZone.LeftGlove || r.zone == ImpactZone.RightGlove;

        // The torso stays behind the vertical plane through the eyes, for any head orientation; body strikes aim at its axis.
        [Test] public void BodyFrontNeverPassesTheEyePlane()
        {
            var head = new Vector3(.1f, 1.65f, -.2f);
            foreach (var euler in new[] { Vector3.zero, new Vector3(0, 90, 0), new Vector3(0, -135, 0), new Vector3(60, 20, 0),
                new Vector3(-80, 0, 0), new Vector3(10, 45, 30), new Vector3(89.9f, 0, 0), new Vector3(-89.9f, 70, 0) })
            {
                var rotation = Quaternion.Euler(euler);
                Vector3 face = rotation * Vector3.forward; face.y = 0; face.Normalize();
                Assert.That(Vector3.Dot(BoxingPlayerBody.Facing(rotation), face), Is.GreaterThan(.999f), euler.ToString());
                for (int i = 0; i < BoxingPlayerBody.Samples; i++)
                    Assert.That(Vector3.Dot(BoxingPlayerBody.Center(head, rotation, i) - head, face) + BoxingPlayerBody.Radius, Is.LessThanOrEqualTo(0), euler.ToString());
                Vector3 aim = BoxingPlayerBody.Aim(head, rotation);
                Assert.That(Vector3.Dot(aim - head, face), Is.EqualTo(-BoxingPlayerBody.Back).Within(1e-4f), euler.ToString());
                Assert.That(aim.y, Is.InRange(head.y - BoxingPlayerBody.Bottom, head.y - BoxingPlayerBody.Top));
            }
            Assert.That(BoxingPlayerBody.Facing(default), Is.EqualTo(Vector3.forward), "An unknown head rotation faces the opponent's side.");
        }
        // A hard straight meets the front of the torso while still fast, as one on the head does (the recording's hard strikes).
        [Test] public void HardBodyStraightsLandAsFastAsHardHeadStraights()
        {
            var speeds = new List<(ImpactZone zone, float speed)>();
            game.feedback.Reported += impact => { if (!impact.attack) speeds.Add((impact.zone, impact.relativeSpeed)); };
            game.Opponent.Scripted = true;
            var pose = BoxingSceneTests.Pose();
            for (int i = 0; i < 4 * 90; i++) game.Simulate(1f / 90, pose);
            float Strike(BoxingAttack attack, bool left, ImpactZone zone)
            {
                speeds.Clear(); int completed = game.Opponent.CompletedAttacks;
                game.Opponent.ScriptedStrike(attack, left, BoxingHapticRecord.HardStrikeScale);
                for (int i = 0; i < 4 * 90 && game.Opponent.CompletedAttacks == completed; i++) game.Simulate(1f / 90, pose);
                Assert.That(speeds.Count, Is.EqualTo(1), attack + (left ? " L" : " R")); Assert.That(speeds[0].zone, Is.EqualTo(zone));
                return speeds[0].speed;
            }
            float head = Strike(BoxingAttack.HeadStraight, true, ImpactZone.Head);
            foreach (bool left in new[] { true, false })
            {
                float body = Strike(BoxingAttack.BodyStraight, left, ImpactZone.Body);
                Assert.That(body, Is.GreaterThanOrEqualTo(game.tuning.hardHitSpeed), "body " + body + " head " + head);
                Assert.That(body, Is.GreaterThanOrEqualTo(.8f * head), "body " + body + " head " + head);
            }
        }
        [Test] public void ForearmsBeforeTheBellyStopBodyStraights()
        {
            var pose = Cover(new Vector3(-.10f, -.20f, .04f), new Vector3(.10f, -.20f, .04f));
            AssertForearmOutsideBody(pose, pose.left, pose.leftRotation); AssertForearmOutsideBody(pose, pose.right, pose.rightRotation);
            Advance(pose);
            Assert.That(Count(BoxingAttack.BodyStraight, Guard), Is.GreaterThan(0));
            Assert.That(Count(BoxingAttack.BodyStraight, r => Guard(r) && r.part == ImpactPart.Forearm), Is.GreaterThan(0), "The forearms, not only the gloves, take the straight.");
            Assert.That(Count(BoxingAttack.BodyStraight, r => !Guard(r)), Is.Zero, "No body straight may pass the forearms.");
        }
        [Test] public void ForearmsOnTheFlanksStopBodyHooksOnTheSameSide()
        {
            var pose = Cover(new Vector3(-.15f, -.20f, -.05f), new Vector3(.15f, -.20f, -.05f));
            AssertForearmOutsideBody(pose, pose.left, pose.leftRotation); AssertForearmOutsideBody(pose, pose.right, pose.rightRotation);
            Advance(pose);
            Assert.That(Count(BoxingAttack.BodyHook, r => r.part == ImpactPart.Forearm && r.zone == (r.left ? ImpactZone.LeftGlove : ImpactZone.RightGlove)), Is.GreaterThan(0));
            Assert.That(Count(BoxingAttack.BodyHook, r => r.zone != (r.left ? ImpactZone.LeftGlove : ImpactZone.RightGlove)), Is.Zero,
                "A body hook must be stopped by the forearm on its side.");
        }
        // The same dropped elbows as the covers above, spread clear of the torso. (Hands at the hips or spread wider
        // draw only head attacks from the opponent, so they cannot show that the body stays open.)
        [Test] public void LoweredOpenArmsLeaveTheBodyHit()
        {
            Advance(Cover(new Vector3(-.25f, -.20f, .05f), new Vector3(.25f, -.20f, .05f)));
            // A body hook swings in from the side and may still meet a forearm held out at that side; the straight cannot.
            Assert.That(Count(BoxingAttack.BodyStraight, r => r.zone == ImpactZone.Body), Is.GreaterThan(0), Summary());
            Assert.That(Count(BoxingAttack.BodyStraight, Guard), Is.Zero, Summary());
        }
        [Test] public void GlovesBeforeTheFaceStillStopHeadStraightsAndLeaveTheBodyOpen()
        {
            Advance(BoxingSceneTests.Pose(true));
            Assert.That(Count(BoxingAttack.HeadStraight, Guard), Is.GreaterThan(0));
            Assert.That(Count(BoxingAttack.HeadStraight, r => r.zone == ImpactZone.Head), Is.Zero);
            Assert.That(Count(BoxingAttack.BodyStraight, r => r.zone == ImpactZone.Body) + Count(BoxingAttack.BodyHook, r => r.zone == ImpactZone.Body), Is.GreaterThan(0));
        }
    }
}
