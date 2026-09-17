using System;
using System.Linq;
using UnityEngine;

namespace Hapbeat.Boxing
{
    // Visual adapter only. The game's swept spheres remain collision authority.
    public sealed class BoxingOpponentAvatar : MonoBehaviour
    {
        public SkinnedMeshRenderer skin;
        public Transform[] bones;
        public Vector3[] restPositions;
        public Quaternion[] restRotations;
        public Vector3[] restScales;
        public Transform head, spine, chest, leftUpper, leftFore, leftHand, rightUpper, rightFore, rightHand;
        public Transform leftThigh, leftShin, leftFoot, rightThigh, rightShin, rightFoot;
        public Vector3 headCenter, leftCenter, rightCenter;
        public float neutralHeadHeight;
        public float Reaction { get; private set; }
        public bool BodyReaction { get; private set; }
        public Vector3 HeadCenter => head.TransformPoint(headCenter);
        public Vector3 LeftCenter => leftHand.TransformPoint(leftCenter);
        public Vector3 RightCenter => rightHand.TransformPoint(rightCenter);

        public void CaptureRest()
        {
            skin = GetComponentInChildren<SkinnedMeshRenderer>();
            bones = skin.bones;
            restPositions = bones.Select(b => b.localPosition).ToArray();
            restRotations = bones.Select(b => b.localRotation).ToArray();
            restScales = bones.Select(b => b.localScale).ToArray();
            Transform Bone(string name) => bones.Single(b => b.name == name);
            head = Bone("head"); spine = Bone("spine"); chest = Bone("chest");
            // The imported character faces +Z; after facing -Z its anatomical right is screen left.
            leftUpper = Bone("upper_arm.R"); leftFore = Bone("forearm.R"); leftHand = Bone("hand.R");
            rightUpper = Bone("upper_arm.L"); rightFore = Bone("forearm.L"); rightHand = Bone("hand.L");
            leftThigh = Bone("thigh.R"); leftShin = Bone("shin.R"); leftFoot = Bone("foot.R");
            rightThigh = Bone("thigh.L"); rightShin = Bone("shin.L"); rightFoot = Bone("foot.L");
            headCenter = WeightedCenter(head); leftCenter = WeightedCenter(leftHand); rightCenter = WeightedCenter(rightHand);
            neutralHeadHeight = HeadCenter.y - transform.position.y;
            skin.updateWhenOffscreen = true;
        }
        private Vector3 WeightedCenter(Transform bone)
        {
            int index = Array.IndexOf(skin.bones, bone);
            var mesh = skin.sharedMesh; var vertices = mesh.vertices; var weights = mesh.boneWeights;
            // Bind poses convert the mesh's rest vertices into the bone frame even when FBX opens posed.
            var bounds = new Bounds(); bool first = true;
            for (int i = 0; i < vertices.Length; i++)
            {
                var w = weights[i];
                if (!(w.boneIndex0 == index && w.weight0 > .9f)) continue;
                Vector3 point = mesh.bindposes[index].MultiplyPoint3x4(vertices[i]);
                if (first) { bounds = new Bounds(point, Vector3.zero); first = false; } else bounds.Encapsulate(point);
            }
            if (first) throw new InvalidOperationException("No weighted vertices for " + bone.name);
            return bounds.center;
        }
        public void React(bool body, float gain)
        {
            BodyReaction = body; Reaction = Mathf.Max(Reaction, Mathf.Lerp(.3f, 1, gain));
        }
        public void ResetReaction() { Reaction = 0; BodyReaction = false; }
        public void Render(BoxingOpponent opponent, float dt)
        {
            for (int i = 0; i < bones.Length; i++)
            {
                bones[i].localPosition = restPositions[i]; bones[i].localRotation = restRotations[i]; bones[i].localScale = restScales[i];
            }
            transform.SetPositionAndRotation(opponent.Root, Quaternion.Euler(0, 180, 0));
            transform.localScale = Vector3.one * ((opponent.Head.y - opponent.Root.y) / neutralHeadHeight);
            var lf = leftFoot.position; var rf = rightFoot.position;
            var lr = leftFoot.rotation; var rr = rightFoot.rotation;
            float angle = (BodyReaction ? -10 : 6) * Reaction;
            spine.rotation = Quaternion.AngleAxis(angle, Vector3.right) * spine.rotation;
            chest.rotation = Quaternion.AngleAxis(angle * .5f, Vector3.right) * chest.rotation;
            head.rotation = Quaternion.AngleAxis((BodyReaction ? -8 : 14) * Reaction, Vector3.right) * head.rotation;
            // Keep the head's rendered centre on its hit sphere while bending the body beneath it.
            transform.position += opponent.Head - HeadCenter;
            Solve(leftThigh, leftShin, leftFoot, lf, Vector3.back); leftFoot.rotation = lr;
            Solve(rightThigh, rightShin, rightFoot, rf, Vector3.back); rightFoot.rotation = rr;
            Arm(leftUpper, leftFore, leftHand, leftCenter, opponent.Left, opponent.Head, -1);
            Arm(rightUpper, rightFore, rightHand, rightCenter, opponent.Right, opponent.Head, 1);
            Reaction = Mathf.Max(0, Reaction - Mathf.Max(0, dt) * 2.8f);
        }
        private void Arm(Transform upper, Transform fore, Transform hand, Vector3 center, Vector3 target, Vector3 targetHead, float side)
        {
            float extension = Mathf.Clamp01((targetHead.z - target.z - .28f) / .35f);
            Vector3 direction = Vector3.Lerp(Vector3.up, Vector3.back, extension).normalized;
            Quaternion rotation = Quaternion.FromToRotation(hand.up, direction) * hand.rotation;
            Vector3 offset = rotation * Vector3.Scale(center, hand.lossyScale);
            Vector3 pole = new Vector3(side * (BodyReaction ? .15f : .6f), -1, .15f);
            Solve(upper, fore, hand, target - offset, pole);
            hand.rotation = rotation;
        }
        private static void Solve(Transform upper, Transform lower, Transform end, Vector3 target, Vector3 pole)
        {
            Vector3 start = upper.position, delta = target - start;
            float distance = Mathf.Max(.0001f, delta.magnitude);
            Vector3 direction = delta / distance;
            float a = Vector3.Distance(start, lower.position), b = Vector3.Distance(lower.position, end.position);
            // Existing attacks can reach beyond anatomical length. Preserve visible contact,
            // stretching the skinned chain rather than displaying a detached/invisible hit sphere.
            float stretch = Mathf.Max(1, distance / (a + b) * 1.001f); a *= stretch; b *= stretch;
            float along = Mathf.Clamp((a*a - b*b + distance*distance) / (2*distance), -a, a);
            Vector3 bend = Vector3.ProjectOnPlane(pole, direction).normalized;
            if (bend.sqrMagnitude < .001f) bend = Vector3.Cross(direction, Vector3.right).normalized;
            Vector3 elbow = start + direction * along + bend * Mathf.Sqrt(Mathf.Max(0, a*a - along*along));
            upper.rotation = Quaternion.FromToRotation(lower.position - start, elbow - start) * upper.rotation;
            lower.position = elbow;
            lower.rotation = Quaternion.FromToRotation(end.position - lower.position, target - lower.position) * lower.rotation;
            end.position = target;
        }
    }
}
