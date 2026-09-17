using NUnit.Framework;
using UnityEngine;

namespace Hapbeat.Boxing.Tests
{
    public sealed class BoxingHandMenuTests
    {
        private readonly Vector3 head = new Vector3(0, 1.65f, 0);
        private readonly Vector3 palm = new Vector3(-0.2f, 1.4f, 0.3f);
        private int Hold(BoxingHandMenuGesture gesture, bool tracked = true, bool open = true, bool facing = true)
        {
            int triggers = 0;
            for (int i = 0; i < 120; i++)
                if (gesture.Update(tracked, open, palm, (head - palm).normalized * (facing ? 1 : -1), head, 0.02f)) triggers++;
            return triggers;
        }
        [Test] public void HoldingTriggersOnceAndReleaseRearms()
        {
            var gesture = new BoxingHandMenuGesture();
            Assert.That(Hold(gesture), Is.EqualTo(1));
            Assert.That(Hold(gesture), Is.Zero);
            gesture.Update(true, false, palm, head - palm, head, 0.02f);
            Assert.That(Hold(gesture), Is.EqualTo(1));
        }
        [TestCase(false, true, true)]
        [TestCase(true, false, true)]
        [TestCase(true, true, false)]
        public void InvalidPoseNeverOpensMenu(bool tracked, bool open, bool facing)
        {
            Assert.That(Hold(new BoxingHandMenuGesture(), tracked, open, facing), Is.Zero);
        }
        [Test] public void TrackingLossAndLongFrameDiscardPartialHold()
        {
            var gesture = new BoxingHandMenuGesture();
            for (int i = 0; i < 30; i++) Assert.That(gesture.Update(true, true, palm, head - palm, head, 0.02f), Is.False);
            Assert.That(gesture.Update(false, true, palm, head - palm, head, 0.02f), Is.False);
            for (int i = 0; i < 30; i++) Assert.That(gesture.Update(true, true, palm, head - palm, head, 0.02f), Is.False);
            Assert.That(gesture.Update(true, true, palm, head - palm, head, 1), Is.False);
            for (int i = 0; i < 30; i++) Assert.That(gesture.Update(true, true, palm, head - palm, head, 0.02f), Is.False);
        }
    }
}
