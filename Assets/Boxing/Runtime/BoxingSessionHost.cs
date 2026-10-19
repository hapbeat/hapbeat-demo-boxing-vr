using System.Collections.Generic;
using Hapbeat.DemoSwitch;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Hapbeat.Boxing
{
    /// <summary>
    /// Demo Session adapter. `round` sets the round length; in session mode the result ends in the
    /// shared completion panel instead of the next round starting by itself, and retry is RESTART ROUND.
    /// The shared pause (settings Pause Menu) and its CONTROL fallbacks `menu_open` / `menu_close` / `restart`
    /// reach the game through <see cref="SetGameplayPaused"/> and <see cref="Restart"/>; Boxing has no IDemoAppControls.
    /// 視線をリセット (button and CONTROL `recenter`) is the menu's recenter (<see cref="BoxingGame.RecenterPlayer"/>).
    /// </summary>
    public sealed class BoxingSessionHost : MonoBehaviour, IDemoSessionHost, IDemoSessionRecenter
    {
        private BoxingGame game;
        private bool completionWired;

        // Added at runtime so the checked-in scene stays unchanged.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            SceneManager.sceneLoaded -= Attach;
            SceneManager.sceneLoaded += Attach;
        }

        private static void Attach(Scene scene, LoadSceneMode mode)
        {
            var found = FindAnyObjectByType<BoxingGame>();
            if (found != null && found.GetComponent<BoxingSessionHost>() == null) found.gameObject.AddComponent<BoxingSessionHost>();
        }

        private void Start()
        {
            game = GetComponent<BoxingGame>();
            DemoSession.RegisterHost(this);
        }

        private void OnDestroy()
        {
            DemoSession.UnregisterHost(this);
            if (game != null) game.ResultsCompleted -= OnResultsCompleted;
            // A scene root of the same scene; unloading the scene (EditMode tests) removes it anyway.
            if (panelHands != null && Application.isPlaying) Destroy(panelHands);
        }

        public void ApplyOptions(IReadOnlyDictionary<string, string> options)
        {
            if (game == null) game = GetComponent<BoxingGame>();
            if (options.TryGetValue("round", out var round) && int.TryParse(round, out var seconds) && seconds >= 10)
                game.tuning.roundSeconds = seconds;
            // haptics_db (scope device) is read like outside a session, through DemoSession.GetOption.
            game.ApplyHapticsTrim();
            game.HoldResultsForSession = true;
            if (!completionWired) game.ResultsCompleted += OnResultsCompleted;
            completionWired = true;
        }

        private void OnResultsCompleted() => DemoSession.ShowCompletion();

        public void Restart() => game.StartRound();

        public void SetHapticsEnabled(bool enabled)
        {
            if (game == null) game = GetComponent<BoxingGame>();
            game.feedback.SetHapticsEnabled(enabled);
        }

        // Round, opponent and impacts stop (BoxingGame.Simulate); the developer menu never stays open under a shared panel.
        // The shared panels are pressed with a fingertip: the Demo Hub hands replace the gloves while one is shown.
        public void SetGameplayPaused(bool paused)
        {
            if (game == null) game = GetComponent<BoxingGame>();
            game.InputSuspended = paused;
            if (paused && game.menu.IsOpen) game.menu.Close();
            SetPanelHands(paused);
        }

        private GameObject panelHands;

        private void SetPanelHands(bool visible)
        {
            if (visible && panelHands == null)
            {
                panelHands = new GameObject("Panel hands (DemoHands)");
                panelHands.SetActive(false);
                panelHands.AddComponent<DemoHands>().SetStyle(DemoHands.ResolveStyle(DemoSession.Ticket, DemoHandStyle.Ghost));
            }
            if (panelHands != null) panelHands.SetActive(visible);
        }

        public void RecenterToStart()
        {
            if (game == null) game = GetComponent<BoxingGame>();
            game.RecenterPlayer();
        }
    }
}
