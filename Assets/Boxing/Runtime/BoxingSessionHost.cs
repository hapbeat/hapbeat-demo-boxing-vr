using System.Collections.Generic;
using Hapbeat.DemoSwitch;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Hapbeat.Boxing
{
    /// <summary>
    /// Demo Session adapter. `round` sets the round length; in session mode the result ends in the
    /// shared completion panel instead of the round menu, and retry is RESTART ROUND.
    /// </summary>
    public sealed class BoxingSessionHost : MonoBehaviour, IDemoSessionHost
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
        }

        public void ApplyOptions(IReadOnlyDictionary<string, string> options)
        {
            if (game == null) game = GetComponent<BoxingGame>();
            if (options.TryGetValue("round", out var round) && int.TryParse(round, out var seconds) && seconds >= 10)
                game.tuning.roundSeconds = seconds;
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

        public void SetGameplayPaused(bool paused) => game.InputSuspended = paused;
    }
}
