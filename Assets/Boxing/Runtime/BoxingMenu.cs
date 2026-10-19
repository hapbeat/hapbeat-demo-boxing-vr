using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace Hapbeat.Boxing
{
    [DefaultExecutionOrder(-100)]
    public sealed class BoxingMenu : MonoBehaviour
    {
        public BoxingGame game;
        public BoxingInput input;
        public Transform panel;
        public Text title, hint;
        public Text[] rows;
        public bool IsOpen { get; private set; }
        public int Selection { get; private set; }
        public bool UsesHandPointer => input.ActiveMode == BoxingInputMode.Hands;
        private bool navigationReady = true;
        private LineRenderer leftRay, rightRay;
        private Material rayMaterial;
        private bool leftReady, rightReady;
        // HAPTIC TEST page (the last main row): one cue clip sent the way an impact sends it, without a punch or a sound.
        public bool TestPage { get; private set; }
        public int TestClip { get; private set; }
        public bool TestRightWrist { get; private set; }
        public bool TestKit { get; private set; }
        // Stream lead: the impact's own (GAME), none (Studio's audition starts its stream at the clip), 60 ms.
        public static readonly string[] TestLeads = { "GAME", "0 ms", "60 ms" };
        public int TestLead { get; private set; }
        public double TestLeadSeconds => TestLead == 0 ? BoxingFeedback.GameHapticLead() : TestLead == 1 ? 0 : 0.06;
        public float LastTestPeak { get; private set; }
        public const int TestRepeats = 5;
        public const float TestRepeatSeconds = 0.6f;
        // The SDK's stream send-ahead (HapbeatConfig.streamSendAheadSeconds) for the whole app, impacts too, until it
        // quits: the configured one or ShortSendAhead. A fresh stream sends BEGIN, two repeats and the send-ahead's blocks
        // at once; past 6 datagrams the device's UDP queue drops the rest (one read per 5 ms loop, lwIP mailbox 6), and
        // at 50 ms that is a short clip's onset. 20 ms sends 4.
        public const float ShortSendAhead = 0.02f;
        public float SendAheadSeconds => SdkConfig != null ? SdkConfig.streamSendAheadSeconds : 0;
        private HapbeatConfig sdkConfig;
        private float configuredSendAhead = -1;
        private HapbeatConfig SdkConfig => sdkConfig != null ? sdkConfig : (sdkConfig = Resources.Load<HapbeatConfig>("HapbeatConfig"));
        private int fontSize;
        private BoxingFeedback.HapticTestClip[] testClips;
        private BoxingFeedback.HapticTestClip[] TestClips => testClips ?? (testClips = game.feedback.HapticTestClips());
        private void OnEnable() { if (input != null) input.Recentered += RepositionAfterRecenter; }
        private void OnDisable()
        {
            if (input != null) input.Recentered -= RepositionAfterRecenter; HideRays();
            // The config is an asset: never leave the test value in it (the Editor would keep it after Play).
            if (configuredSendAhead >= 0 && SdkConfig != null) { SdkConfig.streamSendAheadSeconds = configuredSendAhead; configuredSendAhead = -1; }
        }
        private void RepositionAfterRecenter() { if (IsOpen) Open(); }
        // Developer menu (INPUT / IMPACT / HAPTICS / SOUND): controller B / Y only. The round starts by itself and
        // the left-hand menu (≡ / Quest's left-hand menu gesture) opens the shared Demo Switch pause.
        private void Start() { input.Recentered -= RepositionAfterRecenter; input.Recentered += RepositionAfterRecenter; }
        public void Open()
        {
            IsOpen = true; Selection = 0; TestPage = false;
            leftReady = rightReady = false;
            panel.gameObject.SetActive(true);
            var camera = input.headCamera.transform;
            Vector3 forward = Vector3.ProjectOnPlane(camera.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.1f) forward = Vector3.forward;
            panel.position = camera.position + forward * 0.9f + Vector3.down * 0.03f;
            panel.localScale = Vector3.one * 0.001f;
            panel.rotation = Quaternion.LookRotation(forward);
            game.ResetHistory(); game.feedback.StopFeedback(); Refresh();
        }
        public void Close() { IsOpen = false; TestPage = false; StopAllCoroutines(); panel.gameObject.SetActive(false); HideRays(); game.ResetHistory(); }
        private void Update()
        {
            if (game.InputSuspended) { HideRays(); return; }
            if (input.MenuPressed) { if (IsOpen) Close(); else Open(); }
            if (!IsOpen) return;
            if (Mathf.Abs(input.Navigate) < 0.25f) navigationReady = true;
            if (navigationReady && Mathf.Abs(input.Navigate) > 0.6f)
            {
                Selection = (Selection + (input.Navigate > 0 ? rows.Length - 1 : 1)) % rows.Length;
                navigationReady = false;
            }
            if (input.ConfirmPressed) Activate(Selection);
            if (!IsOpen) return;
            if (UsesHandPointer)
            {
                int left = Point(input.LeftPointer, ref leftRay, ref leftReady, out bool leftClick);
                int right = Point(input.RightPointer, ref rightRay, ref rightReady, out bool rightClick);
                if (right >= 0) Selection = right; else if (left >= 0) Selection = left;
                if (rightClick) Activate(right); else if (leftClick) Activate(left);
            }
            else { HideRays(); leftReady = rightReady = false; }
            Refresh();
        }
        private int Point(BoxingHandPointer pointer, ref LineRenderer line, ref bool ready, out bool clicked)
        {
            clicked = false;
            if (!pointer.Valid) { ready = false; if (line != null) line.enabled = false; return -1; }
            if (!pointer.Pinching) ready = true;
            if (line == null)
            {
                if (rayMaterial == null) rayMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                var go = new GameObject("Hand menu ray"); go.transform.SetParent(transform, false);
                line = go.AddComponent<LineRenderer>(); line.sharedMaterial = rayMaterial;
                line.positionCount = 2; line.startWidth = .002f; line.endWidth = .004f;
                line.useWorldSpace = true; line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            Ray ray = pointer.Ray;
            float distance = 1.5f; int hovered = -1;
            if (new Plane(panel.forward, panel.position).Raycast(ray, out float hit) && hit < 3)
            {
                distance = hit;
                for (int i = 0; i < rows.Length; i++)
                {
                    Vector3 local = rows[i].rectTransform.InverseTransformPoint(ray.GetPoint(hit));
                    if (rows[i].rectTransform.rect.Contains(new Vector2(local.x, local.y))) { hovered = i; break; }
                }
            }
            line.enabled = true; line.SetPosition(0, ray.origin); line.SetPosition(1, ray.GetPoint(distance));
            line.startColor = line.endColor = hovered >= 0 ? Color.cyan : Color.white;
            clicked = ready && pointer.Pressed && hovered >= 0;
            if (pointer.Pinching) ready = false;
            return hovered;
        }
        private void HideRays() { if (leftRay != null) leftRay.enabled = false; if (rightRay != null) rightRay.enabled = false; }
        private void OnDestroy() { if (rayMaterial != null) Destroy(rayMaterial); }
        public void Activate(int index)
        {
            if (TestPage) { ActivateTest(index); return; }
            switch (index)
            {
                case 0:
                    if (game.Round.Phase == BoxingPhase.Ready || game.Round.Phase == BoxingPhase.Results) game.StartRound(); else Close();
                    break;
                case 1: game.StartRound(); break;
                case 2: game.UseHandTracking(); break;
                case 3: game.tuning.impactMode = game.tuning.impactMode == ImpactMode.Continuous ? ImpactMode.WeakHard : ImpactMode.Continuous; break;
                case 4: game.RecenterPlayer(); Open(); break;
                case 5:
                    // Shared state with the Demo Session haptics button; haptics only, sound unchanged.
                    bool haptics = !game.feedback.hapticsEnabled;
                    game.feedback.SetHapticsEnabled(haptics); Hapbeat.DemoSwitch.DemoSession.SetHapticsEnabled(haptics);
                    break;
                case 6: game.feedback.soundEnabled = !game.feedback.soundEnabled; if (!game.feedback.soundEnabled) { game.feedback.audioSource.Stop(); game.feedback.StopImpactSounds(); game.feedback.bellSource.Stop(); if(game.feedback.voiceSource!=null) game.feedback.voiceSource.Stop(); if(game.feedback.musicSource!=null) game.feedback.musicSource.Stop(); } break;
                case 7: TestPage = true; Selection = 0; testClips = game.feedback.HapticTestClips(); break;
            }
        }
        // HAPTIC TEST rows: back, clip, wrist, send, stream lead, send-ahead, play once, play TestRepeats times.
        private void ActivateTest(int index)
        {
            var clips = TestClips;
            switch (index)
            {
                case 0: TestPage = false; StopAllCoroutines(); Selection = 7; break;
                case 1: if (clips.Length > 0) TestClip = (TestClip + 1) % clips.Length; break;
                case 2: TestRightWrist = !TestRightWrist; break;
                case 3: TestKit = !TestKit; break;
                case 4: TestLead = (TestLead + 1) % TestLeads.Length; break;
                case 5:
                    if (SdkConfig == null) break;
                    if (configuredSendAhead < 0) { configuredSendAhead = SdkConfig.streamSendAheadSeconds; SdkConfig.streamSendAheadSeconds = ShortSendAhead; }
                    else { SdkConfig.streamSendAheadSeconds = configuredSendAhead; configuredSendAhead = -1; }
                    break;
                case 6: StopAllCoroutines(); PlayTest(); break;
                case 7: StopAllCoroutines(); StartCoroutine(PlayTests()); break;
            }
        }
        private HapbeatUnityEventTrigger TestTrigger(BoxingFeedback.HapticTestClip[] clips) =>
            clips.Length == 0 ? null : game.feedback.TestTrigger(clips[TestClip % clips.Length], TestRightWrist);
        private void PlayTest() => LastTestPeak = game.feedback.TestHaptic(TestTrigger(TestClips), TestKit, TestLeadSeconds);
        private IEnumerator PlayTests()
        {
            for (int i = 0; i < TestRepeats; i++) { PlayTest(); yield return new WaitForSecondsRealtime(TestRepeatSeconds); }
        }
        private void Refresh()
        {
            if (fontSize == 0) fontSize = rows[0].fontSize;
            foreach (var row in rows) row.fontSize = TestPage ? Mathf.RoundToInt(fontSize * 0.8f) : fontSize;
            if (TestPage) { RefreshTest(); return; }
            title.text = game.Round.Phase == BoxingPhase.Results ?
                (game.Round.PlayerHealth <= 0 ? "KO - OPPONENT WINS" : game.Round.EnemyHealth <= 0 ? "KO - YOU WIN" : "ROUND COMPLETE") + "\n" + game.Round.Score + " POINTS" : "HAPBEAT\nBOXING";
            string[] labels = {game.Round.Phase == BoxingPhase.Ready || game.Round.Phase == BoxingPhase.Results ? "START " + game.tuning.roundSeconds.ToString("0") + "s ROUND" : "RESUME", "RESTART ROUND",
                "INPUT: " + input.ActiveMode + " (HANDS FIRST)", "IMPACT: " + game.tuning.impactMode, "RECENTER", "HAPTICS: " + (game.feedback.hapticsEnabled ? "ON" : "OFF") + "  (" + game.feedback.HapticsTrimDb.ToString("0") + " dB)", "SOUND: " + (game.feedback.soundEnabled ? "ON" : "OFF"),
                "HAPTIC TEST  >"};
            for (int i = 0; i < rows.Length; i++)
            {
                rows[i].text = (i == Selection ? ">  " : "   ") + labels[i];
                rows[i].color = i == Selection ? new Color(0.25f, 0.95f, 1) : new Color(0.8f, 0.85f, 0.92f);
            }
            hint.text = (UsesHandPointer ? "AIM YOUR HAND - PINCH THUMB AND INDEX TO SELECT" :
                "EITHER STICK: SELECT   A / X: CONFIRM\nB / Y: CLOSE") + "\nCLEAR YOUR PLAY AREA - DO NOT HIT REAL OBJECTS";
        }
        private void RefreshTest()
        {
            var clips = TestClips;
            var trigger = TestTrigger(clips);
            var entry = trigger != null ? trigger.ResolveEntry() : null;
            var item = clips.Length > 0 ? clips[TestClip % clips.Length] : default;
            string clip = entry != null && entry.streamClip != null ? entry.streamClip.name : "none";
            float peak = entry != null && entry.streamClip != null ? game.feedback.SentPeak(1, trigger) : 0;
            float clipPeak = entry != null ? game.feedback.ClipPeak(entry.streamClip) : 0;
            title.text = "HAPTIC\nTEST";
            string[] labels = { "<  BACK",
                "CLIP " + (clips.Length == 0 ? 0 : TestClip % clips.Length + 1) + "/" + clips.Length + ": " + clip,
                "CUE " + (clips.Length == 0 ? "-" : BoxingFeedback.Cues[item.cue]) + "  AT " +
                    (clips.Length > 0 && item.cue >= BoxingFeedback.WristCues ? "NECK" : TestRightWrist ? "RIGHT WRIST" : "LEFT WRIST"),
                "SEND: " + (TestKit ? "KIT CLIP ON THE DEVICE" : "STREAM (AS AN IMPACT)"),
                "STREAM LEAD: " + TestLeads[TestLead] + (TestLead == 0 ? " (" + (BoxingFeedback.GameHapticLead() * 1000).ToString("0") + " ms)" : TestLead == 1 ? " (AS STUDIO)" : ""),
                "SEND-AHEAD: " + (SendAheadSeconds * 1000).ToString("0") + " ms" + (configuredSendAhead >= 0 ? " (WHOLE APP, TEST)" : " (CONFIG)"),
                "PLAY ONCE", "PLAY " + TestRepeats + " TIMES, " + TestRepeatSeconds.ToString("0.0") + " s APART" };
            for (int i = 0; i < rows.Length; i++)
            {
                rows[i].text = (i == Selection ? ">  " : "   ") + (i < labels.Length ? labels[i] : "");
                rows[i].color = i == Selection ? new Color(0.25f, 0.95f, 1) : new Color(0.8f, 0.85f, 0.92f);
            }
            hint.text = "PEAK " + peak.ToString("0.00") + " FS = STUDIO INTENSITY " + (clipPeak > 0 ? peak / clipPeak : 0).ToString("0.00") +
                "  (TRIM " + game.feedback.HapticsTrimDb.ToString("0") + " dB)\nA FULL-STRENGTH IMPACT OF THE CUE, NO PUNCH, NO SOUND\n" +
                "KIT: INSTALL " + BoxingFeedback.KitName + " WITH THE CLIP AS A DEVICE (FIRE) CLIP\n" +
                (UsesHandPointer ? "PINCH TO SELECT" : "STICK: SELECT   A / X: CONFIRM   B / Y: CLOSE");
        }
    }
}
