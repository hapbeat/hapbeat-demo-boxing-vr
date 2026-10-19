using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.OpenXR;

namespace Hapbeat.Boxing
{
    [DefaultExecutionOrder(0)]
    public sealed class BoxingGame : MonoBehaviour
    {
        public BoxingTuning tuning;
        public BoxingInput input;
        public BoxingFeedback feedback;
        public BoxingPresentation presentation;
        public BoxingMenu menu;
        public BoxingRound Round { get; private set; } = new BoxingRound();
        public BoxingOpponent Opponent { get; private set; }
        public bool Paused { get; private set; }
        public string PauseReason { get; private set; } = "";
        public bool TrackingReady => validTime >= 0.2f;
        private bool focus = true, appPaused;
        private float validTime, leftCooldown, rightCooldown;
        private bool haveHistory, leftContact, rightContact;
        private bool leftBlockedThisFrame, rightBlockedThisFrame;
        private BoxerPose previous;
        private Vector3 oldEnemyLeft, oldEnemyRight, oldEnemyHead, oldEnemyBody;
        private readonly BoxingGuardArm[] enemyArms = new BoxingGuardArm[4], oldEnemyArms = new BoxingGuardArm[4];
        private int resolvedAttack, lastCompleted;
        private BoxingPhase previousPhase;
        private readonly BoxingPunch leftPunch = new BoxingPunch(), rightPunch = new BoxingPunch();
        private Vector3 sampledHead;
        private XRDisplaySubsystem display;
        private bool displayFocused = true, displayFocusKnown;
        private readonly List<XRDisplaySubsystem> displays = new List<XRDisplaySubsystem>();
        private string lastPauseReport;
        public float ResultPresentationTime { get; private set; }
        private bool calibrationPending;
        // Result hold before the completion panel. The panel does not stop the bell or the result voice.
        public float ResultDuration => tuning.resultPanelDelaySeconds;
        // Outside a session the next round restarts (StartRound stops all sound), so wait for the bell and voice first.
        public float RestartTime => Mathf.Max(ResultDuration, resultAudioSeconds) + tuning.restartAfterResultsSeconds;
        private float resultAudioSeconds;
        private int spokenCountdown;
        // Demo Session: results end in the shared completion panel instead of the round menu.
        public bool HoldResultsForSession { get; set; }
        // Shared pause / completion panel (IDemoSessionHost.SetGameplayPaused): the round, opponent and impacts stop; the gloves still follow the hands.
        public bool InputSuspended { get; set; }
        public event Action ResultsCompleted;
        private bool resultsReported;
        private float readyTime;
        // Tracking dropouts: lost parts hold their last measured pose for up to tuning.trackingGraceSeconds.
        public bool LeftHandActive { get; private set; } = true;
        public bool RightHandActive { get; private set; } = true;
        // Paused because tracking stayed lost beyond the grace time (the presentation shows the warning).
        public bool TrackingWarning { get; private set; }
        public float TrackingLostSeconds { get; private set; }
        private BoxerPose held;
        private bool haveHeld, leftWasActive = true, rightWasActive = true, headWasTracked = true;
        // Tutorial (BoxingPhase.Tutorial): 1-2 punch the head-guarding opponent, 3-4 take / block its straight, 5 = 試合開始.
        public int TutorialStep { get; private set; }
        public string TutorialText => tutorialMessageTime > 0 ? tutorialMessage : TutorialSteps[Mathf.Clamp(TutorialStep - 1, 0, TutorialSteps.Length - 1)];
        internal static readonly string[] TutorialSteps = { "1/4  相手の顔を打ってみよう", "2/4  お腹を打とう", "3/4  相手のパンチを受けてみよう", "4/4  顔の前で構えてパンチを防ごう" };
        internal static readonly string[] TutorialResults = { "ガードの上からはダメージが入らない", "ダメージが入った！", "ガードしないとダメージを受ける", "ガード成功！" };
        // Steps 3-4: the wrong outcome (blocked / hit) repeats the strike with this hint.
        internal static readonly string[] TutorialHints = { "ガードしないで受けてみよう", "グローブを顔の前に構えよう" };
        internal const string TutorialStartText = "試合開始";
        // Pause after a tutorial strike before the next; the blocking step's straight is this much slower than usual.
        private const float TutorialStrikeGap = 1.5f, TutorialBlockStrikeScale = 1.5f;
        // Face marker just before the guarding gloves (0.2 m before the head); belly marker this far below the body centre
        // (the head guard's elbows hang before the upper body, BoxingTutorialTests).
        private const float TutorialFaceMarkerDepth = .31f, TutorialBellyDrop = .1f;
        private string tutorialMessage = "";
        private float tutorialStepTime, tutorialMessageTime, tutorialStrikeWait;
        private bool tutorialAdvance, tutorialGuarded, tutorialBody, tutorialStriking;
        private int tutorialStrikes, tutorialTaken, tutorialBlocks;

