using UnityEngine;
using UnityEngine.UI;

namespace Hapbeat.Boxing
{
    public sealed class BoxingPresentation : MonoBehaviour
    {
        public Transform leftGlove, rightGlove;
        public Transform leftForearm, rightForearm;
        public Transform arena;
        public void CalibrateEnvironment(float floor,float eyeHeight)
        {
            if(arena!=null) { var p=arena.position; p.y=floor; arena.position=p; }
            var board=timerText.transform.parent; var position=board.position;
            position.y=floor+eyeHeight+1.05f; board.position=position;
        }
        public BoxingOpponentAvatar enemyAvatar;
        public Text timerText, scoreText, cueText, statusText, impactText;
        public Transform hitBurst;
        public Image PlayerHealthBar { get; private set; }
        public Image EnemyHealthBar { get; private set; }
        private Text playerHealthText, enemyHealthText;
        private float flashTime;
        // The tutorial's Japanese board text: the Demo Session's bundled Noto Sans CJK JP (com.hapbeat.demo-switch
        // Resources, DemoSessionFont.ResourcePath). The built-in LegacyRuntime font has no Japanese glyphs on Quest.
        // Its CJK line is taller than the Latin one: 48 keeps each line inside the 85-unit board box (a line cut by the box shows nothing).
        public const string JapaneseFontPath = "HapbeatDemoSession/NotoSansCJKjp-Regular";
        private const int JapaneseFontSize = 48;
        private Font boardFont, japaneseFont;
        private int boardFontSize;
        // Tutorial target (BoxingGame.TutorialTarget): one unlit loop line in the contact ring's material, facing the player.
        // Drawing only: contacts use the opponent's collision shapes, never this.
        public LineRenderer TutorialMarker { get; private set; }
        private const float MarkerRadius = .07f;
        private string lastImpact = "";
        public void Flash(BoxingImpact impact)
        {
            flashTime = 0.28f;
            lastImpact = (impact.surface == ImpactSurface.Glove ? (impact.attack ? "GUARDED" : "BLOCK") : impact.attack ? "HIT" : "HEAD HIT") +
                "  " + (impact.hard ? "HARD" : "SOFT") + (impact.attack ? "  " + (impact.surface == ImpactSurface.Body ? impact.damage : 0).ToString("0.0") + " DMG" : "");
            if (hitBurst != null) { hitBurst.position = impact.point; hitBurst.gameObject.SetActive(true); }
            if (hitBurst != null)
            {
                var lines = hitBurst.GetComponent<LineRenderer>();
                if (lines != null) lines.startColor = lines.endColor = impact.surface == ImpactSurface.Glove ? new Color(0.4f, 0.85f, 1) : new Color(1, 0.65f, 0.25f);
            }
        }
        public void Render(BoxingGame game, BoxerPose pose, bool valid, float deltaTime=0)
        {
            bool menuOpen = game.menu != null && game.menu.IsOpen;
            // Under a shared panel the tracked hands draw the Demo Hub hands (BoxingSessionHost) instead of the gloves.
            bool panelHands = game.InputSuspended && game.input.ActiveMode == BoxingInputMode.Hands;
            bool gloves = (pose.valid || pose.visualValid) && !menuOpen && !panelHands;
            timerText.transform.parent.gameObject.SetActive(!menuOpen);
            // A hand lost within the tracking grace is hidden instead of drawn frozen.
            bool left = gloves && game.LeftHandActive, right = gloves && game.RightHandActive;
            leftGlove.gameObject.SetActive(left); rightGlove.gameObject.SetActive(right);
            RenderForearm(leftForearm,pose.left,pose.leftRotation,left);
            RenderForearm(rightForearm,pose.right,pose.rightRotation,right);
            enemyAvatar.gameObject.SetActive(!menuOpen);
            if (pose.valid || pose.visualValid)
            {
                leftGlove.SetPositionAndRotation(pose.left, pose.leftRotation);
                rightGlove.SetPositionAndRotation(pose.right, pose.rightRotation);
            }
            enemyAvatar.Render(game.Opponent, !game.Paused && valid ? deltaTime : 0);
            string result = (game.Round.PlayerHealth<=0 || game.Round.EnemyHealth<=0 ? "KNOCKOUT" : "ROUND COMPLETE") + " - " +
                (game.Round.Outcome==BoxingOutcome.Win ? "YOU WIN" : game.Round.Outcome==BoxingOutcome.Lose ? "YOU LOSE" : "DRAW");
            if (boardFont == null) { boardFont = timerText.font; boardFontSize = timerText.fontSize; }
            bool tutorial = game.Round.Phase == BoxingPhase.Tutorial;
            if (tutorial && japaneseFont == null) { japaneseFont = Resources.Load<Font>(JapaneseFontPath); if (japaneseFont == null) japaneseFont = boardFont; }
            timerText.font = tutorial ? japaneseFont : boardFont; timerText.fontSize = tutorial ? JapaneseFontSize : boardFontSize;
            RenderTutorialMarker(game, menuOpen);
            timerText.text = game.TrackingWarning && game.Round.Phase != BoxingPhase.Results ?
                (game.input.ActiveMode == BoxingInputMode.Hands ? "HAND TRACKING LOST" : "TRACKING LOST") :
                game.Round.Phase == BoxingPhase.Results ? result :
                tutorial ? game.TutorialText :
                game.Round.Phase == BoxingPhase.Countdown ? Mathf.CeilToInt(game.Round.Countdown).ToString() :
                game.Round.Phase == BoxingPhase.Ready ? game.tuning.roundSeconds.ToString("0") + " SECOND ROUND" : Mathf.CeilToInt(game.Round.TimeLeft).ToString("00") + "s";
            scoreText.text = "SCORE " + game.Round.Score + "     HIT " + game.Round.Hits + "     BLOCK " + game.Round.Blocks + "     DODGE " + game.Round.Dodges;
            RenderHealth(game.Round);
            statusText.text = game.input.ActiveMode + "  |  " + (game.feedback.CanSend ? "HAPBEAT " + (Hapbeat.HapbeatManager.Instance != null ? Hapbeat.HapbeatManager.Instance.AliveDeviceCount : 0) + " DEVICE(S)" : "HAPTICS OFF") +
                "  |  " + (game.Round.Phase == BoxingPhase.Results ? result : game.Paused ? game.PauseReason : game.tuning.impactMode.ToString()) +
                // Size probe (TODO with tuning.opponentSizeScale).
                "  |  EYE " + game.SizeEyeHeight.ToString("0.00") + "m (now " + game.input.HeadAboveFloor.ToString("0.00") + "m)  OPP " + game.SizeOpponentStature.ToString("0.00") + "m";
            flashTime = Mathf.Max(0, flashTime - Time.unscaledDeltaTime);
            impactText.text = flashTime > 0 ? lastImpact : "";
            if (hitBurst != null)
            {
                hitBurst.gameObject.SetActive(flashTime > 0);
                hitBurst.localScale = Vector3.one * (0.035f + (0.28f - flashTime) * 0.32f);
                hitBurst.rotation = Quaternion.LookRotation(game.input.headCamera.transform.forward);
                var lines = hitBurst.GetComponent<LineRenderer>();
                if (lines != null) { var c = lines.startColor; c.a = flashTime / 0.28f; lines.startColor = lines.endColor = c; }
            }
        }
        public void RenderHealth(BoxingRound round)
        {
            cueText.enabled = false;
            if (PlayerHealthBar == null)
            {
                PlayerHealthBar = CreateHealthBar("YOU", -285, new Color(0.15f, 0.85f, 1), out playerHealthText);
                EnemyHealthBar = CreateHealthBar("OPPONENT", 285, new Color(1, 0.25f, 0.15f), out enemyHealthText);
            }
            PlayerHealthBar.rectTransform.anchorMax = new Vector2(round.PlayerHealth / round.MaximumHealth, 1);
            EnemyHealthBar.rectTransform.anchorMax = new Vector2(round.EnemyHealth / round.MaximumHealth, 1);
            playerHealthText.text = "YOU  " + Mathf.CeilToInt(round.PlayerHealth) + " / " + Mathf.CeilToInt(round.MaximumHealth);
            enemyHealthText.text = "OPPONENT  " + Mathf.CeilToInt(round.EnemyHealth) + " / " + Mathf.CeilToInt(round.MaximumHealth);
        }
        private void RenderTutorialMarker(BoxingGame game, bool menuOpen)
        {
            bool visible = game.TutorialTarget(out var position) && !menuOpen;
            if (TutorialMarker == null)
            {
                if (!visible || hitBurst == null) return;
                TutorialMarker = new GameObject("Tutorial target", typeof(LineRenderer)).GetComponent<LineRenderer>();
                var ring = hitBurst.GetComponent<LineRenderer>();
                if (ring != null) TutorialMarker.sharedMaterial = ring.sharedMaterial;
                TutorialMarker.useWorldSpace = false; TutorialMarker.loop = true; TutorialMarker.widthMultiplier = .012f;
                TutorialMarker.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; TutorialMarker.receiveShadows = false;
                TutorialMarker.startColor = TutorialMarker.endColor = new Color(1, .85f, .2f, .75f);
                TutorialMarker.positionCount = 32;
                for (int i = 0; i < 32; i++) { float a = i * Mathf.PI * 2 / 32; TutorialMarker.SetPosition(i, new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0) * MarkerRadius); }
            }
            TutorialMarker.gameObject.SetActive(visible);
            // The opponent faces -Z (towards the player): the ring's plane faces the player.
            if (visible) TutorialMarker.transform.SetPositionAndRotation(position, Quaternion.identity);
        }
        private static void RenderForearm(Transform arm,Vector3 hand,Quaternion rotation,bool visible)
        {
            if(arm==null) return; // Scene generation renders before installing art.
            arm.gameObject.SetActive(visible);
            arm.SetPositionAndRotation(hand+rotation*Vector3.back*(BoxingForearm.WristOffset+BoxingForearm.Length*.5f),
                rotation*Quaternion.FromToRotation(Vector3.up,Vector3.forward));
            arm.localScale=Vector3.one;
        }
        private Image CreateHealthBar(string name, float x, Color color, out Text label)
        {
            var root = new GameObject(name + " HP", typeof(RectTransform)).GetComponent<RectTransform>();
            root.SetParent(cueText.transform, false); root.anchoredPosition = new Vector2(x, 0); root.sizeDelta = new Vector2(520, 45);
            label = new GameObject("Label", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
            label.transform.SetParent(root, false); label.font = cueText.font; label.fontSize = 20;
            label.alignment = TextAnchor.MiddleCenter; label.color = Color.white; label.raycastTarget = false;
            label.rectTransform.sizeDelta = new Vector2(520, 24); label.rectTransform.anchoredPosition = new Vector2(0, 12);
            var background = new GameObject("Track", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            background.transform.SetParent(root, false); background.color = new Color(0.08f, 0.1f, 0.14f); background.raycastTarget = false;
            background.rectTransform.sizeDelta = new Vector2(520, 16); background.rectTransform.anchoredPosition = new Vector2(0, -12);
            var fill = new GameObject("Health", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            fill.transform.SetParent(background.transform, false); fill.color = color; fill.raycastTarget = false;
            fill.rectTransform.anchorMin = Vector2.zero; fill.rectTransform.anchorMax = Vector2.one;
            fill.rectTransform.offsetMin = fill.rectTransform.offsetMax = Vector2.zero;
            return fill;
        }
        public static void Segment(Transform segment, Vector3 a, Vector3 b, float radius)
        {
            var delta = b - a;
            segment.position = (a + b) * 0.5f;
            segment.rotation = Quaternion.FromToRotation(Vector3.up, delta.normalized);
            segment.localScale = new Vector3(radius * 2, delta.magnitude * 0.5f, radius * 2);
        }
    }
}
