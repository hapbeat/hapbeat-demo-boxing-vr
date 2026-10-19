using System;
using System.Linq;
using UnityEngine;

namespace Hapbeat.Boxing
{
    // One sphere of a hit shape (world space).
    public struct BoxingHitSphere
    {
        public Vector3 center;
        public float radius;
        public BoxingHitSphere(Vector3 center, float radius) { this.center = center; this.radius = radius; }
    }

    // Rig pose provides arm-guard and head / torso hit geometry; game-side sweeps remain collision authority.
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
        // Top of the head mesh above the feet at avatar scale 1 (measured on the skinned mesh: 1.806 m at scale 1.0365).
        public const float RestStature = 1.742f;
        // The avatar scale and stature Render gives this opponent (its standing head height).
        public float ScaleFor(BoxingOpponent opponent) => opponent.StandingHeadHeight / neutralHeadHeight;
        public float StatureFor(BoxingOpponent opponent) => ScaleFor(opponent) * RestStature;
        public const float GloveWidth = .15f;
        public const float OpponentGloveWidth = .15f;
        public const float GloveDepthRatio = .75f;
        public float leftGloveLocalWidth, rightGloveLocalWidth;
        public float leftUpperRadius,leftForeRadius,rightUpperRadius,rightForeRadius;
        public float Reaction { get; private set; }
        public bool BodyReaction { get; private set; }
        public Vector3 HeadCenter => head.TransformPoint(headCenter);
        public Vector3 LeftCenter => leftHand.TransformPoint(leftCenter);
        public Vector3 RightCenter => rightHand.TransformPoint(rightCenter);
        // Elbow poles and torso yaw ease towards the opponent state instead of switching in one frame.
        private Vector3 leftPole, rightPole;
        private float smoothedYaw;
        // Each glove's twist about its forearm (degrees, GloveTwist), eased at most TwistRate so a fast strike cannot spin it.
        private float leftTwist, rightTwist;
        private const float TwistRate = 360;
        // Each elbow's last bend direction (world, from the shoulder-wrist axis): the torso-clearance choice turns it at
        // most BendRate, so a changed choice swings the elbow round instead of flipping it (behind the body, across).
        private Vector3 leftBend, rightBend;
        // Each forearm's last direction. The glove is aligned to it and the arm solved once per frame, so the wrist
        // follows the forearm over frames; iterating to the fixed point within a frame (the former 5 passes from a fixed
        // guess) could settle on another solution and swing the glove and elbow round in one frame.
        private Vector3 leftForearm, rightForearm;
        private const float BendRate = 540;
        private bool smoothingStarted;

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
            headCenter = WeightedBounds(head).center; leftCenter = WeightedBounds(leftHand).center; rightCenter = WeightedBounds(rightHand).center;
            leftGloveLocalWidth=WeightedBounds(leftHand).size.x; rightGloveLocalWidth=WeightedBounds(rightHand).size.x;
            float Radius(Transform bone) { var size=WeightedBounds(bone).size; return Mathf.Max(size.x,size.z)*.5f; }
            leftUpperRadius=Radius(leftUpper); leftForeRadius=Radius(leftFore);
            rightUpperRadius=Radius(rightUpper); rightForeRadius=Radius(rightFore);
            neutralHeadHeight = HeadCenter.y - transform.position.y;
            skin.updateWhenOffscreen = true;
        }
        private Bounds WeightedBounds(Transform bone)
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
            return bounds;
        }
        public void React(bool body, float gain)
        {
            BodyReaction = body; Reaction = Mathf.Max(Reaction, Mathf.Lerp(.3f, 1, gain));
        }
        public void ResetReaction() { Reaction = 0; BodyReaction = false; smoothingStarted = false; }
        // Head and torso hit shapes fitted to the skinned mesh, in each bone's frame (metres at avatar scale 1;
        // +Y up the bone, +Z the opponent's front, X across), so they lean and turn with the rendered body.
        // Torso: one row per mesh ring (spine / chest), its elliptic cross-section (half width A, half depth B,
        // centre Zc) covered by spheres on the major axis, each as large as the ellipse allows at its place.
        // Head: three spheres stacked along the head bone. BoxingHitShapeTests checks them against the mesh.
        private readonly struct TorsoRow
        {
            public readonly bool chest; public readonly float y, zc, a, b;
            public TorsoRow(bool chest, float y, float zMin, float zMax, float a) { this.chest = chest; this.y = y; zc = (zMin + zMax) * .5f; b = (zMax - zMin) * .5f; this.a = a; }
        }
        private static readonly TorsoRow[] TorsoRows = {
            new TorsoRow(false, .005f, -.087f, .087f, .129f), new TorsoRow(false, .065f, -.086f, .090f, .131f), new TorsoRow(false, .095f, -.090f, .097f, .142f),
            new TorsoRow(true, -.055f, -.093f, .108f, .160f), new TorsoRow(true, .005f, -.098f, .117f, .180f), new TorsoRow(true, .065f, -.100f, .124f, .194f),
            new TorsoRow(true, .125f, -.099f, .118f, .207f), new TorsoRow(true, .155f, -.095f, .102f, .216f), new TorsoRow(true, .215f, -.085f, .085f, .214f) };
        // Sphere places along each row's major axis (share of A).
        private static readonly float[] RowSpread = { 0, -.25f, .25f, -.5f, .5f, -.7f, .7f, -.85f, .85f };
        private static readonly float[] HeadHeights = { .065f, .125f, .185f };
        private const float HeadRadius = .109f, HeadZ = .003f;
        public static readonly int TorsoSpheres = 9 * 9, HeadSpheres = 3;
        // Fills head[HeadSpheres] and torso[TorsoSpheres] for the current (rendered) pose. No allocation.
        public void GetHitShapes(BoxingHitSphere[] headShape, BoxingHitSphere[] torso)
        {
            float scale = transform.localScale.x;
            for (int i = 0; i < HeadHeights.Length; i++)
                headShape[i] = new BoxingHitSphere(head.position + head.rotation * (new Vector3(0, HeadHeights[i], HeadZ) * scale), HeadRadius * scale);
            int k = 0;
            foreach (var row in TorsoRows)
            {
                var bone = row.chest ? chest : spine;
                float focus = (row.a * row.a - row.b * row.b) / row.a;
                foreach (float share in RowSpread)
                {
                    float x = share * row.a, ax = Mathf.Abs(x);
                    float radius = ax <= focus ? row.b * Mathf.Sqrt(Mathf.Max(0, 1 - x * x / (row.a * row.a - row.b * row.b))) : row.a - ax;
                    torso[k++] = new BoxingHitSphere(bone.position + bone.rotation * (new Vector3(x, row.y, row.zc) * scale), radius * scale);
                }
            }
        }
        public void GetGuardArms(BoxingGuardArm[] arms)
        {
            BoxingGuardArm Segment(Transform a,Transform b,float radius) => new BoxingGuardArm(a.position,b.position,
                radius*Mathf.Max(Mathf.Abs(a.lossyScale.x),Mathf.Abs(a.lossyScale.z)));
            arms[0]=Segment(leftUpper,leftFore,leftUpperRadius); arms[1]=Segment(leftFore,leftHand,leftForeRadius);
            arms[2]=Segment(rightUpper,rightFore,rightUpperRadius); arms[3]=Segment(rightFore,rightHand,rightForeRadius);
        }
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
            // NPC gloves have the player's 15 cm width (GloveWidth).
            leftHand.localScale *= OpponentGloveWidth/(leftGloveLocalWidth*leftHand.lossyScale.x);
            rightHand.localScale *= OpponentGloveWidth/(rightGloveLocalWidth*rightHand.lossyScale.x);
            leftHand.localScale=Vector3.Scale(leftHand.localScale,new Vector3(1,1,GloveDepthRatio));
            rightHand.localScale=Vector3.Scale(rightHand.localScale,new Vector3(1,1,GloveDepthRatio));
            var lf = leftFoot.position + new Vector3(-.035f,0,-.13f);
            var rf = rightFoot.position + new Vector3(.035f,0,.13f);
            var lr = leftFoot.rotation; var rr = rightFoot.rotation;
            Vector3 leftTarget = Pole(opponent, true, -1), rightTarget = Pole(opponent, false, 1);
            bool snap = !smoothingStarted;
            if (!smoothingStarted) { leftPole = leftTarget; rightPole = rightTarget; smoothedYaw = opponent.BodyYaw; smoothingStarted = true; }
            float step = Mathf.Max(0, dt);
            float poleBlend = 1 - Mathf.Exp(-step / .08f);
            leftPole = Vector3.Lerp(leftPole, leftTarget, poleBlend); rightPole = Vector3.Lerp(rightPole, rightTarget, poleBlend);
            smoothedYaw = Mathf.Lerp(smoothedYaw, opponent.BodyYaw, 1 - Mathf.Exp(-step / .05f));
            float angle = (BodyReaction ? -10 : 6) * Reaction;
            spine.rotation = Quaternion.Euler(angle, 12 + smoothedYaw * .45f, 0) * spine.rotation;
            chest.rotation = Quaternion.Euler(angle * .5f, smoothedYaw * .55f, 0) * chest.rotation;
            head.rotation = Quaternion.AngleAxis(-12 - smoothedYaw,Vector3.up) * head.rotation;
            head.rotation = Quaternion.AngleAxis((BodyReaction ? -8 : 14) * Reaction, Vector3.right) * head.rotation;
            // Keep the head's rendered centre on its hit sphere while bending the body beneath it.
            transform.position += opponent.Head - HeadCenter;
            Solve(leftThigh, leftShin, leftFoot, lf, Vector3.back); leftFoot.rotation = lr;
            Solve(rightThigh, rightShin, rightFoot, rf, Vector3.back); rightFoot.rotation = rr;
            Arm(leftUpper, leftFore, leftHand, leftCenter, opponent.Left, opponent.Head, leftPole, -1, ref leftTwist, ref leftBend, ref leftForearm, snap, step);
            Arm(rightUpper, rightFore, rightHand, rightCenter, opponent.Right, opponent.Head, rightPole, 1, ref rightTwist, ref rightBend, ref rightForearm, snap, step);
            Reaction = Mathf.Max(0, Reaction - Mathf.Max(0, dt) * 2.8f);
        }
        // Elbow direction: horizontal for a hook, down for the guards (a head hook guard's covering elbow forward under
        // the glove, a body hook guard's dropped onto the flank).
        private static Vector3 Pole(BoxingOpponent opponent, bool left, float side)
        {
            if (opponent.Hook && opponent.AttackLeft == left) return new Vector3(side, .05f, .1f);
            if (opponent.Guarding)
                switch (opponent.Guard)
                {
                    case BoxingGuard.BodyStraight: return new Vector3(side * .15f, -1, -.05f);
                    // The glove at the temple is nearly above the shoulder: the elbow comes forward under it, not out.
                    case BoxingGuard.HeadHook: if (side == opponent.GuardSide) return new Vector3(side * .05f, -.25f, -1); break;
                    case BoxingGuard.BodyHook: if (side == opponent.GuardSide) return new Vector3(side * .45f, -1, .9f); break;
                }
            return new Vector3(side * .3f, -1, .1f);
        }
        // The back of the glove (local Z) for a forearm direction (local Y, cuff -> knuckles): the back that faces the
        // player (-Z) on an upright forearm, carried to the forearm by the shortest rotation, then twisted about the forearm.
        // Continuous in the direction; its only singular direction is a forearm hanging straight down, which no guard or
        // strike uses. The former reference (-Z projected on the forearm's normal plane, right as the fallback) flipped
        // whenever the forearm pointed near the player, as in the body guards and at the end of a strike.
        internal static Vector3 GloveBack(Vector3 direction, float twist) =>
            Quaternion.AngleAxis(twist, direction) * (Quaternion.FromToRotation(Vector3.up, direction) * Vector3.back);
        // Twist for a forearm pointing at the player: a quarter turn inwards (vertical fist, palm towards the centre line)
        // as the forearm comes down from upright to the player, none on an upright forearm.
        internal static float GloveTwist(Vector3 direction, float side) =>
            -side * 90 * Mathf.InverseLerp(.4f, .95f, Vector3.Dot(direction, Vector3.back));
        private void Arm(Transform upper, Transform fore, Transform hand, Vector3 center, Vector3 target, Vector3 targetHead, Vector3 pole, float side,
            ref float twist, ref Vector3 bendState, ref Vector3 forearm, bool snap, float dt)
        {
            Vector3 previousBend = snap ? Vector3.zero : bendState, used = previousBend;
            float maxTurn = BendRate * dt;
            float extension = Mathf.Clamp01((targetHead.z - target.z - .28f) / .35f);
            Vector3 direction = snap || forearm.sqrMagnitude < .5f ? Vector3.Lerp(Vector3.up, Vector3.back, extension).normalized : forearm;
            // Explicit two-axis wrist frame: local Y runs cuff -> knuckles, local Z
            // is the back of the glove. After a reset iterate towards the forearm to avoid bent wrists.
            for (int pass = snap ? 0 : 1; pass < 2; pass++)
            {
                for (int i=0; i<(snap ? 5 : 1); i++)
                {
                    Quaternion rotation = Quaternion.LookRotation(GloveBack(direction, twist),direction);
                    Vector3 offset = rotation * Vector3.Scale(center, hand.lossyScale);
                    used = SolveArm(upper, fore, hand, target - offset, pole, previousBend, maxTurn);
                    hand.rotation = rotation;
                    direction = (hand.position-fore.position).normalized;
                }
                // After a reset the twist starts at its target (a second solve); otherwise it eases towards it for the next frame.
                float goal = GloveTwist(direction, side);
                twist = pass == 0 ? goal : Mathf.MoveTowards(twist, goal, TwistRate * dt);
            }
            bendState = used; forearm = direction;
        }
        // The torso as an elliptic column from the belly to the shoulders (metres at scale 1), widened by the arm's thickness.
        private const float TorsoHalfWidth = .15f, TorsoHalfDepth = .11f, ArmClearance = .045f;
        // Signed clearance of a point from that column: negative inside.
        private float TorsoClearance(Vector3 point, float shoulderHeight)
        {
            float scale = transform.localScale.x;
            if (point.y < spine.position.y - .2f * scale || point.y > shoulderHeight) return 1;
            Vector3 axis = Vector3.Lerp(spine.position, chest.position, Mathf.InverseLerp(spine.position.y, chest.position.y, point.y));
            // Spine + chest turn the torso by 12 + BodyYaw degrees (Render); the column is symmetric under the root's half turn.
            Vector3 local = Quaternion.Euler(0, -(12 + smoothedYaw), 0) * (point - axis);
            float x = local.x / ((TorsoHalfWidth + ArmClearance) * scale), z = local.z / ((TorsoHalfDepth + ArmClearance) * scale);
            return Mathf.Sqrt(x * x + z * z) - 1;
        }
        private float ArmClearanceScore(Vector3 elbow, Vector3 wrist, float shoulderHeight) =>
            Mathf.Min(TorsoClearance(elbow, shoulderHeight),
                Mathf.Min(TorsoClearance(Vector3.Lerp(elbow, wrist, .35f), shoulderHeight), TorsoClearance(Vector3.Lerp(elbow, wrist, .7f), shoulderHeight)));
        // Two-bone arm IK whose elbow follows the pole unless the elbow or forearm would sink into the torso;
        // then the elbow turns about the shoulder-wrist axis to the nearest clear direction (guards tuck the arms close).
        // The bend turns from the previous frame's (previous, zero = none) by at most maxTurn degrees. Returns the bend used.
        private Vector3 SolveArm(Transform upper, Transform lower, Transform end, Vector3 target, Vector3 pole, Vector3 previous, float maxTurn)
        {
            Vector3 start = upper.position, delta = target - start;
            float distance = Mathf.Max(.0001f, delta.magnitude);
            Vector3 direction = delta / distance;
            float a = Vector3.Distance(start, lower.position), b = Vector3.Distance(lower.position, end.position);
            float stretch = Mathf.Max(1, distance / (a + b) * 1.001f); a *= stretch; b *= stretch;
            float along = Mathf.Clamp((a*a - b*b + distance*distance) / (2*distance), -a, a);
            Vector3 bend = Vector3.ProjectOnPlane(pole, direction).normalized;
            if (bend.sqrMagnitude < .001f) bend = Vector3.Cross(direction, Vector3.right).normalized;
            float reach = Mathf.Sqrt(Mathf.Max(0, a*a - along*along));
            Vector3 centre = start + direction * along;
            float original = ArmClearanceScore(centre + bend * reach, target, start.y), bestScore = original;
            Vector3 best = bend;
            bool clear = original >= 0;
            for (int step = 1; step <= 12 && !clear; step++)
                for (int sign = 1; sign >= -1 && !clear; sign -= 2)
                {
                    Vector3 candidate = Quaternion.AngleAxis(sign * step * 12f, direction) * bend;
                    float score = ArmClearanceScore(centre + candidate * reach, target, start.y);
                    if (score >= 0) { best = candidate; clear = true; }
                    else if (score > bestScore) { best = candidate; bestScore = score; }
                }
            // Without a clear direction keep the pole unless turning helps noticeably (no flipping between poor choices).
            if (!clear && bestScore < original + .1f) best = bend;
            Vector3 last = Vector3.ProjectOnPlane(previous, direction);
            if (last.sqrMagnitude > 1e-6f) best = Vector3.RotateTowards(last.normalized, best, maxTurn * Mathf.Deg2Rad, 0);
            Vector3 elbow = centre + best * reach;
            upper.rotation = Quaternion.FromToRotation(lower.position - start, elbow - start) * upper.rotation;
            lower.position = elbow;
            lower.rotation = Quaternion.FromToRotation(end.position - lower.position, target - lower.position) * lower.rotation;
            end.position = target;
            return best;
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
