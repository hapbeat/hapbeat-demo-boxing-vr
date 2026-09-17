using UnityEngine;
using UnityEngine.UI;

namespace Hapbeat.Boxing
{
    public sealed class BoxingPresentation : MonoBehaviour
    {
        public Transform leftGlove, rightGlove;
        public BoxingOpponentAvatar enemyAvatar;
        public Text timerText, scoreText, cueText, statusText, impactText;
        public Transform hitBurst;
        public Image PlayerHealthBar { get; private set; }
        public Image EnemyHealthBar { get; private set; }
        private Text playerHealthText, enemyHealthText;
        private float flashTime;
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
        public void Render(BoxingGame game, BoxerPose pose, bool valid)
        {
            bool menuOpen = game.menu != null && game.menu.IsOpen;
            timerText.transform.parent.gameObject.SetActive(!menuOpen);
            leftGlove.gameObject.SetActive(pose.valid && !menuOpen); rightGlove.gameObject.SetActive(pose.valid && !menuOpen);
            enemyAvatar.gameObject.SetActive(!menuOpen);
            if (pose.valid)
            {
                leftGlove.SetPositionAndRotation(pose.left, pose.leftRotation);
                rightGlove.SetPositionAndRotation(pose.right, pose.rightRotation);
            }
            enemyAvatar.Render(game.Opponent, !game.Paused && valid ? Time.unscaledDeltaTime : 0);
            timerText.text = game.Round.Phase == BoxingPhase.Countdown ? Mathf.CeilToInt(game.Round.Countdown).ToString() :
                game.Round.Phase == BoxingPhase.Ready ? game.tuning.roundSeconds.ToString("0") + " SECOND ROUND" : Mathf.CeilToInt(game.Round.TimeLeft).ToString("00") + "s";
            scoreText.text = "SCORE " + game.Round.Score + "     HIT " + game.Round.Hits + "     BLOCK " + game.Round.Blocks + "     DODGE " + game.Round.Dodges;
            RenderHealth(game.Round);
            statusText.text = game.input.ActiveMode + "  |  " + (game.feedback.CanSend ? "HAPBEAT " + (Hapbeat.HapbeatManager.Instance != null ? Hapbeat.HapbeatManager.Instance.AliveDeviceCount : 0) + " DEVICE(S)" : "HAPTICS OFF") +
                "  |  " + (game.Round.Phase == BoxingPhase.Results ? (game.Round.PlayerHealth <= 0 ? "KO - OPPONENT WINS" : game.Round.EnemyHealth <= 0 ? "KO - YOU WIN" : "ROUND COMPLETE") : game.Paused ? game.PauseReason : game.tuning.impactMode.ToString());
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
