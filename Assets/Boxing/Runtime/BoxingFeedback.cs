using System;
using Hapbeat;
using Hapbeat.DemoSwitch;
using UnityEngine;

namespace Hapbeat.Boxing
{
    public enum CueSoundPick { Random, RoundRobin }
    // One cue's sounds (cue table sfx.sound / sfx.sounds) with their volumes (sfx volume x sound intensity)
    // and how one is picked per impact (variation.pick).
    [Serializable]
    public sealed class CueSoundSet
    {
        public AudioClip[] clips = new AudioClip[0];
        public float[] volumes = new float[0];
        public CueSoundPick pick;
        private int last = -1;
        // The index of the sound to play, -1 = none. Random never repeats the previous sound; RoundRobin goes in order.
        public int Next()
        {
            int count = clips == null ? 0 : clips.Length;
            if (count == 0) return -1;
            if (count == 1) last = 0;
            else if (pick == CueSoundPick.RoundRobin) last = (last + 1) % count;
            else if (last < 0) last = UnityEngine.Random.Range(0, count);
            else { int next = UnityEngine.Random.Range(0, count - 1); last = next >= last ? next + 1 : next; }
            return last;
        }
    }
    [DefaultExecutionOrder(-10000)]
    public sealed class BoxingFeedback : MonoBehaviour
    {
        public GameObject sdkRoot;
        // Haptic / sound cues (Assets/Boxing/Haptics/boxing-cues.json): where the contact was x soft/hard.
        // The first eight play at a wrist (the punching or the guarding hand), hit_body at the neck.
        public static readonly string[] Cues = {
            "punch_glove_soft", "punch_glove_hard", "punch_body_soft", "punch_body_hard",
            "guard_glove_soft", "guard_glove_hard", "guard_arm_soft", "guard_arm_hard", "hit_body_soft", "hit_body_hard" };
        public const int WristCues = 8;
        // left_<cue> x 8, right_<cue> x 8, then the neck's hit_body_soft / hit_body_hard (TriggerIndex).
        public HapbeatUnityEventTrigger[] impactTriggers = new HapbeatUnityEventTrigger[2 * WristCues + 2];
        public AudioSource audioSource, bellSource, voiceSource;
        // Kenney voice OGGs normalized to -20 LUFS (tools/tts/loudness.py), played at the Hub's narration volume.
        public AudioClip[] countdownVoice = new AudioClip[3];
        public AudioClip winVoice, loseVoice, tieVoice;
        public int ResultVoiceCues { get; private set; }
        public BoxingOutcome LastSpokenOutcome { get; private set; }
        public int LastSpokenNumber { get; private set; }
        public int VoiceCues { get; private set; }
        public float ImpactTailSeconds { get; private set; }
        // Per cue (Cues order): its sounds (none = no sound), from the cue table.
        public CueSoundSet[] cueSounds = new CueSoundSet[10];
        public AudioClip LastSound { get; private set; }
        public AudioClip bell;
        public bool soundEnabled = true;
        public bool hapticsEnabled = true;
        public bool forceSilent;
        public int Reports { get; private set; }
        public int Sends { get; private set; }
        public int Rings { get; private set; }
        public int ImpactStops { get; private set; }
        public BoxingImpact LastImpact { get; private set; }
        public event Action<BoxingImpact> Reported;
        public bool CanSend => !Application.isBatchMode && !forceSilent && hapticsEnabled;
        private void Awake()
        {
            if (Application.isBatchMode || forceSilent)
            {
                if (sdkRoot != null) sdkRoot.SetActive(false);
                if (audioSource != null) audioSource.mute = true;
                if (bellSource != null) bellSource.mute = true;
                if (voiceSource != null) voiceSource.mute = true;
            }
        }
        public void Impact(BoxingImpact impact)
        {
            int cue = CueIndex(impact);
            var sounds = cueSounds[cue]; int sound = sounds.Next();
            var clip = sound < 0 ? null : sounds.clips[sound];
            LastSound = clip;
            // Haptic tails are finite (max 0.19s); preserve the whole sound as well.
            ImpactTailSeconds=Mathf.Max(ImpactTailSeconds,.19f,clip!=null ? clip.length : 0);
            LastImpact = impact; Reports++; Reported?.Invoke(impact);
            if (CanSend)
            {
                int index = TriggerIndex(impact);
                if (index < impactTriggers.Length && impactTriggers[index] != null)
                {
                    var trigger = impactTriggers[index];
                    trigger.GainMultiplier = impact.gain;
                    // Position addressing selects the wearable; do not silence one channel of a wrist unit.
                    trigger.Pan = 0;
                    trigger.Fire(); Sends++;
                }
            }
            if (clip != null) PlaySound(clip, Mathf.Clamp01(impact.gain) * sounds.volumes[sound]);
        }
        // Own punch: on the opponent's glove, or anywhere else (head, body, arms). Received: on the glove,
        // the forearm, or the head / body. Scoring keeps counting arm contacts as glove surfaces.
        public static int CueIndex(BoxingImpact impact)
        {
            int hard = impact.hard ? 1 : 0;
            if (impact.attack) return (impact.surface == ImpactSurface.Glove && impact.part == ImpactPart.Glove ? 0 : 2) + hard;
            if (impact.zone == ImpactZone.Head || impact.zone == ImpactZone.Body)
            {
                if (impact.surface != ImpactSurface.Body) throw new ArgumentException("Received head/body hits cannot be glove guards.");
                return 8 + hard;
            }
            return (impact.part == ImpactPart.Forearm ? 6 : 4) + hard;
        }
        public static string CueName(BoxingImpact impact) => Cues[CueIndex(impact)];
        // This demo has wrist L/R and neck receivers, not a fourth torso device.
        // Both received body/head hits use the neck entries.
        public static int TriggerIndex(BoxingImpact impact)
        {
            int cue = CueIndex(impact);
            return cue >= WristCues ? 2 * WristCues + cue - WristCues : (int)impact.zone * WristCues + cue;
        }
        public void Ring()
        {
            Rings++;
            if (!Application.isBatchMode && !forceSilent && soundEnabled && bellSource != null && bell != null)
            { bellSource.Stop(); bellSource.PlayOneShot(bell, 0.5f); }
        }
        private void Update() => ImpactTailSeconds=Mathf.Max(0,ImpactTailSeconds-Time.unscaledDeltaTime);
        public void SpeakCountdown(int number)
        {
            if(number<1 || number>3) return;
            LastSpokenNumber=number; VoiceCues++;
            if(!Application.isBatchMode && !forceSilent && soundEnabled && voiceSource!=null && countdownVoice[number-1]!=null)
            { voiceSource.Stop(); voiceSource.PlayOneShot(countdownVoice[number-1],DemoNarration.Gain); }
        }
        public AudioClip ResultClip(BoxingOutcome outcome) => outcome==BoxingOutcome.Win ? winVoice : outcome==BoxingOutcome.Lose ? loseVoice : tieVoice;
        public void SpeakResult(BoxingOutcome outcome)
        {
            LastSpokenOutcome=outcome; ResultVoiceCues++;
            if(!Application.isBatchMode && !forceSilent && soundEnabled && voiceSource!=null)
            { voiceSource.Stop(); voiceSource.PlayOneShot(ResultClip(outcome),DemoNarration.Gain); }
        }
        private void PlaySound(AudioClip clip, float volume)
        {
            if (!Application.isBatchMode && !forceSilent && soundEnabled && audioSource != null && clip != null)
                audioSource.PlayOneShot(clip, volume);
        }
        public void StopImpacts()
        {
            ImpactStops++;
            ImpactTailSeconds=0;
            foreach (var trigger in impactTriggers)
                if (trigger != null) { trigger.FlushPendingDelayCoroutines(); if (!Application.isBatchMode && !forceSilent) trigger.Stop(); }
            if (!Application.isBatchMode && !forceSilent && HapbeatManager.Instance != null && HapbeatManager.Instance.IsConnected) HapbeatManager.Instance.StopAll();
            if (audioSource != null) audioSource.Stop();
        }
        /// <summary>Hapbeat output only (Demo Session haptics switch); sounds keep playing.</summary>
        public void SetHapticsEnabled(bool enabled) { hapticsEnabled = enabled; if (!enabled) StopHaptics(); }
        public void StopHaptics()
        {
            foreach (var trigger in impactTriggers)
                if (trigger != null) { trigger.FlushPendingDelayCoroutines(); if (!Application.isBatchMode && !forceSilent) trigger.Stop(); }
            if (!Application.isBatchMode && !forceSilent && HapbeatManager.Instance != null && HapbeatManager.Instance.IsConnected) HapbeatManager.Instance.StopAll();
        }
        public void StopFeedback() { StopImpacts(); if (bellSource != null) bellSource.Stop(); if(voiceSource!=null) voiceSource.Stop(); }
        private void OnDisable() => StopFeedback();
    }
}
