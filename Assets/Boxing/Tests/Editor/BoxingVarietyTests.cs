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
                var enemy = new BoxingOpponent(tuning, 14); int mask = 0; Vector3 head = new Vector3(0,1.65f,.4f);
                for (int i=0;i<24000;i++)
                {
                    enemy.Tick(.005f,head,Quaternion.identity,true);
                    if (!enemy.Striking || enemy.MotionWeight < .96f) continue;
                    mask |= 1 << (int)enemy.Attack;
                    Vector3 fist = enemy.AttackLeft ? enemy.Left : enemy.Right;
                    float height = enemy.BodyAttack ? BoxingPlayerBody.Aim(head,Quaternion.identity).y : head.y;
                    Assert.That(fist.y, Is.EqualTo(height).Within(.07f));
                }
                Assert.That(mask,Is.EqualTo(15));
            }
            finally { Object.DestroyImmediate(tuning); }
        }
        [Test] public void BodyHitDamagesPlayerAndUsesTheNeckHitFeedback()
        {
            var tuning = ScriptableObject.CreateInstance<BoxingTuning>();
            try
            {
                var round = new BoxingRound(); round.Start(tuning.roundSeconds,tuning.maximumHealth); round.Tick(3,false);
                var impact = new BoxingImpact(ImpactZone.Body,4,false,Vector3.zero,tuning,ImpactSurface.Body);
                round.Report(impact);
                Assert.That(round.PlayerHealth,Is.LessThan(tuning.maximumHealth));
                Assert.That(BoxingFeedback.CueName(impact),Is.EqualTo("hit_body_hard"));
                Assert.That(BoxingFeedback.TriggerIndex(impact),Is.EqualTo(17));
            }
            finally { Object.DestroyImmediate(tuning); }
        }
    }
}
