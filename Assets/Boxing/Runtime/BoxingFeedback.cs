using System;
using Hapbeat;
using UnityEngine;

namespace Hapbeat.Boxing
{
    [DefaultExecutionOrder(-10000)]
    public sealed class BoxingFeedback : MonoBehaviour
    {
        public GameObject sdkRoot;
        // Per receiver: glove soft/hard, body soft/hard. Surface is independent of attack direction.
        public HapbeatUnityEventTrigger[] impactTriggers = new HapbeatUnityEventTrigger[12];
        public AudioSource audioSource, bellSource, voiceSource;
        public AudioClip[] countdownVoice = new AudioClip[3];
        public AudioClip winVoice, loseVoice, tieVoice;
        public int ResultVoiceCues { get; private set; }
        public BoxingOutcome LastSpokenOutcome { get; private set; }
        public int LastSpokenNumber { get; private set; }
        public int VoiceCues { get; private set; }
        public float ImpactTailSeconds { get; private set; }
        public AudioClip[] contactSounds = new AudioClip[4];
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
            var clip=contactSounds[SoundIndex(impact)];
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
            PlaySound(contactSounds[SoundIndex(impact)], Mathf.Clamp01(impact.gain));
        }
        public static int SoundIndex(BoxingImpact impact) => (int)impact.surface * 2 + (impact.hard ? 1 : 0);
        // This demo has wrist L/R and neck receivers, not a fourth torso device.
        // Both received body/head hits use the existing neck feedback bank.
        public static int TriggerIndex(BoxingImpact impact) => (impact.zone == ImpactZone.Body ? 2 : (int)impact.zone) * 4 + SoundIndex(impact);
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
            { voiceSource.Stop(); voiceSource.PlayOneShot(countdownVoice[number-1],.75f); }
        }
        public AudioClip ResultClip(BoxingOutcome outcome) => outcome==BoxingOutcome.Win ? winVoice : outcome==BoxingOutcome.Lose ? loseVoice : tieVoice;
        public void SpeakResult(BoxingOutcome outcome)
        {
            LastSpokenOutcome=outcome; ResultVoiceCues++;
            if(!Application.isBatchMode && !forceSilent && soundEnabled && voiceSource!=null)
            { voiceSource.Stop(); voiceSource.PlayOneShot(ResultClip(outcome),.75f); }
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
        public void StopFeedback() { StopImpacts(); if (bellSource != null) bellSource.Stop(); if(voiceSource!=null) voiceSource.Stop(); }
        private void OnDisable() => StopFeedback();
    }
}