        public void Initialize() { if (Opponent == null) Opponent = new BoxingOpponent(tuning); previousPhase = Round.Phase; }
        private void Awake() => Initialize();
        private void OnEnable()
        {
            Hapbeat.DemoSwitch.DemoSwitch.BeforeSwitch += BeforeSwitch;
            Application.focusChanged += FocusChanged;
            if (input != null) input.Recentered += OnRecentered;
        }
        private void OnDisable()
        {
            Hapbeat.DemoSwitch.DemoSwitch.BeforeSwitch -= BeforeSwitch;
            Application.focusChanged -= FocusChanged;
            if (input != null) input.Recentered -= OnRecentered;
            if (display != null) display.displayFocusChanged -= DisplayFocus;
            if (feedback != null) feedback.StopFeedback();
        }
        // A failed external launch leaves a resumable pause, not a permanent switch-pending latch.
        // The shared pause (再開 / 最初からやり直す) when this runtime hosts it, otherwise the round menu.
        public void PauseForExternalTransition()
        {
            if (Hapbeat.DemoSwitch.DemoPause.IsEnabled) Hapbeat.DemoSwitch.DemoPause.Pause();
            else if (!menu.IsOpen) menu.Open();
            ResetHistory(); feedback.StopFeedback();
        }
        private void BeforeSwitch(string _) => PauseForExternalTransition();
        private void FocusChanged(bool value) { focus = value; if (!value && !displayFocusKnown) PauseForExternalTransition(); }
        private void DisplayFocus(bool value)
        {
            bool lost = !value && (!displayFocusKnown || displayFocused);
            displayFocusKnown = true; displayFocused = value;
            if (lost) PauseForExternalTransition();
        }
        private void OnApplicationPause(bool value) { appPaused = value; if (value) PauseForExternalTransition(); }
        public void ResetHistory() { haveHistory = false; validTime = 0; leftContact = rightContact = false; leftCooldown = rightCooldown = 0; ResetPunches(); }
        public void ResetPunches() { leftPunch.Reset(); rightPunch.Reset(); }
        private void OnRecentered()
        {
            Initialize(); Opponent.Reset(input.ReferenceEyeHeight,input.ReferenceFloorHeight);
            presentation.CalibrateEnvironment(input.ReferenceFloorHeight,input.ReferenceEyeHeight);
            resolvedAttack=lastCompleted=0; ResetHistory();
        }
        // Local operations for a future authenticated M5 command adapter. No new wire protocol here.
        public void RecenterPlayer() { input.Recenter(); OnRecentered(); feedback.StopImpacts(); }
        public void UseHandTracking() { input.SelectMode(BoxingInputMode.Hands); ResetHistory(); }
        public void UseControllers() { input.SelectMode(BoxingInputMode.Controllers); ResetHistory(); }
        // The plain round (countdown, gong): retry, restart and the next round never repeat the tutorial.
        public void StartRound()
        {
            Prepare();
            // The opponent's own AI fights again; Opponent.Reset (OnRecentered) already dropped a held tutorial guard.
            Opponent.Scripted = false; TutorialStep = 0;
            Round.Start(tuning.roundSeconds, tuning.maximumHealth);
        }
        // Four short steps before the first round (tuning.tutorialOnStart); the last one starts the round.
        public void StartTutorial()
        {
            Prepare();
            Opponent.Scripted = true;
            Round.StartTutorial(tuning.maximumHealth);
            TutorialStep = 0; EnterTutorialStep();
        }
        private void Prepare()
        {
            if (presentation != null && presentation.enemyAvatar != null) presentation.enemyAvatar.ResetReaction();
            // Head height must not depend on controllers/both hands already being tracked.
            input.Recenter(); OnRecentered();
            calibrationPending=!input.HeadTracked && !input.HasOverride;
            ResultPresentationTime=0;
            resolvedAttack = lastCompleted = 0; ResetHistory(); feedback.StopFeedback(); menu.Close();
            spokenCountdown=0; resultsReported=false; readyTime=0;
        }
        private void Update()
        {
            Simulate(Time.unscaledDeltaTime, input.Current);
            string state = Paused ? PauseReason : "RUNNING";
            if (state != lastPauseReport)
            {
                Debug.Log("[Boxing State] " + state); lastPauseReport = state;
            }
        }
        public void Simulate(float dt, BoxerPose pose)
        {
            if(calibrationPending && input.HeadTracked)
            { input.Recenter(); OnRecentered(); calibrationPending=false; return; }
            if(Round.Phase==BoxingPhase.Results && previousPhase==BoxingPhase.Results && !menu.IsOpen)
            {
                // End the action before the next step. No combat runs here; a pause holds the result.
                if(!InputSuspended) ResultPresentationTime += dt>0 && dt<=.1f ? dt : 0;
                presentation.Render(this,pose,false);
                if(ResultPresentationTime>=ResultDuration)
                {
                    if(HoldResultsForSession) { if(!resultsReported) { resultsReported=true; ResultsCompleted?.Invoke(); } }
                    // Outside a session the next round starts by itself, like the completion panel's もう一度.
                    else if(ResultPresentationTime>=RestartTime) StartRound();
                }
                return;
            }
            bool stepValid = dt > 0 && dt <= 0.1f;
            // A brief dropout (Quest hands vanish for a few frames) keeps the round running: lost parts hold their
            // last measured pose and take no part in contacts. Only a loss beyond trackingGraceSeconds pauses.
            bool headOk = pose.valid || pose.headTracked, leftOk = pose.valid || pose.leftTracked, rightOk = pose.valid || pose.rightTracked;
            bool complete = headOk && leftOk && rightOk;
            if (complete) TrackingLostSeconds = 0; else if (stepValid) TrackingLostSeconds += dt;
            bool graced = !complete && haveHeld && validTime > 0 && TrackingLostSeconds < tuning.trackingGraceSeconds;
            if (headOk) { held.head = pose.head; held.headRotation = pose.headRotation; }
            if (leftOk) { held.left = pose.left; held.leftRotation = pose.leftRotation; held.leftClosed = pose.leftClosed; }
            if (rightOk) { held.right = pose.right; held.rightRotation = pose.rightRotation; held.rightClosed = pose.rightClosed; }
            if (complete) haveHeld = true;
            if (graced)
            {
                if (!headOk) { pose.head = held.head; pose.headRotation = held.headRotation; }
                if (!leftOk) { pose.left = held.left; pose.leftRotation = held.leftRotation; }
                if (!rightOk) { pose.right = held.right; pose.rightRotation = held.rightRotation; }
                pose.valid = true;
            }
            LeftHandActive = !graced || leftOk; RightHandActive = !graced || rightOk;
            bool tracking = pose.valid && stepValid;
            Vector3 fromStart = pose.head - input.StartPosition;
            bool outside = new Vector2(fromStart.x, fromStart.z).magnitude > tuning.playRadius;
            if (display != null && !display.running)
            {
                display.displayFocusChanged -= DisplayFocus;
                display = null; displayFocusKnown = false;
            }
            if (display == null)
            {
                SubsystemManager.GetSubsystems(displays);
                display = displays.Find(d => d.running);
                if (display != null) display.displayFocusChanged += DisplayFocus;
            }
            // A focused headset does not require the PC mirror/Game window to have focus.
            // Poll OpenXR as well as receiving events: startup focus may precede subscription.
            if (display != null) DisplayFocus(OpenXRUtility.IsSessionFocused);
            bool unavailable = !input.HasOverride && (appPaused || (displayFocusKnown ? !displayFocused : !focus));
            TrackingWarning = !tracking && stepValid && !outside && !unavailable && !InputSuspended && !(menu != null && menu.IsOpen);
            if (!tracking || outside || unavailable || InputSuspended || (menu != null && menu.IsOpen))
            {
                if (!Paused) feedback.StopImpacts();
                Paused = true;
                PauseReason = unavailable ? (appPaused ? "APPLICATION PAUSED" : displayFocusKnown ? "HEADSET PAUSED - OPENXR NOT FOCUSED" : "GAME WINDOW NOT FOCUSED") : InputSuspended ? "PAUSED" : outside ? "RETURN TO YOUR START POSITION" : !tracking ? input.TrackingStatus : "PAUSED";
                ResetHistory();
                if (presentation != null) presentation.Render(this, pose, false);
                return;
            }
            // A part that comes back resumes from where it is now: no sweep or punch across the gap.
            if (haveHistory)
            {
                if (LeftHandActive && !leftWasActive) { previous.left = pose.left; previous.leftRotation = pose.leftRotation; leftPunch.Reset(); leftContact = TouchingEnemy(pose.left); leftCooldown = tuning.hitCooldown; }
                if (RightHandActive && !rightWasActive) { previous.right = pose.right; previous.rightRotation = pose.rightRotation; rightPunch.Reset(); rightContact = TouchingEnemy(pose.right); rightCooldown = tuning.hitCooldown; }
                if (headOk && !headWasTracked) previous.head = pose.head;
            }
            leftWasActive = LeftHandActive; rightWasActive = RightHandActive; headWasTracked = headOk;
            validTime += dt;
            Paused = !TrackingReady;
            PauseReason = Paused ? "HOLD STILL" : "";
            if (Paused)
            {
                leftContact = TouchingEnemy(pose.left); rightContact = TouchingEnemy(pose.right);
                SaveHistory(pose); return;
            }
            if (haveHistory && (Vector3.Distance(pose.left, previous.left) > 0.6f || Vector3.Distance(pose.right, previous.right) > 0.6f || Vector3.Distance(pose.head, previous.head) > 0.35f))
            { ResetHistory(); feedback.StopFeedback(); return; }
            if(Round.Phase==BoxingPhase.Ready)
            {
                // Startup: both gloves tracked at the start position for a moment, then the tutorial (or straight the countdown and gong).
                readyTime+=dt;
                if(readyTime>=tuning.autoStartSeconds) { if(tuning.tutorialOnStart) StartTutorial(); else StartRound(); return; }
            }
            if(Round.Phase==BoxingPhase.Countdown)
            {
                int number=Mathf.CeilToInt(Round.Countdown);
                if(number!=spokenCountdown) { feedback.SpeakCountdown(number); spokenCountdown=number; }
            }
            Round.Tick(dt, false);
            // The tutorial fights like a round (contacts, reports, the scripted opponent) without the round clock.
            bool combat = Combat;
            // Without history this frame restarts the opponent's view of the gloves (no velocity across a gap).
            Opponent.ObservePlayer(pose, haveHistory ? dt : 0);
            Opponent.Tick(dt, pose.head, pose.headRotation, combat);
            sampledHead = pose.head;
            if (LeftHandActive) leftPunch.Sample(pose.left - pose.head, dt, tuning);
            if (RightHandActive) rightPunch.Sample(pose.right - pose.head, dt, tuning);
            if (haveHistory && combat)
            {
                leftBlockedThisFrame = rightBlockedThisFrame = false;
                leftCooldown = Mathf.Max(0, leftCooldown - dt); rightCooldown = Mathf.Max(0, rightCooldown - dt);
                ResolveEnemy(pose, dt);
                UpdateEnemyArms();
                if (LeftHandActive) ResolvePlayer(pose.left, previous.left, pose.leftClosed, ImpactZone.LeftGlove, ref leftContact, ref leftCooldown, dt);
                if (RightHandActive) ResolvePlayer(pose.right, previous.right, pose.rightClosed, ImpactZone.RightGlove, ref rightContact, ref rightCooldown, dt);
                if (Opponent.CompletedAttacks > lastCompleted)
                {
                    if (resolvedAttack != Opponent.AttackId) Round.Dodge();
                    lastCompleted = Opponent.CompletedAttacks;
                }
            }
            if (Round.Phase == BoxingPhase.Tutorial) TickTutorial(dt);
            if (Round.Phase != previousPhase)
            {
                if (Round.Phase == BoxingPhase.Results)
                {
                    ResultPresentationTime=0;
                    // Independent sources overlap the final hit without cancelling its tail.
                    feedback.Ring(); feedback.SpeakResult(Round.Outcome);
                    resultAudioSeconds=Mathf.Max(feedback.bell.length,feedback.ResultClip(Round.Outcome).length)+.35f;
                }
                if (Round.Phase == BoxingPhase.Fighting) feedback.Ring();
                previousPhase = Round.Phase;
            }
            SaveHistory(pose);
            if (presentation != null) presentation.Render(this, pose, true,dt);
        }
        private bool Combat => Round.Phase == BoxingPhase.Fighting || Round.Phase == BoxingPhase.Tutorial;
        private void EnterTutorialStep()
        {
            TutorialStep++; tutorialStepTime = 0; tutorialAdvance = tutorialGuarded = tutorialBody = tutorialStriking = false;
            tutorialMessageTime = 0; tutorialStrikes = 0; tutorialStrikeWait = TutorialStrikeGap;
            if (TutorialStep == 3) Opponent.ScriptedRelease();
        }
        // The marker for steps 1-2 (BoxingPresentation): before the guarded face, then on the belly below the guarding
        // elbows, where a straight reaches the body past the head guard. Hidden during the step's result message.
        public bool TutorialTarget(out Vector3 position)
        {
            position = default;
            if (Round.Phase != BoxingPhase.Tutorial || TutorialStep < 1 || TutorialStep > 2 || tutorialAdvance) return false;
            position = TutorialStep == 1 ? Opponent.Head + Vector3.back * TutorialFaceMarkerDepth
                : Opponent.Body + Vector3.down * TutorialBellyDrop + Vector3.back * (tuning.enemyBodyRadius + .01f);
            return true;
        }
        // The fallback (timeout, last strike) moves on without the step's result message.
        private void NextTutorialStep()
        {
            if (TutorialStep == 5) { StartRound(); return; }
            if (TutorialStep == 4) { TutorialStep = 5; TutorialMessage(TutorialStartText, true); return; }
            EnterTutorialStep();
        }
        // advance: a step's result message, then the next step; otherwise a hint while the step goes on.
        private void TutorialMessage(string text, bool advance)
        {
            tutorialMessage = text; tutorialMessageTime = tuning.tutorialMessageSeconds; tutorialAdvance = advance;
        }
        private void TickTutorial(float dt)
        {
            if (tutorialMessageTime > 0) tutorialMessageTime -= dt;
            if (tutorialAdvance)
            {
                if (tutorialMessageTime > 0) return;
                NextTutorialStep(); return;
            }
            tutorialStepTime += dt;
            if (TutorialStep <= 2)
            {
                // Set every step: a recenter (Opponent.Reset) drops it.
                Opponent.ScriptedGuard(BoxingGuard.HeadStraight);
                if (TutorialStep == 1 ? tutorialGuarded : tutorialBody) TutorialMessage(TutorialResults[TutorialStep - 1], true);
                else if (tutorialStepTime >= tuning.tutorialStepTimeoutSeconds) NextTutorialStep();
                return;
            }
            if (tutorialStriking)
            {
                // A strike ends when the opponent leaves its attack (retracted, or cut short by a recenter).
                if (Opponent.State == BoxingOpponentState.Attack) return;
                tutorialStriking = false; tutorialStrikes++; tutorialStrikeWait = TutorialStrikeGap;
                bool hit = Round.Taken > tutorialTaken, blocked = Round.Blocks > tutorialBlocks;
                if (TutorialStep == 3 ? hit : blocked) TutorialMessage(TutorialResults[TutorialStep - 1], true);
                else if (tutorialStrikes >= tuning.tutorialStrikes) NextTutorialStep();
                else if (TutorialStep == 3 ? blocked : hit) TutorialMessage(TutorialHints[TutorialStep - 3], false);
                return;
            }
            if ((tutorialStrikeWait -= dt) > 0) return;
            tutorialTaken = Round.Taken; tutorialBlocks = Round.Blocks; tutorialStriking = true;
            Opponent.ScriptedStrike(BoxingAttack.HeadStraight, false, TutorialStep == 4 ? TutorialBlockStrikeScale : 1);
        }
        private void ResolveEnemy(BoxerPose p, float dt)
        {
            if (!Opponent.Striking || resolvedAttack == Opponent.AttackId) return;
            Vector3 from = Opponent.AttackLeft ? oldEnemyLeft : oldEnemyRight;
            Vector3 to = Opponent.AttackLeft ? Opponent.Left : Opponent.Right;
            float earliest = float.PositiveInfinity;
            ImpactZone zone = ImpactZone.Head;
            var part = ImpactPart.Glove;
            Vector3 targetFrom = Vector3.zero, targetTo = Vector3.zero;
            void Candidate(ImpactZone z, Vector3 a, Vector3 b, float radius)
            {
                if (BoxingCollision.Sweep(from, to, tuning.enemyGloveRadius, a, b, radius, out float t) && t < earliest)
                { earliest = t; zone = z; targetFrom = a; targetTo = b; part = ImpactPart.Glove; }
            }
            // A hand lost within the tracking grace cannot guard.
            if (LeftHandActive) Candidate(ImpactZone.LeftGlove, previous.left, p.left, tuning.gloveRadius);
            if (RightHandActive) Candidate(ImpactZone.RightGlove, previous.right, p.right, tuning.gloveRadius);
            void Forearm(ImpactZone side, Vector3 a, Quaternion ar, Vector3 b, Quaternion br)
            {
                if(BoxingForearm.Sweep(from,to,tuning.enemyGloveRadius,a,ar,b,br,out float t,out var af,out var bf) && t<earliest)
                { earliest=t; zone=side; targetFrom=af; targetTo=bf; part=ImpactPart.Forearm; }
            }
            if (LeftHandActive) Forearm(ImpactZone.LeftGlove,previous.left,previous.leftRotation,p.left,p.leftRotation);
            if (RightHandActive) Forearm(ImpactZone.RightGlove,previous.right,previous.rightRotation,p.right,p.rightRotation);
            Candidate(ImpactZone.Head, previous.head, p.head, tuning.headRadius);
            if (BoxingPlayerBody.Sweep(from, to, tuning.enemyGloveRadius, previous.head, previous.headRotation, p.head, p.headRotation, out float bodyTime, out var bodyFrom, out var bodyTo) && bodyTime < earliest)
            { earliest = bodyTime; zone = ImpactZone.Body; targetFrom = bodyFrom; targetTo = bodyTo; part = ImpactPart.Glove; }
            if (float.IsPositiveInfinity(earliest)) return;
            resolvedAttack = Opponent.AttackId;
            float speed = BoxingCollision.RelativeSpeed(from, to, targetFrom, targetTo, dt);
            // A guard contact stops the trajectory, not just the scoring for this attack.
            if (zone == ImpactZone.LeftGlove || zone == ImpactZone.RightGlove)
            {
                Opponent.Block(Vector3.Lerp(from, to, earliest));
                leftBlockedThisFrame = zone == ImpactZone.LeftGlove; rightBlockedThisFrame = zone == ImpactZone.RightGlove;
                Vector3 hand = zone == ImpactZone.LeftGlove ? Vector3.Lerp(previous.left, p.left, earliest) : Vector3.Lerp(previous.right, p.right, earliest);
                (zone == ImpactZone.LeftGlove ? leftPunch : rightPunch).Consume(hand - Vector3.Lerp(previous.head, p.head, earliest), tuning);
            }
            if (speed >= tuning.minimumImpactSpeed) Report(new BoxingImpact(zone, speed, false, Vector3.Lerp(from, to, earliest), tuning,
                zone == ImpactZone.Head || zone == ImpactZone.Body ? ImpactSurface.Body : ImpactSurface.Glove, part));
        }
        private void ResolvePlayer(Vector3 to, Vector3 from, bool closed, ImpactZone side, ref bool contact, ref float cooldown, float dt)
        {
            if (!Combat) return;
            float earliest = float.PositiveInfinity;
            Vector3 targetFrom = default, targetTo = default;
            var surface = ImpactSurface.Body;
            var part = ImpactPart.Glove;
            bool hitHead = false;
            void Candidate(Vector3 a, Vector3 b, float radius, ImpactSurface material, bool head = false)
            {
                if (BoxingCollision.Sweep(from, to, tuning.gloveRadius, a, b, radius, out float t) && t < earliest)
                { earliest = t; targetFrom = a; targetTo = b; surface = material; hitHead = head; part = ImpactPart.Glove; }
            }
            // Earliest surface wins; a glove interception cannot also damage the body behind it.
            Candidate(oldEnemyLeft, Opponent.Left, tuning.enemyGloveRadius, ImpactSurface.Glove);
            Candidate(oldEnemyRight, Opponent.Right, tuning.enemyGloveRadius, ImpactSurface.Glove);
            for(int i=0;i<enemyArms.Length;i++)
                if(BoxingGuardArm.Sweep(from,to,tuning.gloveRadius,oldEnemyArms[i],enemyArms[i],out float t,out var a,out var b) && t<earliest)
                { earliest=t; targetFrom=a; targetTo=b; surface=ImpactSurface.Glove; hitHead=false; part=ImpactPart.Forearm; }
            Candidate(oldEnemyHead, Opponent.Head, tuning.enemyHeadRadius, ImpactSurface.Body, true);
            Candidate(oldEnemyBody, Opponent.Body, tuning.enemyBodyRadius, ImpactSurface.Body);
            bool hit = !float.IsPositiveInfinity(earliest);
            bool overlap = TouchingEnemy(to);
            // Hysteresis releases an existing contact; it must never create an early one.
            bool wasContact = contact; contact = hit || (wasContact && overlap);
            bool alreadyResolved = side == ImpactZone.LeftGlove ? leftBlockedThisFrame : rightBlockedThisFrame;
            if (!hit || wasContact || cooldown > 0 || !closed || alreadyResolved) return;
            float speed = BoxingCollision.RelativeSpeed(from, to, targetFrom, targetTo, dt);
            if (speed < tuning.minimumImpactSpeed) return;
            cooldown = tuning.hitCooldown;
            Vector3 point = Vector3.Lerp(from, to, earliest);
            Vector3 headAtContact = Vector3.Lerp(previous.head, sampledHead, earliest);
            float strength = (side == ImpactZone.LeftGlove ? leftPunch : rightPunch).Consume(point - headAtContact, tuning);
            var impact = new BoxingImpact(side, speed, point, tuning, surface, strength, part);
            if (Round.Phase == BoxingPhase.Tutorial) { if (surface == ImpactSurface.Glove) tutorialGuarded = true; else if (!hitHead) tutorialBody = true; }
            if (surface == ImpactSurface.Body)
            {
                Opponent.React(impact.gain, BoxingOpponent.Zone(hitHead, dt > 0 ? (to - from) / dt : Vector3.zero));
                if (presentation != null && presentation.enemyAvatar != null) presentation.enemyAvatar.React(!hitHead, impact.gain);
            }
            Report(impact);
        }
        private bool TouchingEnemy(Vector3 position)
        {
            foreach(var arm in enemyArms)
                if(arm.radius>0 && arm.Distance(position)<=tuning.gloveRadius+arm.radius+.03f) return true;
            return Vector3.Distance(position, Opponent.Head) <= tuning.gloveRadius + tuning.enemyHeadRadius + 0.03f ||
            Vector3.Distance(position, Opponent.Body) <= tuning.gloveRadius + tuning.enemyBodyRadius + 0.03f ||
            Vector3.Distance(position, Opponent.Left) <= tuning.gloveRadius+tuning.enemyGloveRadius + 0.03f || Vector3.Distance(position, Opponent.Right) <= tuning.gloveRadius+tuning.enemyGloveRadius + 0.03f;
        }
        private void UpdateEnemyArms()
        {
            if(presentation == null || presentation.enemyAvatar == null) return;
            presentation.enemyAvatar.Render(Opponent,0);
            presentation.enemyAvatar.GetGuardArms(enemyArms);
        }
        private void Report(BoxingImpact impact)
        {
            if (!Combat) return;
            Round.Report(impact); feedback.Impact(impact);
            if (presentation != null) presentation.Flash(impact);
        }
        private void SaveHistory(BoxerPose pose)
        {
            UpdateEnemyArms(); Array.Copy(enemyArms,oldEnemyArms,enemyArms.Length);
            previous = pose; oldEnemyLeft = Opponent.Left; oldEnemyRight = Opponent.Right;
            oldEnemyHead = Opponent.Head; oldEnemyBody = Opponent.Body; haveHistory = true;
        }
    }
}
