using NUnit.Framework;
using UnityEngine;

namespace Hapbeat.Boxing.Tests
{
    public sealed class BoxingVarietyTests
    {
        [Test] public void PinchRequiresReleaseAndMeasuredJoints()
        {
            var p = new BoxingHandPointer(); Vector3 tip = Vector3.forward * .1f;
            p.Sample(true, tip, Vector3.forward, true, false); Assert.That(p.Pressed, Is.False);
            p.Sample(true, tip, Vector3.forward, false, true);
            p.Sample(true, tip, Vector3.forward, true, false); Assert.That(p.Pressed, Is.True);
            p.Sample(true, tip, Vector3.forward, true, false); Assert.That(p.Pressed, Is.False);
            p.Sample(false, default, default, false, false); Assert.That(p.Valid, Is.False);
            p.Sample(true, tip, Vector3.forward, true, false); Assert.That(p.Pressed, Is.False);
        }
        [Test] public void FourAttackTypesReachDistinctHeadAndBodyHeights()
        {
            var tuning = ScriptableObject.CreateInstance<BoxingTuning>();
            try
            {
                var enemy = new BoxingOpponent(tuning); int mask = 0; Vector3 head = new Vector3(0,1.65f,.4f);
                for (int i=0;i<12000;i++)
                {
                    enemy.Tick(.005f,head,true);
                    if (!enemy.Striking || enemy.MotionWeight < .96f) continue;
                    mask |= 1 << (int)enemy.Attack;
                    Vector3 fist = enemy.AttackLeft ? enemy.Left : enemy.Right;
                    float height = enemy.BodyAttack ? head.y-.43f : head.y;
                    Assert.That(fist.y, Is.EqualTo(height).Within(.07f));
                }
                Assert.That(mask,Is.EqualTo(15));
            }
            finally { Object.DestroyImmediate(tuning); }
        }
        [Test] public void BodyHitDamagesPlayerAndUsesExistingReceivedHitFeedback()
        {
            var tuning = ScriptableObject.CreateInstance<BoxingTuning>();
            try
            {
                var round = new BoxingRound(); round.Start(tuning.roundSeconds,tuning.maximumHealth); round.Tick(3,false);
                var impact = new BoxingImpact(ImpactZone.Body,4,false,Vector3.zero,tuning,ImpactSurface.Body);
                round.Report(impact);
                Assert.That(round.PlayerHealth,Is.LessThan(tuning.maximumHealth));
                Assert.That(BoxingFeedback.TriggerIndex(impact),Is.InRange(8,11));
            }
            finally { Object.DestroyImmediate(tuning); }
        }
        [Test] public void CounterWaitsForTelegraphAndDoesNotInterruptActiveAttack()
        {
            var tuning = ScriptableObject.CreateInstance<BoxingTuning>();
            try
            {
                var enemy = new BoxingOpponent(tuning);
                for(int i=0;i<3;i++) enemy.ObservePlayerPunch(true);
                enemy.Tick(.26f,Vector3.up*1.65f,true);
                Assert.That(enemy.Counter,Is.True); Assert.That(enemy.Telegraphing,Is.True); Assert.That(enemy.Striking,Is.False);
                int id=enemy.AttackId;
                for(int i=0;i<30;i++) enemy.ObservePlayerPunch(false);
                Assert.That(enemy.AttackId,Is.EqualTo(id)); Assert.That(enemy.Attack,Is.EqualTo(BoxingAttack.BodyStraight));
            }
            finally { Object.DestroyImmediate(tuning); }
        }
    }
}
