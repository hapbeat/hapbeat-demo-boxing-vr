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
            // Height is fixed throughout a round. Bobbing lowers the hips over planted feet,
            // rather than scaling the entire character up and down.
            transform.localScale = Vector3.one * (opponent.StandingHeadHeight / neutralHeadHeight);
            var lf = leftFoot.position + new Vector3(-.035f,0,-.13f);
            var rf = rightFoot.position + new Vector3(.035f,0,.13f);
            var lr = leftFoot.rotation; var rr = rightFoot.rotation;
            float angle = (BodyReaction ? -10 : 6) * Reaction;
            spine.rotation = Quaternion.Euler(angle, 12 + opponent.BodyYaw * .45f, 0) * spine.rotation;
            chest.rotation = Quaternion.Euler(angle * .5f, opponent.BodyYaw * .55f, 0) * chest.rotation;
            head.rotation = Quaternion.AngleAxis(-12 - opponent.BodyYaw,Vector3.up) * head.rotation;
            head.rotation = Quaternion.AngleAxis((BodyReaction ? -8 : 14) * Reaction, Vector3.right) * head.rotation;
            // Keep the head's rendered centre on its hit sphere while bending the body beneath it.
            transform.position += opponent.Head - HeadCenter;
            Solve(leftThigh, leftShin, leftFoot, lf, Vector3.back); leftFoot.rotation = lr;
            Solve(rightThigh, rightShin, rightFoot, rf, Vector3.back); rightFoot.rotation = rr;
            Arm(leftUpper, leftFore, leftHand, leftCenter, opponent.Left, opponent.Head, -1, opponent.Hook && opponent.AttackLeft ? opponent.MotionWeight : 0);
            Arm(rightUpper, rightFore, rightHand, rightCenter, opponent.Right, opponent.Head, 1, opponent.Hook && !opponent.AttackLeft ? opponent.MotionWeight : 0);
            Reaction = Mathf.Max(0, Reaction - Mathf.Max(0, dt) * 2.8f);
        }
        private void Arm(Transform upper, Transform fore, Transform hand, Vector3 center, Vector3 target, Vector3 targetHead, float side, float hook)
        {
            float extension = Mathf.Clamp01((targetHead.z - target.z - .28f) / .35f);
            Vector3 direction = Vector3.Lerp(Vector3.up, Vector3.back, extension).normalized;
            Vector3 pole = Vector3.Lerp(new Vector3(side * .3f, -1, .1f), new Vector3(side, .05f, .1f), hook);
            // Explicit two-axis wrist frame: local Y runs cuff -> knuckles, local Z
            // is the back of the glove. Iterate towards the forearm to avoid bent wrists.
            for (int i=0; i<5; i++)
            {
                Vector3 back = Vector3.Lerp(Vector3.back,Vector3.up,extension);
                back = Vector3.ProjectOnPlane(back,direction).normalized;
                if(back.sqrMagnitude<.001f) back=Vector3.ProjectOnPlane(Vector3.right,direction).normalized;
                Quaternion rotation = Quaternion.LookRotation(back,direction);
                Vector3 offset = rotation * Vector3.Scale(center, hand.lossyScale);
                Solve(upper, fore, hand, target - offset, pole);
                hand.rotation = rotation;
                direction = (hand.position-fore.position).normalized;
            }
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
