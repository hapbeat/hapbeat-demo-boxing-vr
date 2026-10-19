using System.Linq;
using Hapbeat.Boxing.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Hapbeat.Boxing.Tests
{
    public sealed class BoxingRulesTests
    {
        [TestCase("ProjectSettings/TagManager.asset", "layers")]
        [TestCase("Assets/XRI/Settings/Resources/InteractionLayerSettings.asset", "m_LayerNames")]
        public void SerializedLayerTablesLoadWithAll32Slots(string path, string property)
        {
            if (path.StartsWith("Assets/")) AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var assets = AssetDatabase.LoadAllAssetsAtPath(path);
            Assert.That(assets, Is.Not.Empty, "Unity must parse the actual serialized settings, not use a fallback.");
            var table = new SerializedObject(assets[0]).FindProperty(property);
            Assert.That(table, Is.Not.Null);
            Assert.That(table.arraySize, Is.EqualTo(32));
            Assert.That(table.GetArrayElementAtIndex(0).stringValue, Is.EqualTo("Default"));
            Assert.That(table.GetArrayElementAtIndex(31).stringValue, Is.Empty);
        }
        private BoxingTuning tuning;
        [Test] public void SimulatorAssetsResolveWithoutMissingReferences() => BoxingSimulator.ValidateAssets();
        [Test] public void SimulatorAssetsAreNotSceneOrResourcesDependencies()
        {
            foreach (var path in AssetDatabase.GetDependencies("Assets/Boxing/Scenes/Boxing.unity", true))
                Assert.That(path, Does.Not.StartWith("Assets/Boxing/Editor/Simulator/"));
            var config = AssetDatabase.LoadAllAssetsAtPath("Assets/XRI/Settings/Resources/XRDeviceSimulatorSettings.asset")[0];
            var properties = new SerializedObject(config);
            Assert.That(properties.FindProperty("m_AutomaticallyInstantiateSimulatorPrefab").boolValue, Is.False);
            Assert.That(properties.FindProperty("m_SimulatorPrefab").objectReferenceValue, Is.Null);
        }
        [SetUp] public void Setup() => tuning = ScriptableObject.CreateInstance<BoxingTuning>();
        [TearDown] public void Cleanup() => Object.DestroyImmediate(tuning);
        [Test] public void RelativeVelocityIsDifferenceNotSum()
        {
            Assert.That(BoxingCollision.RelativeSpeed(Vector3.zero, Vector3.right, Vector3.zero, Vector3.right, 1), Is.Zero);
            Assert.That(BoxingCollision.RelativeSpeed(Vector3.zero, Vector3.right, Vector3.zero, Vector3.left, 1), Is.EqualTo(2));
        }
        [Test] public void FastCrossingBetweenFramesStillHits()
        {
            Assert.That(BoxingCollision.Sweep(Vector3.back, Vector3.forward, 0.1f, Vector3.zero, Vector3.zero, 0.1f, out float time), Is.True);
            Assert.That(time, Is.EqualTo(0.4f).Within(0.00001));
        }
        [Test] public void ParallelMotionDoesNotCreateAnImpact()
        {
            Assert.That(BoxingCollision.Sweep(Vector3.zero, Vector3.forward, 0.1f, Vector3.right, Vector3.right + Vector3.forward, 0.1f, out _), Is.False);
        }
        [Test] public void GainIsMonotonicBoundedAndRejectsInvalidSpeed()
        {
            Assert.That(tuning.Gain(0), Is.Zero); Assert.That(tuning.Gain(float.NaN), Is.Zero);
            float last = 0;
            for (float speed = 0; speed <= 20; speed += 0.1f)
            { float gain = tuning.Gain(speed); Assert.That(gain, Is.GreaterThanOrEqualTo(last)); Assert.That(gain, Is.LessThanOrEqualTo(tuning.maximumGain)); last = gain; }
            Assert.That(tuning.Gain(5), Is.GreaterThan(tuning.Gain(0.6f) * 2));
        }
        [Test] public void WeakHardThresholdAndContinuousModeAreIndependentOfGain()
        {
            tuning.hardHitSpeed = 2.5f;
            Assert.That(tuning.IsHard(2.49f), Is.False); Assert.That(tuning.IsHard(2.5f), Is.True);
            float gain = tuning.Gain(4); tuning.impactMode = ImpactMode.Continuous;
            Assert.That(tuning.IsHard(4), Is.False); Assert.That(tuning.Gain(4), Is.EqualTo(gain));
        }
        [Test] public void CountdownPauseAndRoundHaveExactLifecycle()
        {
            var round = new BoxingRound(); round.Start(90); round.Tick(2, false);
            Assert.That(round.Phase, Is.EqualTo(BoxingPhase.Countdown)); round.Tick(30, true);
            Assert.That(round.Countdown, Is.EqualTo(1)); round.Tick(1, false);
            Assert.That(round.Phase, Is.EqualTo(BoxingPhase.Fighting)); Assert.That(round.TimeLeft, Is.EqualTo(90));
            round.Tick(90, false); Assert.That(round.Phase, Is.EqualTo(BoxingPhase.Results));
        }
        [Test] public void EnemyUsesBothHandsAndMovesWithinOneSmallStep()
        {
            var opponent = new BoxingOpponent(tuning, 12); int left = 0, right = 0, id = 0;
            for (int i = 0; i < 9000; i++)
            {
                opponent.Tick(0.01f, Vector3.up * 1.65f, Quaternion.identity, true);
                Assert.That(Mathf.Abs(opponent.Root.x), Is.LessThanOrEqualTo(0.161f));
                Assert.That(opponent.Root.z, Is.InRange(0.97f, 1.19f));
                if (opponent.AttackId != id) { id = opponent.AttackId; if (opponent.AttackLeft) left++; else right++; }
            }
            Assert.That(left, Is.GreaterThan(5)); Assert.That(right, Is.GreaterThan(5));
        }
        [Test] public void HealthDamageGuardKnockoutAndRestart()
        {
            var round = new BoxingRound(); round.Start(90); round.Tick(3, false);
            round.Report(new BoxingImpact(ImpactZone.LeftGlove, 1, false, Vector3.zero, tuning, ImpactSurface.Glove));
            Assert.That(round.PlayerHealth, Is.EqualTo(100)); Assert.That(round.Blocks, Is.EqualTo(1));
            round.Report(new BoxingImpact(ImpactZone.Head, 1, false, Vector3.zero, tuning, ImpactSurface.Body));
            Assert.That(round.PlayerHealth, Is.EqualTo(90)); Assert.That(round.EnemyHealth, Is.EqualTo(100));
            for (int i = 0; i < 5; i++) round.Report(new BoxingImpact(ImpactZone.LeftGlove, 4, true, Vector3.zero, tuning, ImpactSurface.Body));
            Assert.That(round.EnemyHealth, Is.Zero); Assert.That(round.Phase, Is.EqualTo(BoxingPhase.Results));
            round.Report(new BoxingImpact(ImpactZone.Head, 4, false, Vector3.zero, tuning, ImpactSurface.Body));
            Assert.That(round.PlayerHealth, Is.EqualTo(90), "No damage after KO.");
            round.Start(90); Assert.That(round.PlayerHealth, Is.EqualTo(100)); Assert.That(round.EnemyHealth, Is.EqualTo(100));
            round.Tick(3, false);
            for (int i = 0; i < 5; i++) round.Report(new BoxingImpact(ImpactZone.Head, 4, false, Vector3.zero, tuning, ImpactSurface.Body));
            Assert.That(round.PlayerHealth, Is.Zero); Assert.That(round.Phase, Is.EqualTo(BoxingPhase.Results));
        }
        [TestCase(true)] [TestCase(false)]
        public void GloveSurfaceNeverDamagesEitherBoxer(bool attack)
        {
            var round = new BoxingRound(); round.Start(90); round.Tick(3, false);
            round.Report(new BoxingImpact(ImpactZone.LeftGlove, 5, attack, Vector3.zero, tuning, ImpactSurface.Glove));
            Assert.That(round.PlayerHealth, Is.EqualTo(100)); Assert.That(round.EnemyHealth, Is.EqualTo(100));
            Assert.That(attack ? round.EnemyBlocks : round.Blocks, Is.EqualTo(1)); Assert.That(round.Hits, Is.Zero);
        }
        [Test] public void OpponentSometimesRaisesAndLowersGuardWithoutReadingPlayerPunches()
        {
            var enemy = new BoxingOpponent(tuning, 13); int guarded = 0, transitions = 0; bool old = false;
            for (int i = 0; i < 6000; i++)
            {
                enemy.Tick(0.01f, Vector3.up * 1.65f, Quaternion.identity, true);
                if (enemy.Guarding && !old) transitions++;
                if (enemy.GuardWeight > 0.9f)
                {
                    guarded++; Assert.That(enemy.Striking, Is.False);
                    Assert.That(Mathf.Abs(enemy.Left.x - enemy.Head.x), Is.LessThan(enemy.HookGuard ? .28f : .13f));
                    Assert.That(enemy.Left.y, Is.GreaterThan((enemy.BodyGuard ? enemy.Body.y : enemy.Head.y) - 0.06f));
                }
                old = enemy.Guarding;
            }
            Assert.That(guarded, Is.GreaterThan(100)); Assert.That(transitions, Is.GreaterThan(2)); Assert.That(enemy.CompletedAttacks, Is.GreaterThan(5));
        }
    }

    public sealed class BoxingSceneTests
    {
        private BoxingGame game;
        private BoxingTuning originalTuning;
        [SetUp] public void Setup()
        {
            EditorSceneManager.OpenScene(BoxingProject.ScenePath);
            game = Object.FindAnyObjectByType<BoxingGame>();
            originalTuning = game.tuning; game.tuning = Object.Instantiate(originalTuning);
            game.feedback.forceSilent = true; game.feedback.sdkRoot.SetActive(false);
            game.Initialize(); SeedOpponent(game, 21); game.Round.Start(90, 10000); // Long collision tests must not stop early at KO.
        }
        // The opponent draws a fresh seed per round; tests pin one so the action sequence is reproducible.
        public static void SeedOpponent(BoxingGame target, int seed) =>
            typeof(BoxingGame).GetProperty("Opponent").GetSetMethod(true).Invoke(target, new object[] { new BoxingOpponent(target.tuning, seed) });
        [TearDown] public void Cleanup()
        {
            var temp = game.tuning; game.tuning = originalTuning; Object.DestroyImmediate(temp);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
        }
        // The most forward point of the opponent's rendered hit shape (head or torso) on its centre line at height y.
        public static Vector3 OpponentFront(BoxingGame game, bool head, float y)
        {
            var heads = new BoxingHitSphere[BoxingOpponentAvatar.HeadSpheres]; var torso = new BoxingHitSphere[BoxingOpponentAvatar.TorsoSpheres];
            game.presentation.enemyAvatar.GetHitShapes(heads, torso);
            float x = head ? game.Opponent.Head.x : game.Opponent.Body.x, z = float.PositiveInfinity;
            foreach (var s in head ? heads : torso)
            {
                float dx = s.center.x - x, dy = s.center.y - y, h = s.radius * s.radius - dx * dx - dy * dy;
                if (h > 0) z = Mathf.Min(z, s.center.z - Mathf.Sqrt(h));
            }
            return new Vector3(x, y, z);
        }
        public static BoxerPose Pose(bool guard = false) => new BoxerPose {
            valid = true, head = new Vector3(0, 1.65f, 0), left = new Vector3(guard ? -0.12f : -0.45f, guard ? 1.65f : 1.1f, 0.3f),
            right = new Vector3(guard ? 0.12f : 0.45f, guard ? 1.65f : 1.1f, 0.3f),
            headRotation = Quaternion.identity, leftRotation = Quaternion.identity, rightRotation = Quaternion.identity, leftClosed = true, rightClosed = true };
        private void Advance(float seconds, BoxerPose pose) { for (int i = 0; i < seconds * 90; i++) game.Simulate(1f / 90, pose); }
        private void FocusEvent(string method, bool value) => typeof(BoxingGame).GetMethod(method, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(game, new object[] { value });
        [Test] public void FocusedHeadsetContinuesWhenEditorWindowLosesFocus()
        {
            FocusEvent("DisplayFocus", true);
            FocusEvent("FocusChanged", false);
            game.StartRound(); Advance(4, Pose());
            Assert.That(game.Paused, Is.False, game.PauseReason);
            Assert.That(game.Round.Phase, Is.EqualTo(BoxingPhase.Fighting));
        }
        [Test] public void ActualHeadsetFocusLossStillStopsAndRequiresResume()
        {
            FocusEvent("DisplayFocus", true);
            game.StartRound(); Advance(4, Pose());
            FocusEvent("DisplayFocus", false);
            float time = game.Round.TimeLeft; Advance(1, Pose());
            Assert.That(game.Paused, Is.True); Assert.That(game.Round.TimeLeft, Is.EqualTo(time));
            FocusEvent("DisplayFocus", true);
            Advance(1, Pose()); Assert.That(game.menu.IsOpen, Is.True);
            game.menu.Close(); Advance(1, Pose()); Assert.That(game.Paused, Is.False);
        }
        [Test] public void ApplicationPauseStillStopsFocusedHeadset()
        {
            FocusEvent("DisplayFocus", true); game.StartRound(); Advance(4, Pose());
            FocusEvent("OnApplicationPause", true); game.menu.Close(); Advance(1, Pose());
            Assert.That(game.Paused, Is.True); Assert.That(game.PauseReason, Is.EqualTo("APPLICATION PAUSED"));
        }
        [Test] public void WithoutHeadsetWindowFocusStillPauses()
        {
            FocusEvent("FocusChanged", false); game.StartRound(); Advance(1, Pose());
            Assert.That(game.Paused, Is.True); Assert.That(game.PauseReason, Is.EqualTo("GAME WINDOW NOT FOCUSED"));
        }
        [Test] public void UnguardedHeadGetsHitAcrossACompleteRoundWithoutLiveSending()
        {
            Advance(95, Pose());
            Assert.That(game.Round.Phase, Is.EqualTo(BoxingPhase.Results));
            Assert.That(game.Round.Taken, Is.GreaterThan(10)); Assert.That(game.feedback.Sends, Is.Zero);
        }
        [Test] public void GlovesInFrontOfHeadBlockAndNoAttackHitsTwice()
        {
            int bodyHits = 0;
            game.feedback.Reported += impact => { if (!impact.attack && impact.zone == ImpactZone.Body) bodyHits++; };
            Advance(95, Pose(true));
            Assert.That(game.Round.Blocks, Is.GreaterThan(0));
            Assert.That(game.Round.Blocks + game.Round.Taken, Is.LessThanOrEqualTo(game.Opponent.AttackId));
            Assert.That(bodyHits, Is.GreaterThan(0), "A head-only guard must leave the torso exposed.");
        }
        [Test] public void GuardContactStopsEnemyStrikeAndRetractsInsteadOfPassingThrough()
        {
            var pose = Pose(true); int takenBefore = 0;
            // Earlier body attacks may land on the open torso; the blocked strike itself must not.
            for (int i = 0; i < 900 && game.Round.Blocks == 0; i++) { takenBefore = game.Round.Taken; game.Simulate(1f / 90, pose); }
            Assert.That(game.Round.Blocks, Is.EqualTo(1));
            Assert.That(game.Opponent.Striking, Is.False, "A counted block must stop the visible strike.");
            bool left = game.Opponent.AttackLeft;
            Vector3 contact = left ? game.Opponent.Left : game.Opponent.Right;
            game.Simulate(1f / 90, pose);
            Assert.That((left ? game.Opponent.Left : game.Opponent.Right).z, Is.GreaterThanOrEqualTo(contact.z));
            Assert.That(game.Round.Taken, Is.EqualTo(takenBefore));
        }
        [Test] public void HealthBarsReplaceCueAndReflectBothHealthValues()
        {
            game.Round.Start(90); game.Round.Tick(3, false);
            game.Round.Report(new BoxingImpact(ImpactZone.Head, 1, false, Vector3.zero, game.tuning, ImpactSurface.Body));
            game.Round.Report(new BoxingImpact(ImpactZone.LeftGlove, 4, true, Vector3.zero, game.tuning, ImpactSurface.Body));
            game.presentation.RenderHealth(game.Round);
            Assert.That(game.presentation.cueText.enabled, Is.False);
            Assert.That(game.presentation.PlayerHealthBar.rectTransform.anchorMax.x, Is.EqualTo(0.9f).Within(0.001f));
            Assert.That(game.presentation.EnemyHealthBar.rectTransform.anchorMax.x, Is.EqualTo(0.8f).Within(0.001f));
        }
        [Test] public void SceneAndNewInputUseCorrectedControllerPitch()
        {
            Assert.That(game.input.controllerRotation, Is.EqualTo(new Vector3(75, 0, 0)));
        }
        [Test] public void ImpactFeedbackMustNotCoverTheViewWithARectangularPanel()
        {
            Assert.That(game.input.headCamera.transform.Find("Impact feedback/Damage tint"), Is.Null);
            var ring = game.presentation.hitBurst.GetComponent<LineRenderer>();
            Assert.That(ring, Is.Not.Null); Assert.That(ring.useWorldSpace, Is.False);
            Assert.That(ring.sharedMaterial.GetFloat("_Surface"), Is.EqualTo(1));
        }
        [Test] public void CompactGlovesMatchTheSmallerCollisionEnvelope()
        {
            var size = BoxingContent.GloveSize(game.presentation.leftGlove);
            Assert.That(size.x, Is.EqualTo(.15f).Within(.001f)); Assert.That(size.z, Is.InRange(0.25f, 0.27f));
            Assert.That(game.tuning.gloveRadius, Is.EqualTo(0.07f));
        }
        [TestCase(false)] [TestCase(true)] public void GuardInterceptionAndBodyHitSelectDifferentOutcomes(bool bodyGuard)
        {
            game.Round.Tick(3, false);
            // The head case uses the head hook guard (its glove at the temple), the body case the low body straight guard. A high
            // guard's forearms legitimately cover the chest; a body hook guard keeps the other glove before the face.
            var wanted = bodyGuard ? BoxingGuard.BodyStraight : BoxingGuard.HeadHook;
            for (int i = 0; i < 12000 && (game.Opponent.GuardWeight < 0.99f || !game.Opponent.Guarding || game.Opponent.Guard != wanted); i++) game.Opponent.Tick(0.01f, Pose().head, Quaternion.identity, true);
            Assert.That(game.Opponent.Guarding, Is.True);
            typeof(BoxingGame).GetMethod("SaveHistory", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(game, new object[] { Pose() });
            var resolve = typeof(BoxingGame).GetMethod("ResolvePlayer", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            // Head: a hook into the covering glove at the temple (its forearm stands before the face below it); body: a straight.
            Vector3 guardGlove = bodyGuard || game.Opponent.GuardSide < 0 ? game.Opponent.Left : game.Opponent.Right;
            Vector3 from = guardGlove + (bodyGuard ? Vector3.back : Vector3.right * game.Opponent.GuardSide) * 0.5f, to = guardGlove;
            var args = new object[] { to, from, true, ImpactZone.LeftGlove, false, 0f, 0.1f };
            resolve.Invoke(game, args);
            Assert.That(game.Round.EnemyBlocks, Is.EqualTo(1)); Assert.That(game.Round.Hits, Is.Zero);
            Assert.That(game.feedback.LastImpact.surface, Is.EqualTo(ImpactSurface.Glove));
            Assert.That(BoxingFeedback.CueName(game.feedback.LastImpact), Does.StartWith("punch_glove_"));
            Assert.That(game.Round.EnemyHealth, Is.EqualTo(10000));
            resolve.Invoke(game, args); Assert.That(game.Round.EnemyBlocks, Is.EqualTo(1), "Held overlap must not repeatedly fire.");
            // Open: the face over the low gloves, or the belly below the head hook guard's elbows.
            Vector3 uncovered=bodyGuard ? game.Opponent.Head : game.Opponent.Body + Vector3.down * .12f;
            resolve.Invoke(game, new object[] { uncovered, uncovered + Vector3.back * 0.6f, true, ImpactZone.RightGlove, false, 0f, 0.1f });
            Assert.That(game.Round.Hits, Is.EqualTo(1)); Assert.That(game.Round.EnemyHealth, Is.LessThan(10000));
            Assert.That(game.feedback.LastImpact.surface, Is.EqualTo(ImpactSurface.Body));
            Assert.That(BoxingFeedback.CueName(game.feedback.LastImpact), Does.StartWith("punch_body_"));
        }
        [Test] public void ExtendedWristTapCannotDealFullPunchDamage()
        {
            game.Round.Tick(3, false);
            typeof(BoxingGame).GetMethod("SaveHistory", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(game, new object[] { Pose() });
            var resolve = typeof(BoxingGame).GetMethod("ResolvePlayer", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Vector3 contact = OpponentFront(game, false, game.Opponent.Body.y) + Vector3.back * game.tuning.gloveRadius;
            resolve.Invoke(game, new object[] { contact + Vector3.forward * 0.01f, contact + Vector3.back * 0.04f, true, ImpactZone.LeftGlove, false, 0f, 0.01f });
            Assert.That(game.Round.Hits, Is.EqualTo(1));
            Assert.That(10000 - game.Round.EnemyHealth, Is.LessThanOrEqualTo(1), "A fast 5cm wrist tap is not a full punch.");
        }
        // A passive opponent: these check punch strength, not whether the opponent reads the punch.
        private void Passive() { game.tuning.telegraphSeconds = 100; game.tuning.opponentReadChanceLow = game.tuning.opponentReadChanceHigh = 0; }
        [Test] public void RealSimulationUsesHeadRelativeStrokeForBodyDamage()
        {
            game.Round.Tick(3, false); Passive();
            var pose = Pose(); pose.head.z = 0.4f; pose.left = new Vector3(0, 1.22f, 0.25f);
            Advance(0.4f, pose);
            int before = game.Round.Hits;
            for (int i = 1; i <= 30 && game.Round.Hits == before; i++)
            {
                pose.left.z = 0.25f + i * 0.025f; game.Simulate(0.01f, pose);
            }
            Assert.That(game.Round.Hits, Is.EqualTo(before + 1));
            Assert.That(game.feedback.LastImpact.attack, Is.True);
            Assert.That(game.feedback.LastImpact.damage, Is.GreaterThan(15));
            Assert.That(game.feedback.Sends, Is.Zero);
        }
        [Test] public void WalkingAnExtendedFistIntoOpponentIsOnlyATap()
        {
            game.Round.Tick(3, false); Passive();
            var pose = Pose(); pose.left = new Vector3(0, 1.22f, 0.5f);
            Advance(0.4f, pose);
            int before = game.Round.Hits;
            for (int i = 0; i < 30 && game.Round.Hits == before; i++)
            {
                pose.head.z += 0.015f; pose.left.z += 0.015f; pose.right.z += 0.015f;
                game.Simulate(0.01f, pose);
            }
            Assert.That(game.Round.Hits, Is.EqualTo(before + 1));
            Assert.That(game.feedback.LastImpact.damage, Is.LessThanOrEqualTo(1));
        }
        [TestCase(true)] [TestCase(false)]
        public void ContactPartSelectsItsOwnCueOnTheSameWristRouteForBothDirections(bool attack)
        {
            foreach (var zone in new[] { ImpactZone.LeftGlove, ImpactZone.RightGlove })
            foreach (float speed in new[] { 1f, 5f })
            {
                // Own punch: the opponent's glove / body. Received: the player's glove / forearm.
                var glove = attack ? new BoxingImpact(zone, speed, Vector3.zero, game.tuning, ImpactSurface.Glove, 1)
                    : new BoxingImpact(zone, speed, false, Vector3.zero, game.tuning, ImpactSurface.Glove);
                var other = attack ? new BoxingImpact(zone, speed, Vector3.zero, game.tuning, ImpactSurface.Body, 1)
                    : new BoxingImpact(zone, speed, false, Vector3.zero, game.tuning, ImpactSurface.Glove, ImpactPart.Forearm);
                var g = game.feedback.impactHaptics[BoxingFeedback.TriggerIndex(glove)].triggers[0].ResolveEntry();
                var o = game.feedback.impactHaptics[BoxingFeedback.TriggerIndex(other)].triggers[0].ResolveEntry();
                Assert.That(g.target, Is.EqualTo(zone == ImpactZone.LeftGlove ? "*/pos_l_wrist" : "*/pos_r_wrist")); Assert.That(o.target, Is.EqualTo(g.target));
                Assert.That(g.eventName, Is.EqualTo((zone == ImpactZone.LeftGlove ? "left_" : "right_") + BoxingFeedback.CueName(glove)));
                Assert.That(o.eventName, Is.EqualTo((zone == ImpactZone.LeftGlove ? "left_" : "right_") + BoxingFeedback.CueName(other)));
                Assert.That(g.eventName, Is.Not.EqualTo(o.eventName));
                // Every cue has its own sounds from the cue table, one volume per sound.
                foreach (var sounds in new[] { game.feedback.cueSounds[BoxingFeedback.CueIndex(glove)], game.feedback.cueSounds[BoxingFeedback.CueIndex(other)] })
                { Assert.That(sounds.clips, Is.Not.Empty.And.All.Not.Null); Assert.That(sounds.volumes.Length, Is.EqualTo(sounds.clips.Length)); }
                // Every clip of the cue has its own entry at this wrist: <entry>, <entry>_2, ...
                var clips = game.feedback.impactHaptics[BoxingFeedback.TriggerIndex(glove)].triggers;
                for (int k = 0; k < clips.Length; k++)
                {
                    var entry = clips[k].ResolveEntry();
                    Assert.That(entry.eventName, Is.EqualTo(BoxingHapticImport.EntryName(g.eventName, k))); Assert.That(entry.target, Is.EqualTo(g.target));
                }
            }
            Assert.That(game.feedback.Sends, Is.Zero);
        }
        [Test] public void CueWithSeveralSoundsUsesAllWithoutRepeatingAndASingleSoundCueKeepsIt()
        {
            Random.InitState(5);
            // hit_body_soft (a received head hit) has several sounds picked at random; guard_glove_soft one.
            var hit = new BoxingImpact(ImpactZone.Head, 1, false, Vector3.zero, game.tuning, ImpactSurface.Body);
            var guard = new BoxingImpact(ImpactZone.LeftGlove, 1, false, Vector3.zero, game.tuning, ImpactSurface.Glove);
            var several = game.feedback.cueSounds[BoxingFeedback.CueIndex(hit)];
            var single = game.feedback.cueSounds[BoxingFeedback.CueIndex(guard)];
            Assert.That(several.clips.Length, Is.GreaterThan(1)); Assert.That(several.pick, Is.EqualTo(CueSoundPick.Random));
            Assert.That(single.clips.Length, Is.EqualTo(1));
            var used = new System.Collections.Generic.HashSet<AudioClip>(); AudioClip previous = null;
            for (int i = 0; i < 200; i++)
            {
                game.feedback.Impact(hit);
                Assert.That(game.feedback.LastSound, Is.Not.Null.And.Not.SameAs(previous));
                previous = game.feedback.LastSound; used.Add(previous);
                game.feedback.Impact(guard); Assert.That(game.feedback.LastSound, Is.SameAs(single.clips[0]));
            }
            Assert.That(used, Is.EquivalentTo(several.clips));
            Assert.That(game.feedback.Sends, Is.Zero);
        }
        [Test] public void CueWithSeveralClipsUsesAllWithoutRepeatingAcrossBothWristsAndASingleClipCueKeepsIt()
        {
            Random.InitState(7);
            // guard_glove_soft (a slow opponent punch caught on the glove) has several clips picked at random;
            // punch_body_soft (a lighter own punch on the body) one.
            var left = new BoxingImpact(ImpactZone.LeftGlove, 1, false, Vector3.zero, game.tuning, ImpactSurface.Glove);
            var right = new BoxingImpact(ImpactZone.RightGlove, 1, false, Vector3.zero, game.tuning, ImpactSurface.Glove);
            var body = new BoxingImpact(ImpactZone.LeftGlove, 4, Vector3.zero, game.tuning, ImpactSurface.Body, game.tuning.punchHardStrength - .05f);
            Assert.That(BoxingFeedback.CueName(left), Is.EqualTo("guard_glove_soft")); Assert.That(BoxingFeedback.CueName(body), Is.EqualTo("punch_body_soft"));
            var several = game.feedback.impactHaptics[BoxingFeedback.TriggerIndex(left)];
            var single = game.feedback.impactHaptics[BoxingFeedback.TriggerIndex(body)];
            Assert.That(several.triggers.Length, Is.GreaterThan(1)); Assert.That(several.pick, Is.EqualTo(CueSoundPick.Random));
            Assert.That(single.triggers.Length, Is.EqualTo(1));
            AudioClip Played() => game.feedback.LastHaptic.ResolveEntry().streamClip;
            var used = new System.Collections.Generic.HashSet<AudioClip>(); AudioClip previous = null;
            for (int i = 0; i < 200; i++)
            {
                var impact = i % 3 == 0 ? right : left;
                game.feedback.Impact(impact);
                Assert.That(game.feedback.impactHaptics[BoxingFeedback.TriggerIndex(impact)].triggers, Does.Contain(game.feedback.LastHaptic));
                Assert.That(Played(), Is.Not.Null.And.Not.SameAs(previous));
                previous = Played(); used.Add(previous);
                game.feedback.Impact(body); Assert.That(game.feedback.LastHaptic, Is.SameAs(single.triggers[0]));
            }
            Assert.That(used, Is.EquivalentTo(several.triggers.Select(t => t.ResolveEntry().streamClip)));
            Assert.That(game.feedback.Sends, Is.Zero);
        }
        [Test] public void PickGoesInOrderForRoundRobinAndNeverRepeatsForRandom()
        {
            Assert.That(CuePick.Next(0, CueSoundPick.Random, -1), Is.EqualTo(-1));
            Assert.That(CuePick.Next(1, CueSoundPick.Random, 0), Is.EqualTo(0));
            int last = -1; var order = new int[5];
            for (int i = 0; i < order.Length; i++) order[i] = last = CuePick.Next(3, CueSoundPick.RoundRobin, last);
            Assert.That(order, Is.EqualTo(new[] { 0, 1, 2, 0, 1 }));
            Random.InitState(3); last = -1;
            for (int i = 0; i < 100; i++) { int next = CuePick.Next(2, CueSoundPick.Random, last); if (last >= 0) Assert.That(next, Is.Not.EqualTo(last)); last = next; }
        }
        [Test] public void RoundRobinSoundsGoInOrder()
        {
            var clips = new[] { AudioClip.Create("a", 10, 1, 48000, false), AudioClip.Create("b", 10, 1, 48000, false), AudioClip.Create("c", 10, 1, 48000, false) };
            try
            {
                var sounds = new CueSoundSet { clips = clips, volumes = new[] { 1f, 1f, 1f }, pick = CueSoundPick.RoundRobin };
                Assert.That(new[] { sounds.Next(), sounds.Next(), sounds.Next(), sounds.Next() }, Is.EqualTo(new[] { 0, 1, 2, 0 }));
                Assert.That(new CueSoundSet().Next(), Is.EqualTo(-1));
            }
            finally { foreach (var clip in clips) Object.DestroyImmediate(clip); }
        }
        [Test] public void StartAndEndRingOnceAndResultsRemainStable()
        {
            Advance(4, Pose()); Assert.That(game.feedback.Rings, Is.EqualTo(1));
            Advance(95, Pose()); Assert.That(game.Round.Phase, Is.EqualTo(BoxingPhase.Results));
            Assert.That(game.feedback.Rings, Is.EqualTo(2)); Advance(1, Pose()); Assert.That(game.feedback.Rings, Is.EqualTo(2));
            Assert.That(game.feedback.bellSource, Is.Not.SameAs(game.feedback.audioSource));
            Assert.That(game.feedback.bell.length, Is.GreaterThan(2));
        }
        [Test] public void RecordedGongHasFourSeparatedAttacksAndALongFinalTail()
        {
            var clip = game.feedback.bell; var samples = new float[clip.samples];
            Assert.That(clip.channels, Is.EqualTo(1)); Assert.That(clip.GetData(samples, 0), Is.True);
            float Rms(float time)
            {
                int start = (int)(time * clip.frequency), count = (int)(0.015f * clip.frequency); double sum = 0;
                for (int i = 0; i < count; i++) sum += samples[start + i] * samples[start + i];
                return (float)System.Math.Sqrt(sum / count);
            }
            foreach (float at in new[] { 0f, 0.24f, 0.48f, 0.88f })
            {
                Assert.That(Rms(at + 0.015f), Is.GreaterThan(0.05f));
                if (at > 0) Assert.That(Rms(at - 0.02f), Is.LessThan(0.005f));
            }
            Assert.That(Rms(1.5f), Is.GreaterThan(0.005f));
        }
        [Test] public void KnockoutDoesNotEmitAnotherImpactInTheSameFrame()
        {
            game.Round.Start(90, 10); game.Round.Tick(3, false);
            var report = typeof(BoxingGame).GetMethod("Report", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var impact = new BoxingImpact(ImpactZone.LeftGlove, 1, true, Vector3.zero, game.tuning, ImpactSurface.Body);
            report.Invoke(game, new object[] { impact }); report.Invoke(game, new object[] { impact });
            Assert.That(game.Round.Phase, Is.EqualTo(BoxingPhase.Results));
            Assert.That(game.feedback.Reports, Is.EqualTo(1));
        }
        [Test] public void MenuFreezesTimeAndOpponentThenRearms()
        {
            Advance(6, Pose()); game.menu.Open(); float time = game.Round.TimeLeft; Vector3 enemy = game.Opponent.Left;
            Advance(10, Pose()); Assert.That(game.Round.TimeLeft, Is.EqualTo(time)); Assert.That(game.Opponent.Left, Is.EqualTo(enemy));
            game.menu.Close(); Advance(1, Pose()); Assert.That(game.Round.TimeLeft, Is.LessThan(time));
        }
        [TestCase(false)] [TestCase(true)] public void LostTrackingAndRestoredDistantFistsNeverCreateGhostHits(bool wideVisual)
        {
            Advance(4, Pose()); int hits = game.Round.Hits;
            var missing = Pose(); missing.valid = false; missing.visualValid=wideVisual;
            missing.left=game.Opponent.Head; missing.right=game.Opponent.Body;
            // Beyond the tracking grace the round pauses with the warning; lost hands never land punches.
            Advance(game.tuning.trackingGraceSeconds + .5f, missing);
            Assert.That(game.Paused, Is.True); Assert.That(game.TrackingWarning, Is.True);
            float time = game.Round.TimeLeft; Advance(1, missing); Assert.That(game.Round.TimeLeft, Is.EqualTo(time));
            Assert.That(game.Round.Hits, Is.EqualTo(hits));
            Assert.That(game.presentation.leftGlove.gameObject.activeSelf,Is.EqualTo(wideVisual));
            var restored = Pose(); restored.left = game.Opponent.Head; restored.right = game.Opponent.Head;
            Advance(0.3f, restored); Assert.That(game.Round.Hits, Is.EqualTo(hits));
        }
        [Test] public void BriefHandDropoutKeepsTheRoundRunningWithoutGhostHits()
        {
            Advance(4, Pose()); int hits = game.Round.Hits; float time = game.Round.TimeLeft;
            // Left hand lost for half a second, drawn on the opponent's head by the tracker meanwhile.
            var lost = Pose(); lost.valid = false; lost.headTracked = lost.rightTracked = true; lost.left = game.Opponent.Head;
            Advance(.5f, lost);
            Assert.That(game.Paused, Is.False, game.PauseReason); Assert.That(game.TrackingWarning, Is.False);
            Assert.That(game.Round.TimeLeft, Is.LessThan(time));
            Assert.That(game.LeftHandActive, Is.False); Assert.That(game.presentation.leftGlove.gameObject.activeSelf, Is.False);
            Assert.That(game.Round.Hits, Is.EqualTo(hits));
            // It returns inside the opponent's head: no swing across the gap.
            var back = Pose(); back.left = game.Opponent.Head;
            Advance(.3f, back);
            Assert.That(game.LeftHandActive, Is.True); Assert.That(game.Round.Hits, Is.EqualTo(hits));
        }
        [Test] public void OnlyReachableSurfaceAndStrengthHapticBindingsAreInstalled()
        {
            Assert.That(game.feedback.impactHaptics.Length, Is.EqualTo(18));
            foreach (var trigger in game.feedback.ImpactTriggers())
            {
                var entry = trigger.ResolveEntry(); Assert.That(entry, Is.Not.Null); Assert.That(entry.streamClip, Is.Not.Null);
                Assert.That(entry.mode, Is.EqualTo(Hapbeat.HapticMode.StreamClip)); Assert.That(entry.streamClip.frequency, Is.EqualTo(16000));
            }
        }
        [Test] public void GradualPunchDoesNotLatchBeforeActualContact()
        {
            game.tuning.enemyInterval = 100;
            var pose = Pose(); Advance(4, pose);
            pose.left = game.Opponent.Head + Vector3.back * 0.5f;
            Advance(0.4f, pose);
            int before = game.Round.Hits;
            float contactDistance = game.tuning.gloveRadius + game.Opponent.Head.z - OpponentFront(game, true, game.Opponent.Head.y).z;
            int steps = Mathf.CeilToInt((.5f - contactDistance) / .008f) + 2;
            for (int i = 0; i < steps; i++)
            {
                pose.left = game.Opponent.Head + Vector3.back * (0.5f - i * 0.008f);
                game.Simulate(1f / 90, pose);
            }
            Assert.That(game.Round.Hits - before, Is.EqualTo(1), "The 3cm release margin must not claim contact before a real hit.");
            Advance(0.4f, pose);
            Assert.That(game.Round.Hits - before, Is.EqualTo(1), "Holding contact must not repeat the impact.");
        }
        [Test] public void KnockoutMustNotCancelWinningImpact()
        {
            game.tuning.tapDamage=2;
            game.Round.Start(90,.01f);
            var pose=Pose(); int stopsAtHit=-1;
            game.feedback.Reported += impact => { if (impact.attack) stopsAtHit=game.feedback.ImpactStops; };
            Advance(3.6f,pose);
            int ringsBefore=game.feedback.Rings;
            for(int i=0;i<80 && game.Round.Phase!=BoxingPhase.Results;i++)
            {
                pose.left=game.Opponent.Head+Vector3.right*Mathf.Max(0,.5f-i*.01f);
                game.Simulate(1f/90,pose);
            }
            Assert.That(stopsAtHit,Is.GreaterThanOrEqualTo(0),"Winning collision must be exercised.");
            Assert.That(game.Round.Phase,Is.EqualTo(BoxingPhase.Results));
            Assert.That(game.feedback.ImpactStops,Is.EqualTo(stopsAtHit),"KO transition must let the final impact audio and haptic clip finish.");
            Assert.That(game.feedback.Rings,Is.EqualTo(ringsBefore+1),"Gong and result voice start on the result frame without cancelling the impact.");
            Assert.That(game.feedback.ResultVoiceCues,Is.EqualTo(1));
            Advance(game.feedback.ImpactTailSeconds+.3f,pose);
            Assert.That(game.feedback.Rings,Is.EqualTo(ringsBefore+1));
        }
        [Test] public void TargetingAndSwitchAllowlistArePresent()
        {
            var settings = Resources.Load<Hapbeat.DemoSwitch.DemoSwitchSettings>("HapbeatDemoSwitchSettings");
            Assert.That(settings.CurrentDemoId, Is.EqualTo("boxing"));
            Assert.That(settings.TryResolveTarget("gloveball", out var target), Is.True);
            Assert.That(target.PackageName, Is.EqualTo("jp.hapbeat.gloveballdemo"));
            string[] targets = { "*/pos_l_wrist", "*/pos_r_wrist", "*/pos_neck" };
            for (int i = 0; i < 18; i++)
                foreach (var trigger in game.feedback.impactHaptics[i].triggers) Assert.That(trigger.ResolveEntry().target, Is.EqualTo(targets[i / 8]));
            Assert.That(settings.TryResolveTarget("handdemo", out target), Is.True);
            Assert.That(target.PackageName, Is.EqualTo("com.Hapbeat.HapticHandDemo_G2"));
        }
        [Test] public void ExternalTransitionCanResumeIfAppLaunchFails()
        {
            Advance(6, Pose()); game.PauseForExternalTransition();
            float time = game.Round.TimeLeft; Advance(2, Pose());
            Assert.That(game.menu.IsOpen, Is.True); Assert.That(game.Round.TimeLeft, Is.EqualTo(time));
            Assert.That(game.menu.panel.Find("Look to select progress"), Is.Null);
            game.menu.Activate(0); Advance(1, Pose());
            Assert.That(game.menu.IsOpen, Is.False); Assert.That(game.Round.TimeLeft, Is.LessThan(time));
        }
        [Test] public void LeanAfterTelegraphDodgesTheCommittedPunch()
        {
            // The lean moves the gloves too; keep the opponent from reading it so the committed strike runs.
            game.tuning.opponentReadChanceLow = game.tuning.opponentReadChanceHigh = 0;
            var pose = Pose();
            for (int i = 0; i < 600 && !game.Opponent.Telegraphing; i++) game.Simulate(1f / 90, pose);
            Assert.That(game.Opponent.Telegraphing, Is.True);
            int completed = game.Opponent.CompletedAttacks, taken = game.Round.Taken;
            for (int i = 0; i < 10; i++)
            {
                pose.head.x -= 0.05f; pose.left.x -= 0.05f; pose.right.x -= 0.05f;
                game.Simulate(1f / 90, pose);
            }
            for (int i = 0; i < 180 && game.Opponent.CompletedAttacks == completed; i++) game.Simulate(1f / 90, pose);
            Assert.That(game.Opponent.CompletedAttacks, Is.EqualTo(completed + 1));
            Assert.That(game.Round.Taken, Is.EqualTo(taken)); Assert.That(game.Round.Dodges, Is.GreaterThanOrEqualTo(1));
        }
        [Test] public void TrackedControllersDoNotHaveGazeOverrideTheirMenuSelection()
        {
            game.input.mode = BoxingInputMode.Controllers; game.input.SetTestPose(Pose());
            Assert.That(game.menu.UsesHandPointer, Is.False);
            game.input.mode = BoxingInputMode.Hands; Assert.That(game.menu.UsesHandPointer, Is.True);
        }
        [Test] public void LostControllerTrackingDoesNotEnableGazeMenu()
        {
            game.input.mode = BoxingInputMode.Controllers; var missing = Pose(); missing.valid = false; game.input.SetTestPose(missing);
            Assert.That(game.menu.UsesHandPointer, Is.False);
        }
        [Test] public void WristHapticsNeverPlayBelowTheFloorButTheNeckKeepsTheSpeedGain()
        {
            var slowGuard = new BoxingImpact(ImpactZone.LeftGlove, 0.6f, false, Vector3.zero, game.tuning, ImpactSurface.Glove);
            var slowHit = new BoxingImpact(ImpactZone.Body, 0.6f, false, Vector3.zero, game.tuning, ImpactSurface.Body);
            var fastPunch = new BoxingImpact(ImpactZone.RightGlove, 4, Vector3.zero, game.tuning, ImpactSurface.Body, 1);
            Assert.That(slowGuard.gain, Is.LessThan(game.feedback.wristMinimumGain));
            Assert.That(game.feedback.HapticGain(slowGuard), Is.EqualTo(game.feedback.wristMinimumGain));
            Assert.That(game.feedback.HapticGain(slowHit), Is.EqualTo(slowHit.gain));
            Assert.That(game.feedback.HapticGain(fastPunch), Is.EqualTo(fastPunch.gain));
        }
        [Test] public void GlovesBehindHeadCannotRetroactivelyBlock()
        {
            var pose = Pose(true); pose.left.z = pose.right.z = -0.3f;
            Advance(95, pose);
            Assert.That(game.Round.Taken, Is.GreaterThan(10)); Assert.That(game.Round.Blocks, Is.Zero);
        }
        [Test] public void FeedbackRetainsRelativeSpeedGainAndWaveformAcrossAllZones()
        {
            foreach (ImpactZone zone in System.Enum.GetValues(typeof(ImpactZone)))
            {
                game.feedback.Impact(new BoxingImpact(zone, 0.6f, false, Vector3.zero, game.tuning, ImpactSurface.Body));
                float weak = game.feedback.LastImpact.gain; Assert.That(game.feedback.LastImpact.hard, Is.False);
                game.feedback.Impact(new BoxingImpact(zone, 5, false, Vector3.zero, game.tuning, ImpactSurface.Body));
                Assert.That(game.feedback.LastImpact.gain, Is.GreaterThan(weak)); Assert.That(game.feedback.LastImpact.hard, Is.True);
                Assert.That(game.feedback.LastImpact.zone, Is.EqualTo(zone));
            }
            Assert.That(game.feedback.Sends, Is.Zero);
        }
    }
}
