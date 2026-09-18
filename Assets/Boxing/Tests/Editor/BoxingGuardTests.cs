using NUnit.Framework;
using UnityEngine;

namespace Hapbeat.Boxing.Tests
{
    public sealed class BoxingGuardTests
    {
        private BoxingTuning tuning;
        [SetUp] public void Setup()=>tuning=ScriptableObject.CreateInstance<BoxingTuning>();
        [TearDown] public void Cleanup()=>Object.DestroyImmediate(tuning);
        [Test] public void AllFourGuardsInterceptTheirIntendedDirectionBeforeTheBody()
        {
            var enemy=new BoxingOpponent(tuning); int seen=0;
            for(int i=0;i<30000 && seen!=15;i++)
            {
                enemy.Tick(.01f,Vector3.up*1.65f,true);
                if(enemy.GuardWeight<.999f) continue;
                Vector3 target=enemy.BodyGuard ? enemy.Body : enemy.Head;
                float radius=enemy.BodyGuard ? tuning.enemyBodyRadius : tuning.enemyHeadRadius;
                foreach(int side in new[]{-1,1})
                {
                    Vector3 from=target+(enemy.HookGuard ? Vector3.right*side : Vector3.back)*.8f;
                    Assert.That(BoxingCollision.Sweep(from,target,tuning.gloveRadius,target,target,radius,out float bodyTime),Is.True);
                    float first=float.PositiveInfinity;
                    foreach(var hand in new[]{enemy.Left,enemy.Right})
                        if(BoxingCollision.Sweep(from,target,tuning.gloveRadius,hand,hand,tuning.gloveRadius,out float t)) first=Mathf.Min(first,t);
                    Assert.That(first,Is.LessThan(bodyTime),enemy.Guard+" must cover the incoming path.");
                }
                seen|=1<<(int)enemy.Guard;
            }
            Assert.That(seen,Is.EqualTo(15));
        }
        [Test] public void NpcRaisesGuardAfterRetractingAPunch()
        {
            var enemy=new BoxingOpponent(tuning);
            for(int i=0;i<1000 && enemy.CompletedAttacks==0;i++) enemy.Tick(.01f,Vector3.up*1.65f,true);
            Assert.That(enemy.CompletedAttacks,Is.EqualTo(1));
            for(int i=0;i<20;i++) enemy.Tick(.01f,Vector3.up*1.65f,true);
            Assert.That(enemy.Guarding,Is.True); Assert.That(enemy.GuardWeight,Is.GreaterThan(.9f));
        }
        [Test] public void LowWidePreparationBiasesBodyHookGuardButLeavesOtherChoices()
        {
            var enemy=new BoxingOpponent(tuning); int matches=0,total=0,seen=0; bool was=false;
            for(int i=0;i<40000;i++)
            {
                var head=Vector3.up*1.65f;
                enemy.ObservePlayerStance(head,head+new Vector3(-.4f,-.4f,.3f),head+new Vector3(.4f,-.4f,.3f));
                enemy.Tick(.01f,head,true);
                if(enemy.Guarding && !was) {total++; if(enemy.Guard==BoxingGuard.BodyHook) matches++; seen|=1<<(int)enemy.Guard;}
                was=enemy.Guarding;
            }
            Assert.That(total,Is.GreaterThan(40)); Assert.That(matches/(float)total,Is.InRange(.45f,.85f));
            Assert.That(seen,Is.EqualTo(15));
        }
    }
}
