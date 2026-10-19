using System.Text;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Hapbeat.Boxing.Tests
{
    // The opponent's guard poses and glove orientation on the rendered avatar. Silent: forceSilent, no Hapbeat output.
    public sealed class BoxingGuardMotionTests
    {
        private BoxingGame game;
        private BoxingOpponent Opponent => game.Opponent;
        private BoxingOpponentAvatar Avatar => game.presentation.enemyAvatar;
        private static readonly Vector3 PlayerHead = new Vector3(0, 1.65f, .2f);
        private static readonly BoxingGuard[] Guards = { BoxingGuard.HeadStraight, BoxingGuard.BodyStraight, BoxingGuard.HeadHook, BoxingGuard.BodyHook };

        [SetUp] public void Setup()
        {
            EditorSceneManager.OpenScene(Editor.BoxingProject.ScenePath);
            game = Object.FindFirstObjectByType<BoxingGame>();
            original = game.tuning; game.tuning = Object.Instantiate(original);
            game.feedback.forceSilent = true; game.feedback.sdkRoot.SetActive(false); game.Initialize();
            game.input.SetTestPose(BoxingSceneTests.Pose());
            Opponent.Reset(1.65f); Opponent.Scripted = true;
            // NUnit keeps one fixture instance for all cases: start each with no previous frame.
            maxStep = 0; maxStepAt = ""; haveLast = false;
        }
        [TearDown] public void Cleanup()
        {
            Object.DestroyImmediate(game.tuning); game.tuning = original;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
        }
        private BoxingTuning original;

        private float maxStep; private string maxStepAt = "";
        private Quaternion lastLeft, lastRight; private bool haveLast; private Vector3 lastBend; private float lastTwist;
        private Vector3 Bend() => (Vector3)typeof(BoxingOpponentAvatar).GetField("rightBend", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(Avatar);
        private float TwistR() => (float)typeof(BoxingOpponentAvatar).GetField("rightTwist", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(Avatar);
        private Vector3 lastLeftElbow, lastRightElbow, lastLeftDir, lastRightDir;
        private void Frame(string label)
        {
            const float dt = 1f / 90;
            Opponent.Tick(dt, PlayerHead, Quaternion.identity, true); Avatar.Render(Opponent, dt);
            if (haveLast)
                foreach (var (now, before, side, elbow, oldElbow, dir, oldDir, glove) in new[] {
                    (Avatar.leftHand.rotation, lastLeft, "L", Avatar.leftFore.position, lastLeftElbow, (Avatar.leftHand.position - Avatar.leftFore.position).normalized, lastLeftDir, Opponent.Left),
                    (Avatar.rightHand.rotation, lastRight, "R", Avatar.rightFore.position, lastRightElbow, (Avatar.rightHand.position - Avatar.rightFore.position).normalized, lastRightDir, Opponent.Right) })
                {
                    float step = Quaternion.Angle(now, before);
                    if (step > maxStep) { maxStep = step; maxStepAt = $"{label} {side}: bendR {lastBend:F2}->{Bend():F2} twistR {lastTwist:F1}->{TwistR():F1} elbow {oldElbow - Opponent.Head:F3}->{elbow - Opponent.Head:F3} forearm {oldDir:F2}->{dir:F2} glove {glove - Opponent.Head:F3}"; }
                }
            lastLeft = Avatar.leftHand.rotation; lastRight = Avatar.rightHand.rotation; haveLast = true; lastBend = Bend(); lastTwist = TwistR();
            lastLeftElbow = Avatar.leftFore.position; lastRightElbow = Avatar.rightFore.position;
            lastLeftDir = (Avatar.leftHand.position - Avatar.leftFore.position).normalized; lastRightDir = (Avatar.rightHand.position - Avatar.rightFore.position).normalized;
        }
        private void Hold(BoxingGuard guard, float side, float seconds)
        {
            Opponent.ScriptedGuard(guard, side);
            for (int i = 0; i < seconds * 90; i++) Frame(guard + (side < 0 ? " L" : " R"));
        }
        // How far the glove's padded knuckle area turns from the player: it covers the fist's front (local Y, cuff -> knuckles)
        // and the back of the glove (local Z), so the nearer of the two counts. An upright guard shows the back, a forearm
        // pointing at the player (the body guards) the front.
        private static float KnuckleAngle(Transform hand, Vector3 toPlayer) =>
            Mathf.Min(Vector3.Angle(hand.rotation * Vector3.up, toPlayer), Vector3.Angle(hand.rotation * Vector3.forward, toPlayer));

        // Seeded: a strike's retraction ends in a randomly chosen cover.
        [TestCase(1)] [TestCase(2)] [TestCase(3)]
        public void GloveOrientationIsContinuousThroughGuardsStrikesAndTransitions(int seed)
        {
            BoxingSceneTests.SeedOpponent(game, seed); Opponent.Reset(1.65f); Opponent.Scripted = true;
            Frame("start");
            foreach (var from in Guards)
            foreach (var to in Guards)
            foreach (float side in new[] { -1f, 1f })
            { Hold(from, side, .6f); Hold(to, -side, .6f); }
            foreach (var attack in new[] { BoxingAttack.HeadStraight, BoxingAttack.BodyStraight, BoxingAttack.HeadHook, BoxingAttack.BodyHook })
            foreach (bool left in new[] { true, false })
            foreach (var guard in Guards)
            {
                Hold(guard, left ? -1 : 1, .5f);
                int done = Opponent.CompletedAttacks; Opponent.ScriptedStrike(attack, left, 1);
                for (int i = 0; i < 180 && Opponent.CompletedAttacks == done; i++) Frame(attack + (left ? " L" : " R") + " from " + guard);
            }
            Debug.Log($"BOXING_GLOVE_ROTATION max {maxStep:F1} deg/frame at 90 Hz ({maxStepAt})");
            // The forearm itself swings up to ~15 deg per frame in the fastest strikes; the eased twist adds at most 4.
            Assert.That(maxStep, Is.LessThan(25), "Largest glove rotation in one frame: " + maxStepAt);
        }

        [Test] public void GuardingKnucklesFaceThePlayer()
        {
            foreach (var guard in Guards)
            foreach (float side in new[] { -1f, 1f })
            {
                Hold(guard, side, 1);
                Assert.That(Opponent.GuardWeight, Is.GreaterThan(.95f), guard.ToString());
                foreach (var hand in new[] { Avatar.leftHand, Avatar.rightHand })
                {
                    float angle = KnuckleAngle(hand, PlayerHead - hand.position);
                    Assert.That(angle, Is.LessThanOrEqualTo(60), $"{guard} cover {side} {hand.name}: {angle:F0} deg");
                }
            }
        }

        private float ElbowOut(Transform fore) => Mathf.Abs(fore.position.x - Opponent.Head.x);

        [Test] public void HookGuardsCoverOneSideCloseToTheBodyAndKeepTheOtherGloveAtTheChin()
        {
            Hold(BoxingGuard.HeadStraight, 1, 1);
            float normalLeft = ElbowOut(Avatar.leftFore), normalRight = ElbowOut(Avatar.rightFore);
            var log = new StringBuilder($"high guard elbows L {normalLeft:F3} R {normalRight:F3}");
            foreach (var guard in new[] { BoxingGuard.HeadHook, BoxingGuard.BodyHook })
            foreach (float side in new[] { -1f, 1f })
            {
                Hold(guard, side, 1);
                Vector3 chin = BoxingOpponent.GuardOffset(BoxingGuard.HeadStraight, -side, side);
                Vector3 other = (side < 0 ? Opponent.Right : Opponent.Left) - Opponent.Head;
                Assert.That(Vector3.Distance(other, chin), Is.LessThan(.03f), $"{guard} {side}: the other glove stays at the chin");
                var cover = side < 0 ? Avatar.leftFore : Avatar.rightFore; var free = side < 0 ? Avatar.rightFore : Avatar.leftFore;
                float coverOut = ElbowOut(cover), freeOut = ElbowOut(free);
                float shoulder = Mathf.Abs((side < 0 ? Avatar.leftUpper : Avatar.rightUpper).position.x - Opponent.Head.x);
                log.Append($"; {guard} {side}: cover elbow {coverOut:F3} (y {cover.position.y - Opponent.Head.y:F3} z {cover.position.z - Opponent.Head.z:F3}, shoulder {shoulder:F3}) other {freeOut:F3}");
                // The covering glove moves out to the temple / the elbow onto the flank, but the elbow stays within a few cm
                // of the shoulder line (the former guards pushed both elbows 13 cm and more beyond it).
                Assert.That(coverOut, Is.LessThan(shoulder + .05f), $"{guard} {side}: the covering elbow only a few cm outward");
                Assert.That(freeOut, Is.LessThan((side < 0 ? normalRight : normalLeft) + .03f));
            }
            Debug.Log("BOXING_GUARD_ELBOWS " + log);
        }

        [Test] public void NoGuardLeavesBothElbowsStraightOut()
        {
            foreach (var guard in Guards)
            foreach (float side in new[] { -1f, 1f })
            {
                Hold(guard, side, 1);
                // Straight out: the elbow at or above shoulder height and beyond the shoulder line to the side.
                int raised = 0;
                foreach (var (upper, fore) in new[] { (Avatar.leftUpper, Avatar.leftFore), (Avatar.rightUpper, Avatar.rightFore) })
                    if (fore.position.y > upper.position.y - .03f && Mathf.Abs(fore.position.x - Opponent.Head.x) > Mathf.Abs(upper.position.x - Opponent.Head.x) + .05f) raised++;
                Assert.That(raised, Is.Zero, $"{guard} {side}: no elbow straight out to the side");
            }
        }

        // A hook through the game from the covered side meets the guard; the same hook from the other side lands.
        [TestCase(BoxingGuard.HeadHook)] [TestCase(BoxingGuard.BodyHook)]
        public void AHookToTheCoveredSideIsBlockedAndTheOtherSideIsOpen(BoxingGuard guard)
        {
            var resolve = typeof(BoxingGame).GetMethod("ResolvePlayer", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var save = typeof(BoxingGame).GetMethod("SaveHistory", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            foreach (float side in new[] { -1f, 1f })
            foreach (bool covered in new[] { true, false })
            {
                game.StartRound(); game.Round.Tick(3, false); Opponent.Scripted = true;
                Hold(guard, side, 1);
                save.Invoke(game, new object[] { BoxingSceneTests.Pose() });
                // A horizontal hook arriving from `from` side at the temple / the lower ribs.
                float from = covered ? side : -side;
                // Offsets at the opponent's size (the size probe scales its layout).
                Vector3 target = Opponent.Head + (guard == BoxingGuard.HeadHook ? new Vector3(0, .02f, -.02f) : new Vector3(0, -.5f, -.06f)) * Opponent.Size;
                Vector3 start = target + Vector3.right * from * .6f;
                int blocks = game.Round.EnemyBlocks, hits = game.Round.Hits;
                resolve.Invoke(game, new object[] { target, start, true, ImpactZone.LeftGlove, false, 0f, .1f });
                string label = $"{guard} cover {side}, hook from {from}";
                if (covered) { Assert.That(game.Round.EnemyBlocks - blocks, Is.EqualTo(1), label); Assert.That(game.Round.Hits, Is.EqualTo(hits), label); }
                else Assert.That(game.Round.Hits - hits, Is.EqualTo(1), label);
            }
            Assert.That(game.feedback.Sends, Is.Zero);
        }
    }
}
