using UnityEngine;
using UnityEngine.XR.Hands;

namespace Hapbeat.Boxing
{
    // Skeleton and joints are driven by Unity's XRHandSkeletonDriver, independently per hand.
    public sealed class BoxingMenuHand : MonoBehaviour
    {
        public BoxingMenu menu;
        public XRHandTrackingEvents tracking;
        public SkinnedMeshRenderer mesh;
        private void LateUpdate()
        {
            mesh.enabled = menu.IsOpen && menu.UsesHandPointer && tracking.bindableHandIsTracked.Value;
        }
        private void OnDisable() { if(mesh!=null) mesh.enabled=false; }
    }
}
