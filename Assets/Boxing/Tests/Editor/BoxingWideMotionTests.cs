using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Xml;
using NUnit.Framework;
using UnityEngine;

namespace Hapbeat.Boxing.Tests
{
    public sealed class BoxingWideMotionTests
    {
        private readonly List<ulong> destroyed=new List<ulong>();
        private int failHand, creates;
        private uint active;
        private ulong flags;
        [SetUp] public void Setup() {destroyed.Clear();failHand=creates=0;active=1;flags=3;}
        private int Create(ulong session,ref BoxingWideMotionTracker.CreateInfo info,out ulong handle)
        {
            Assert.That(session,Is.EqualTo(9)); Assert.That(info.type,Is.EqualTo(1000051001));
            Assert.That(info.jointSet,Is.EqualTo(1));
            var wide=Marshal.PtrToStructure<BoxingWideMotionTracker.WideInfo>(info.next);
            Assert.That(wide.type,Is.EqualTo(1000539000)); Assert.That(wide.mode,Is.EqualTo(1)); Assert.That(wide.next,Is.EqualTo(IntPtr.Zero));
            creates++;handle=info.hand==failHand?0ul:(ulong)info.hand;
            return info.hand==failHand?-1:0;
        }
        private int Locate(ulong handle,ref BoxingWideMotionTracker.LocateInfo info,ref BoxingWideMotionTracker.Locations locations)
        {
            Assert.That(info.type,Is.EqualTo(1000051002)); Assert.That(info.space,Is.EqualTo(7)); Assert.That(info.time,Is.EqualTo(123));
            Assert.That(locations.count,Is.EqualTo(26)); Assert.That(locations.type,Is.EqualTo(1000051003));
            locations.active=active;
            Marshal.StructureToPtr(new BoxingWideMotionTracker.Joint {flags=flags,position=new Vector3((float)handle,2,3),rotation=Quaternion.identity},
                IntPtr.Add(locations.joints,Marshal.SizeOf<BoxingWideMotionTracker.Joint>()),false);
            return 0;
        }
        private int Destroy(ulong handle) {destroyed.Add(handle);return 0;}
        [Test] public void SeparateTrackersUseWmmChainAndConvertWristToUnity()
        {
            using(var tracker=new BoxingWideMotionTracker(Create,Locate,Destroy))
            {
                Assert.That(tracker.Start(9),Is.True); Assert.That(creates,Is.EqualTo(2));
                Assert.That(tracker.TryGet(true,7,123,out var pose),Is.True);
                Assert.That(pose.position,Is.EqualTo(new Vector3(1,2,-3)));
                Assert.That(tracker.TryGet(false,7,123,out pose),Is.True); Assert.That(pose.position.x,Is.EqualTo(2));
                tracker.Dispose();tracker.Dispose(); Assert.That(tracker.Ready,Is.False);
            }
            CollectionAssert.AreEquivalent(new ulong[]{1,2},destroyed);
        }
        [Test] public void PartialCreationFailureReleasesSuccessfulHand()
        {
            failHand=2;
            using(var tracker=new BoxingWideMotionTracker(Create,Locate,Destroy))
            {Assert.That(tracker.Start(9),Is.False); Assert.That(tracker.Ready,Is.False);}
            CollectionAssert.AreEqual(new ulong[]{1},destroyed);
        }
        [Test] public void InactiveOrInvalidDataCannotProduceVisualPose()
        {
            using(var tracker=new BoxingWideMotionTracker(Create,Locate,Destroy))
            {
                tracker.Start(9); active=0; Assert.That(tracker.TryGet(true,7,123,out _),Is.False);
                active=1;flags=1;Assert.That(tracker.TryGet(true,7,123,out _),Is.False);
                flags=3;Assert.That(tracker.TryGet(true,0,123,out _),Is.False);
            }
        }
        [Test] public void AndroidManifestAddsOptionalBodyFeatureAndPermissionExactlyOnce()
        {
            var xml=new XmlDocument();xml.LoadXml("<manifest xmlns:android='http://schemas.android.com/apk/res/android'><application /></manifest>");
            Editor.BoxingWideMotionBuild.AddManifestEntries(xml);Editor.BoxingWideMotionBuild.AddManifestEntries(xml);
            Assert.That(xml.SelectNodes("/manifest/uses-feature").Count,Is.EqualTo(1));
            Assert.That(xml.SelectNodes("/manifest/uses-permission").Count,Is.EqualTo(1));
            Assert.That(((XmlElement)xml.SelectSingleNode("/manifest/uses-feature")).GetAttribute("required","http://schemas.android.com/apk/res/android"),Is.EqualTo("false"));
        }
        [Test] public void Arm64AbiMatchesOpenXrLayouts()
        {
            Assert.That(IntPtr.Size,Is.EqualTo(8));
            Assert.That(Marshal.SizeOf<BoxingWideMotionTracker.CreateInfo>(),Is.EqualTo(24));
            Assert.That(Marshal.SizeOf<BoxingWideMotionTracker.WideInfo>(),Is.EqualTo(24));
            Assert.That(Marshal.SizeOf<BoxingWideMotionTracker.LocateInfo>(),Is.EqualTo(32));
            Assert.That(Marshal.SizeOf<BoxingWideMotionTracker.Locations>(),Is.EqualTo(32));
            Assert.That(Marshal.SizeOf<BoxingWideMotionTracker.Joint>(),Is.EqualTo(40));
        }
    }
}
