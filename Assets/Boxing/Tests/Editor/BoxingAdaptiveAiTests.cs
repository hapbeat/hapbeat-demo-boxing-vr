using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Hapbeat.Boxing.Tests
{
    public sealed class BoxingAdaptiveAiTests
    {
        private static readonly Vector3 PlayerHead = Vector3.up * 1.65f;
        private BoxingTuning tuning;
        [SetUp] public void Setup() => tuning = ScriptableObject.CreateInstance<BoxingTuning>();
        [TearDown] public void Cleanup() => Object.DestroyImmediate(tuning);
        private static void Run(BoxingOpponent enemy, float seconds)
        {
            for (int i = 0; i < Mathf.RoundToInt(seconds * 100); i++) enemy.Tick(.01f, PlayerHead, true);
        }
        private static bool Covers(BoxingGuard guard, BoxingHitZone zone) =>
            zone == BoxingHitZone.Body ? guard == BoxingGuard.BodyStraight || guard == BoxingGuard.BodyHook :
            zone == BoxingHitZone.HeadCenter ? guard == BoxingGuard.HeadStraight : guard == BoxingGuard.HeadHook;

        [Test] public void HitZoneFollowsHeightAndLateralPunchDirection()
        {
            Assert.That(BoxingOpponent.Zone(false, new Vector3(3, 0, 1)), Is.EqualTo(BoxingHitZone.Body));
            Assert.That(BoxingOpponent.Zone(true, new Vector3(.5f, 0, 3)), Is.EqualTo(BoxingHitZone.HeadCenter));
            Assert.That(BoxingOpponent.Zone(true, new Vector3(3, 0, 1)), Is.EqualTo(BoxingHitZone.HeadLeft));
            Assert.That(BoxingOpponent.Zone(true, new Vector3(-3, 0, 1)), Is.EqualTo(BoxingHitZone.HeadRight));
        }
        [TestCase(BoxingHitZone.HeadLeft)] [TestCase(BoxingHitZone.HeadCenter)] [TestCase(BoxingHitZone.Body)]
        public void RepeatedHitsOnOneZoneMakeLaterGuardsCoverIt(BoxingHitZone zone)
        {
            int covered = 0, total = 0;
            for (int trial = 0; trial < 30; trial++)
            {
                var enemy = new BoxingOpponent(tuning, 1000 + trial);
                Run(enemy, 2);
                for (int i = 0; i < 3; i++) { enemy.React(.6f, zone); Run(enemy, .3f); }
                bool was = enemy.Guarding; int seen = 0;
                for (int i = 0; i < 500 && seen < 3; i++)
                {
                    enemy.Tick(.01f, PlayerHead, true);
                    if (enemy.Guarding && !was) { seen++; total++; if (Covers(enemy.Guard, zone)) covered++; }
                    was = enemy.Guarding;
                }
            }
            TestContext.WriteLine($"{zone}: {covered}/{total}");
            Assert.That(total, Is.GreaterThan(45));
            Assert.That(covered / (float)total, Is.GreaterThanOrEqualTo(.75f), $"{covered}/{total}");
        }
        [Test] public void ThreeHitsWithinTwoSecondsBringACounterWithinOneSecond()
        {
            for (int trial = 0; trial < 20; trial++)
            {
                var enemy = new BoxingOpponent(tuning, 2000 + trial);
                Run(enemy, 2);
                for (int i = 0; i < 3; i++) { if (i > 0) Run(enemy, .6f); enemy.React(.5f, BoxingHitZone.HeadCenter); }
                bool countered = false;
                for (int i = 0; i < 100 && !countered; i++) { enemy.Tick(.01f, PlayerHead, true); countered = enemy.Counter && enemy.Telegraphing; }
                Assert.That(countered, Is.True, "trial " + trial);
                Assert.That(enemy.State, Is.EqualTo(BoxingOpponentState.Counter));
                // A hit during the committed strike lets that strike continue.
                for (int i = 0; i < 100 && !enemy.Striking; i++) enemy.Tick(.01f, PlayerHead, true);
                Assert.That(enemy.Striking, Is.True);
                int id = enemy.AttackId; enemy.React(.5f, BoxingHitZone.Body);
                Assert.That(enemy.Striking, Is.True); Assert.That(enemy.AttackId, Is.EqualTo(id));
            }
        }
        [Test] public void MotionStaysSmoothOutsideStrikesAndFootworkStaysSlow()
        {
            var enemy = new BoxingOpponent(tuning, 77); const float dt = 1f / 90;
            var points = new Vector3[3, 3]; var striking = new bool[3]; Vector3 root = enemy.Root;
            float worst = 0, fastestRoot = 0;
            for (int frame = 0; frame < 90 * 90; frame++)
            {
                enemy.Tick(dt, PlayerHead, true);
                int k = frame % 3;
                points[k, 0] = enemy.Left; points[k, 1] = enemy.Right; points[k, 2] = enemy.Head; striking[k] = enemy.Striking;
                fastestRoot = Mathf.Max(fastestRoot, (enemy.Root - root).magnitude / dt); root = enemy.Root;
                Assert.That(Mathf.Abs(enemy.Root.x), Is.LessThanOrEqualTo(.161f)); Assert.That(enemy.Root.z, Is.InRange(.97f, 1.19f));
                if (frame < 2 || striking[0] || striking[1] || striking[2]) continue;
                int a = (frame + 1) % 3, b = (frame + 2) % 3;
                for (int p = 0; p < 3; p++) worst = Mathf.Max(worst, (points[a, p] - 2 * points[b, p] + points[k, p]).magnitude / (dt * dt));
            }
            TestContext.WriteLine($"peak acceleration {worst:0.0} m/s^2, root speed {fastestRoot:0.00} m/s");
            Assert.That(enemy.CompletedAttacks, Is.GreaterThan(10));
            Assert.That(worst, Is.LessThanOrEqualTo(80), "Glove/head acceleration outside strikes (m/s^2).");
            Assert.That(fastestRoot, Is.LessThanOrEqualTo(1), "Footwork speed (m/s).");
        }
        private List<BoxingAction> Sequence(BoxingOpponent enemy)
        {
            var actions = new List<BoxingAction>(); int count = enemy.Actions;
            for (int i = 0; i < 9000; i++)
            {
                enemy.Tick(.01f, PlayerHead, true);
                if (enemy.Actions != count) { count = enemy.Actions; actions.Add(enemy.Action); }
            }
            return actions;
        }
        [Test] public void ActionsAreVariedAperiodicAndChangeEveryReset()
        {
            var enemy = new BoxingOpponent(tuning, 5);
            var first = Sequence(enemy);
            Assert.That(new HashSet<BoxingAction>(first).Count, Is.GreaterThanOrEqualTo(6));
            for (int period = 1; period <= 4; period++)
            {
                bool repeats = true;
                for (int i = 0; i + period < first.Count && repeats; i++) repeats = first[i] == first[i + period];
                Assert.That(repeats, Is.False, "Repeats with period " + period);
            }
            Assert.That(Sequence(new BoxingOpponent(tuning, 5)), Is.EqualTo(first), "An injected seed is deterministic.");
            enemy.Reset(1.65f);
            Assert.That(Sequence(enemy), Is.Not.EqualTo(first), "Every Reset draws a new sequence.");
            var unseeded = new BoxingOpponent(tuning); var a = Sequence(unseeded); unseeded.Reset(1.65f);
            Assert.That(Sequence(unseeded), Is.Not.EqualTo(a));
        }
    }

    public sealed class BoxingOpponentReadTests
    {
        private BoxingGame game;
        private BoxingTuning original;
        [SetUp] public void Setup()
        {
            EditorSceneManager.OpenScene(Editor.BoxingProject.ScenePath);
            game = Object.FindAnyObjectByType<BoxingGame>();
            original = game.tuning; game.tuning = Object.Instantiate(original);
            game.feedback.forceSilent = true; game.feedback.sdkRoot.SetActive(false);
            game.Initialize(); game.menu.Close();
        }
        [TearDown] public void Cleanup()
        {
            var temp = game.tuning; game.tuning = original; Object.DestroyImmediate(temp);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
        }
        // A straight at 3 m/s from in front of the player's face; the game's sweeps decide hit, block or miss.
        private float DefenceRate(float skill)
        {
            game.tuning.opponentSkill = skill;
            var setter = typeof(BoxingGame).GetProperty("Opponent").GetSetMethod(true);
            int defended = 0; const int trials = 40;
            for (int trial = 0; trial < trials; trial++)
            {
                setter.Invoke(game, new object[] { new BoxingOpponent(game.tuning, 500 + trial) });
                game.Round.Start(90, 10000); game.Round.Tick(3, false); game.ResetHistory();
                var pose = BoxingSceneTests.Pose(); pose.left = new Vector3(0, 1.65f, -.1f);
                for (int i = 0; i < 90 || (i < 900 && (game.Opponent.State != BoxingOpponentState.Neutral || game.Opponent.Guarding)); i++)
                    game.Simulate(1f / 90, pose);
                int hits = game.Round.Hits, blocks = game.Round.EnemyBlocks;
                Vector3 direction = (game.Opponent.Head - pose.left).normalized;
                for (int i = 0; i < 45 && game.Round.Hits == hits && game.Round.EnemyBlocks == blocks; i++)
                {
                    pose.left += direction * (3f / 90); game.Simulate(1f / 90, pose);
                }
                if (game.Round.Hits == hits) defended++;
            }
            return defended / (float)trials;
        }
        [Test] public void SkilledOpponentReadsStraightPunchesFarMoreOftenThanAnUnskilledOne()
        {
            float high = DefenceRate(1), low = DefenceRate(0);
            TestContext.WriteLine($"defended: skill 1 = {high:0.00}, skill 0 = {low:0.00}");
            Assert.That(high, Is.GreaterThanOrEqualTo(.65f), $"skill 1 = {high:0.00}, skill 0 = {low:0.00}");
            Assert.That(low, Is.LessThan(high - .25f), $"skill 1 = {high:0.00}, skill 0 = {low:0.00}");
            Assert.That(game.feedback.Sends, Is.Zero);
        }
    }
}
