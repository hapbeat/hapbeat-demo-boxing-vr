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
        private void OnEnable() { if (input != null) input.Recentered += RepositionAfterRecenter; }
        private void OnDisable() { if (input != null) input.Recentered -= RepositionAfterRecenter; HideRays(); }
        private void RepositionAfterRecenter() { if (IsOpen) Open(); }
        private void Start() { input.Recentered -= RepositionAfterRecenter; input.Recentered += RepositionAfterRecenter; Open(); }
        public void Open()
        {
            IsOpen = true; Selection = 0;
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
        public void Close() { IsOpen = false; panel.gameObject.SetActive(false); HideRays(); game.ResetHistory(); }
        private void Update()
        {
            if (input.MenuPressed) { if (IsOpen && game.Round.Phase != BoxingPhase.Ready && game.Round.Phase != BoxingPhase.Results) Close(); else Open(); }
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
            switch (index)
            {
                case 0:
                    if (game.Round.Phase == BoxingPhase.Ready || game.Round.Phase == BoxingPhase.Results) game.StartRound(); else Close();
                    break;
                case 1: game.StartRound(); break;
                case 2: game.UseHandTracking(); break;
                case 3: game.tuning.impactMode = game.tuning.impactMode == ImpactMode.Continuous ? ImpactMode.WeakHard : ImpactMode.Continuous; break;
                case 4: game.RecenterPlayer(); Open(); break;
                case 5: game.feedback.hapticsEnabled = !game.feedback.hapticsEnabled; if (!game.feedback.hapticsEnabled) game.feedback.StopFeedback(); break;
                case 6: game.feedback.soundEnabled = !game.feedback.soundEnabled; if (!game.feedback.soundEnabled) { game.feedback.audioSource.Stop(); game.feedback.bellSource.Stop(); if(game.feedback.voiceSource!=null) game.feedback.voiceSource.Stop(); } break;
            }
        }
        private void Refresh()
        {
            title.text = game.Round.Phase == BoxingPhase.Results ?
                (game.Round.PlayerHealth <= 0 ? "KO - OPPONENT WINS" : game.Round.EnemyHealth <= 0 ? "KO - YOU WIN" : "ROUND COMPLETE") + "\n" + game.Round.Score + " POINTS" : "HAPBEAT\nBOXING";
            string[] labels = {game.Round.Phase == BoxingPhase.Ready || game.Round.Phase == BoxingPhase.Results ? "START " + game.tuning.roundSeconds.ToString("0") + "s ROUND" : "RESUME", "RESTART ROUND",
                "INPUT: " + input.ActiveMode + " (HANDS FIRST)", "IMPACT: " + game.tuning.impactMode, "RECENTER", "HAPTICS: " + (game.feedback.hapticsEnabled ? "ON" : "OFF"), "SOUND: " + (game.feedback.soundEnabled ? "ON" : "OFF")};
            for (int i = 0; i < rows.Length; i++)
            {
                rows[i].text = (i == Selection ? ">  " : "   ") + labels[i];
                rows[i].color = i == Selection ? new Color(0.25f, 0.95f, 1) : new Color(0.8f, 0.85f, 0.92f);
            }
            hint.text = (UsesHandPointer ? "AIM YOUR HAND - PINCH THUMB AND INDEX TO SELECT\nLEFT OPEN PALM TOWARD YOUR FACE: HOLD 0.8s FOR MENU" :
                "EITHER STICK: SELECT   A / X: CONFIRM\nMENU / B / Y: PAUSE") + "\nCLEAR YOUR PLAY AREA - DO NOT HIT REAL OBJECTS";
        }
    }
}
