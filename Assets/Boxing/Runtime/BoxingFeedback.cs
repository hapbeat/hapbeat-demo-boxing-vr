using System;
using System.Collections.Generic;
using Hapbeat;
using Hapbeat.DemoSwitch;
using UnityEngine;

namespace Hapbeat.Boxing
{
    // How one of a cue's sounds / haptic clips is picked per impact (variation.pick).
    public enum CueSoundPick { Random, RoundRobin }
    public static class CuePick
    {
        // The index to play out of count after `last` (-1 = none yet), -1 = none. Random never repeats the
        // previous one; RoundRobin goes in order.
        public static int Next(int count, CueSoundPick pick, int last)
        {
            if (count <= 0) return -1;
            if (count == 1) return 0;
            if (pick == CueSoundPick.RoundRobin) return (last + 1) % count;
            if (last < 0) return UnityEngine.Random.Range(0, count);
            int next = UnityEngine.Random.Range(0, count - 1); return next >= last ? next + 1 : next;
        }
    }
    // One cue's sounds (cue table sfx.sound / sfx.sounds) with their volumes (sfx volume x sound intensity)
    // and how one is picked per impact (variation.pick).
    [Serializable]
    public sealed class CueSoundSet
    {
        public AudioClip[] clips = new AudioClip[0];
        public float[] volumes = new float[0];
        // Each sound's sync point (ms from its start, cue table sounds.<name>.syncMs, DEC-093).
        public float[] syncMs = new float[0];
        public CueSoundPick pick;
        private int last = -1;
        // The index of the sound to play, -1 = none.
        public int Next() => last = CuePick.Next(clips == null ? 0 : clips.Length, pick, last);
    }
    // One cue's haptic clips at one place (cue table route clip / clips): the trigger of each clip's EventMap
    // entry (left_<cue>, left_<cue>_2, ...), picked per impact by variation.pick.
    [Serializable]
    public sealed class CueHapticSet
    {
        public HapbeatUnityEventTrigger[] triggers = new HapbeatUnityEventTrigger[0];
        // Each clip's sync point (ms from its start, cue table clips.<clip>.syncMs, DEC-093).
        public float[] syncMs = new float[0];
        public CueSoundPick pick;
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
        // left_<cue> x 8, right_<cue> x 8, then the neck's hit_body_soft / hit_body_hard (TriggerIndex), each with
        // one trigger per clip of the cue.
        public CueHapticSet[] impactHaptics = new CueHapticSet[2 * WristCues + 2];
        // Per cue: the clip played last (both wrists share it, so the next impact of the cue avoids it).
        private readonly int[] lastHapticClip = { -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 };
        public AudioSource audioSource, bellSource, voiceSource;
        // Kenney voice OGGs normalized to -20 LUFS (tools/tts/loudness.py), played at the Hub's narration volume.
        public AudioClip[] countdownVoice = new AudioClip[3];
        public AudioClip winVoice, loseVoice, tieVoice;
        public int ResultVoiceCues { get; private set; }
        public BoxingOutcome LastSpokenOutcome { get; private set; }
        public int LastSpokenNumber { get; private set; }
        public int VoiceCues { get; private set; }
        // Tutorial narration (Voice/voice-lines.json, AivisSpeech at -20 LUFS, tools/tts): per step (BoxingGame.TutorialSteps
        // order) its instruction and success lines, the retry lines of steps 3-4, and 試合開始.
        public AudioClip[] tutorialStepVoice = new AudioClip[4], tutorialResultVoice = new AudioClip[4], tutorialHintVoice = new AudioClip[2];
        public AudioClip tutorialStartVoice;
        public int TutorialVoiceCues { get; private set; }
        public AudioClip LastTutorialVoice { get; private set; }
        public float ImpactTailSeconds { get; private set; }
        // Per cue (Cues order): its sounds (none = no sound), from the cue table.
        public CueSoundSet[] cueSounds = new CueSoundSet[10];
        public AudioClip LastSound { get; private set; }
        // The trigger picked for the last impact (also when nothing is sent).
        public HapbeatUnityEventTrigger LastHaptic { get; private set; }
        public AudioClip bell;
        public bool soundEnabled = true;
        public bool hapticsEnabled = true;
        // The wrist cues never play below this share of the impact gain (the speed / punch-strength gain stays
        // in the score and the sound); the neck keeps the plain gain.
        [Range(0, 1)] public float wristMinimumGain = 0.8f;
        public bool forceSilent;
        public int Reports { get; private set; }
        public int Sends { get; private set; }
        public int Rings { get; private set; }
        public int ImpactStops { get; private set; }
        public BoxingImpact LastImpact { get; private set; }
        public event Action<BoxingImpact> Reported;
        public bool CanSend => !Application.isBatchMode && !forceSilent && hapticsEnabled;
        // Impact sounds on the audio clock (DEC-093): each at an exact time, overlapping ones never cut each other.
        private DemoTimedSound timedSound;
        // How far ahead of an impact its sound and haptic are placed (seconds): one audio buffer, so both land exactly.
        public double ImpactLeadSeconds { get; private set; }
        public double LastSoundDelay { get; private set; }
        public double LastHapticDelay { get; private set; }
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
            int index = TriggerIndex(impact);
            var haptics = index < impactHaptics.Length ? impactHaptics[index] : null;
            int pick = haptics == null ? -1 : (lastHapticClip[cue] = CuePick.Next(haptics.triggers.Length, haptics.pick, lastHapticClip[cue]));
            LastHaptic = pick < 0 ? null : haptics.triggers[pick];
            LastImpact = impact; Reports++; Reported?.Invoke(impact);
            // DEC-093: the haptic goes (sound sync point − clip sync point) + this device's offset after the sound, or
            // the sound waits when that is negative; both a lead ahead, so the sound is placed exactly on the audio clock.
            double soundSync = clip != null && sounds.syncMs != null && sound < sounds.syncMs.Length ? sounds.syncMs[sound] : 0;
            double hapticSync = haptics != null && pick >= 0 && haptics.syncMs != null && pick < haptics.syncMs.Length ? haptics.syncMs[pick] : 0;
            HapbeatAvSync.Split(clip != null ? soundSync : hapticSync, hapticSync, out var soundDelay, out var hapticDelay);
            ImpactLeadSeconds = DemoTimedSound.Lead;
            LastSoundDelay = ImpactLeadSeconds + soundDelay; LastHapticDelay = ImpactLeadSeconds + hapticDelay;
            if (CanSend && LastHaptic != null)
            {
                var trigger = LastHaptic;
                trigger.GainMultiplier = HapticGain(impact);
                // Position addressing selects the wearable; do not silence one channel of a wrist unit.
                trigger.Pan = 0;
                trigger.FireAfter((float)LastHapticDelay); Sends++;
            }
            if (clip != null) PlaySound(clip, Mathf.Clamp01(impact.gain) * sounds.volumes[sound], LastSoundDelay);
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
        public float HapticGain(BoxingImpact impact) =>
            TriggerIndex(impact) < 2 * WristCues ? Mathf.Max(impact.gain, wristMinimumGain) : impact.gain;
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
        // Starts a tutorial line (a new one stops the previous) and returns its length in seconds (0 = no clip), also when silent.
        public float SpeakTutorial(AudioClip clip)
        {
            LastTutorialVoice=clip; TutorialVoiceCues++;
            if(!Application.isBatchMode && !forceSilent && soundEnabled && voiceSource!=null)
            { voiceSource.Stop(); if(clip!=null) voiceSource.PlayOneShot(clip,DemoNarration.Gain); }
            return clip!=null ? clip.length : 0;
        }
        public AudioClip ResultClip(BoxingOutcome outcome) => outcome==BoxingOutcome.Win ? winVoice : outcome==BoxingOutcome.Lose ? loseVoice : tieVoice;
        public void SpeakResult(BoxingOutcome outcome)
        {
            LastSpokenOutcome=outcome; ResultVoiceCues++;
            if(!Application.isBatchMode && !forceSilent && soundEnabled && voiceSource!=null)
            { voiceSource.Stop(); voiceSource.PlayOneShot(ResultClip(outcome),DemoNarration.Gain); }
        }
        private void PlaySound(AudioClip clip, float volume, double delaySeconds)
        {
            if (Application.isBatchMode || forceSilent || !soundEnabled || audioSource == null || clip == null) return;
            if (timedSound == null) timedSound = new DemoTimedSound(audioSource, 8);
            timedSound.Play(clip, volume, delaySeconds);
        }
        public void StopImpacts()
        {
            ImpactStops++;
            ImpactTailSeconds=0;
            foreach (var trigger in ImpactTriggers())
                if (trigger != null) { trigger.FlushPendingDelayCoroutines(); if (!Application.isBatchMode && !forceSilent) trigger.Stop(); }
            if (!Application.isBatchMode && !forceSilent && HapbeatManager.Instance != null && HapbeatManager.Instance.IsConnected) HapbeatManager.Instance.StopAll();
            if (audioSource != null) audioSource.Stop();
            StopImpactSounds();
        }
        /// <summary>Impact sounds playing or scheduled (they play on their own voices, not on audioSource).</summary>
        public void StopImpactSounds() => timedSound?.StopAll();
        /// <summary>Hapbeat output only (Demo Session haptics switch); sounds keep playing.</summary>
        public void SetHapticsEnabled(bool enabled) { hapticsEnabled = enabled; if (!enabled) StopHaptics(); }
        public void StopHaptics()
        {
            foreach (var trigger in ImpactTriggers())
                if (trigger != null) { trigger.FlushPendingDelayCoroutines(); if (!Application.isBatchMode && !forceSilent) trigger.Stop(); }
            if (!Application.isBatchMode && !forceSilent && HapbeatManager.Instance != null && HapbeatManager.Instance.IsConnected) HapbeatManager.Instance.StopAll();
        }
        // Every impact trigger (all cues, places and clips).
        public IEnumerable<HapbeatUnityEventTrigger> ImpactTriggers()
        {
            foreach (var set in impactHaptics)
                if (set != null) foreach (var trigger in set.triggers) yield return trigger;
        }
        public void StopFeedback() { StopImpacts(); if (bellSource != null) bellSource.Stop(); if(voiceSource!=null) voiceSource.Stop(); }
        private void OnDisable() => StopFeedback();
    }
}
