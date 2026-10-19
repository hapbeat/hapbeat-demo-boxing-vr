using System;
using System.Reflection;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Hapbeat.Boxing.Tests
{
    public class BoxingEventMapTests
    {
        [TestCase("Assets")] [TestCase("Assets/Boxing/Haptics")] [TestCase("Packages")] [TestCase("")]
        public void ManifestSearchHandlesRootAndStandaloneFolders(string folder)
        {
            var type=Type.GetType("Hapbeat.Editor.HapbeatManifestIntensity, Hapbeat.Editor",true);
            var find=type.GetMethod("FindKitManifest",BindingFlags.NonPublic|BindingFlags.Static);
            Assert.DoesNotThrow(()=>find.Invoke(null,new object[]{folder}));
        }
        [Test] public void MapHasOneEntryPerCueSideAndClipWithItsOwnStreamClip()
        {
            var map=AssetDatabase.LoadAssetAtPath<Hapbeat.HapbeatEventMap>("Assets/Boxing/Haptics/BoxingEventMap.asset");
            Assert.That(map.entries.Count,Is.GreaterThanOrEqualTo(18));
            // The first clip of every cue and place in BoxingFeedback.TriggerIndex order, then the further clips
            // (<entry>_2, _3 ...) at the place of their first one.
            var places=new System.Collections.Generic.Dictionary<string,string>();
            for(int i=0;i<map.entries.Count;i++)
            {
                var entry=map.entries[i];
                if(i<18)
                {
                    string cue=BoxingFeedback.Cues[i<16 ? i%8 : i-8];
                    Assert.That(entry.eventName,Is.EqualTo((i<8 ? "left_" : i<16 ? "right_" : "")+cue));
                    Assert.That(entry.target,Is.EqualTo(i<8 ? "*/pos_l_wrist" : i<16 ? "*/pos_r_wrist" : "*/pos_neck"));
                    places[entry.eventName]=entry.target;
                }
                else
                {
                    int split=entry.eventName.LastIndexOf('_');
                    Assert.That(places.TryGetValue(entry.eventName.Substring(0,split),out var target),Is.True,entry.eventName);
                    Assert.That(int.Parse(entry.eventName.Substring(split+1)),Is.GreaterThanOrEqualTo(2));
                    Assert.That(entry.target,Is.EqualTo(target));
                }
                Assert.That(AssetDatabase.GetAssetPath(entry.streamClip),Does.StartWith("Assets/Boxing/Haptics/stream-clips/").And.EndWith(".wav"));
                Assert.That(entry.loop,Is.False); Assert.That(entry.mode,Is.EqualTo(Hapbeat.HapticMode.StreamClip));
                // Wrist routes are raised to 2.0 against the in-game gain; the neck keeps 0.8 (cue table).
                Assert.That(entry.gain,Is.EqualTo(entry.target=="*/pos_neck" ? 0.8f : 2f)); Assert.That(entry.CachedManifestIntensity,Is.GreaterThan(0).And.LessThanOrEqualTo(1));
            }
            Assert.That(map.entries.Select(e=>e.eventName),Is.Unique);
        }
        [Test] public void GameCuesNameTheirTableVariant()
        {
            Assert.That(Hapbeat.Boxing.Editor.BoxingHapticImport.TableCue("punch_glove_soft"),Is.EqualTo("punch_glove:soft"));
            Assert.That(Hapbeat.Boxing.Editor.BoxingHapticImport.TableCue("hit_body_hard"),Is.EqualTo("hit_body:hard"));
            Assert.That(Hapbeat.Boxing.Editor.BoxingHapticImport.EntryName("left_guard_arm_soft",0),Is.EqualTo("left_guard_arm_soft"));
            Assert.That(Hapbeat.Boxing.Editor.BoxingHapticImport.EntryName("left_guard_arm_soft",1),Is.EqualTo("left_guard_arm_soft_2"));
        }
        [Test] public void CuesSeparateTheContactPartAndTheStrength()
        {
            var tuning=ScriptableObject.CreateInstance<BoxingTuning>();
            try
            {
                string Cue(BoxingImpact impact)=>BoxingFeedback.CueName(impact);
                foreach(var speed in new[]{1f,4f})
                {
                    string level=speed<tuning.hardHitSpeed ? "_soft" : "_hard";
                    // Own punches: the opponent's glove, or anything else (head / body / arms). Soft / hard follows
                    // the punch strength (like the damage), not the contact speed.
                    float medium=tuning.punchHardStrength-.05f;
                    Assert.That(Cue(new BoxingImpact(ImpactZone.LeftGlove,speed,Vector3.zero,tuning,ImpactSurface.Glove,1)),Is.EqualTo("punch_glove_hard"));
                    Assert.That(Cue(new BoxingImpact(ImpactZone.LeftGlove,speed,Vector3.zero,tuning,ImpactSurface.Glove,medium)),Is.EqualTo("punch_glove_soft"));
                    // The opponent's forearm guard is a guard like its glove.
                    Assert.That(Cue(new BoxingImpact(ImpactZone.LeftGlove,speed,Vector3.zero,tuning,ImpactSurface.Glove,1,ImpactPart.Forearm)),Is.EqualTo("punch_glove_hard"));
                    Assert.That(Cue(new BoxingImpact(ImpactZone.LeftGlove,speed,Vector3.zero,tuning,ImpactSurface.Glove,0,ImpactPart.Forearm)),Is.EqualTo("punch_glove_soft"));
                    Assert.That(Cue(new BoxingImpact(ImpactZone.RightGlove,speed,Vector3.zero,tuning,ImpactSurface.Body,1)),Is.EqualTo("punch_body_hard"));
                    Assert.That(Cue(new BoxingImpact(ImpactZone.RightGlove,speed,Vector3.zero,tuning,ImpactSurface.Body,medium)),Is.EqualTo("punch_body_soft"));
                    // An unprepared punch is always soft.
                    Assert.That(Cue(new BoxingImpact(ImpactZone.RightGlove,speed,Vector3.zero,tuning,ImpactSurface.Body,0)),Is.EqualTo("punch_body_soft"));
                    // Received: glove, forearm, head / body.
                    Assert.That(Cue(new BoxingImpact(ImpactZone.LeftGlove,speed,false,Vector3.zero,tuning,ImpactSurface.Glove)),Is.EqualTo("guard_glove"+level));
                    Assert.That(Cue(new BoxingImpact(ImpactZone.RightGlove,speed,false,Vector3.zero,tuning,ImpactSurface.Glove,ImpactPart.Forearm)),Is.EqualTo("guard_arm"+level));
                    foreach(var zone in new[]{ImpactZone.Head,ImpactZone.Body})
                    {
                        var hit=new BoxingImpact(zone,speed,false,Vector3.zero,tuning,ImpactSurface.Body);
                        Assert.That(Cue(hit),Is.EqualTo("hit_body"+level));
                        Assert.That(BoxingFeedback.TriggerIndex(hit),Is.EqualTo(speed<tuning.hardHitSpeed ? 16 : 17));
                    }
                    var right=new BoxingImpact(ImpactZone.RightGlove,speed,false,Vector3.zero,tuning,ImpactSurface.Glove,ImpactPart.Forearm);
                    Assert.That(BoxingFeedback.TriggerIndex(right),Is.EqualTo(8+BoxingFeedback.CueIndex(right)));
                }
                // Continuous mode keeps one strength: always the soft cues.
                tuning.impactMode=ImpactMode.Continuous;
                Assert.That(Cue(new BoxingImpact(ImpactZone.Head,5,false,Vector3.zero,tuning,ImpactSurface.Body)),Is.EqualTo("hit_body_soft"));
                Assert.That(Cue(new BoxingImpact(ImpactZone.LeftGlove,5,Vector3.zero,tuning,ImpactSurface.Glove,1)),Is.EqualTo("punch_glove_soft"));
            }
            finally { UnityEngine.Object.DestroyImmediate(tuning); }
        }
        [Test] public void StandaloneStreamClipManifestLookupDoesNotThrow()
        {
            var clip=AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Boxing/Haptics/stream-clips/hit_body_hard.wav");
            Assert.That(clip,Is.Not.Null);
            Assert.That(clip.LoadAudioData(),Is.True);
            var samples=new float[clip.samples*clip.channels];
            Assert.That(clip.GetData(samples,0),Is.True);
            Assert.That(samples.Length,Is.GreaterThan(0));
            var type=Type.GetType("Hapbeat.Editor.HapbeatManifestIntensity, Hapbeat.Editor",true);
            var find=type.GetMethod("FindManifestForClip",BindingFlags.Public|BindingFlags.Static);
            Assert.DoesNotThrow(()=>find.Invoke(null,new object[]{clip}));
        }
    }
}
