using UnityEngine;
using UnityEngine.XR.Hands;

namespace Hapbeat.Boxing
{
    // Skeleton and joints are driven by Unity's XRHandSkeletonDriver, independently per hand.
    // Renderers are supplied by BoxingHandModelResolver once the hand model has been instantiated.
    public sealed class BoxingMenuHand : MonoBehaviour
    {
        public BoxingMenu menu;
        public XRHandTrackingEvents tracking;
        public Renderer[] renderers;
        public bool Visible { get; private set; }
        private void LateUpdate() => SetVisible(menu.IsOpen && menu.UsesHandPointer && tracking.bindableHandIsTracked.Value);
        private void OnDisable() => SetVisible(false);
        private void SetVisible(bool visible)
        {
            Visible = visible;
            if(renderers==null) return;
            foreach(var renderer in renderers) if(renderer!=null) renderer.enabled=visible;
        }
    }
}
