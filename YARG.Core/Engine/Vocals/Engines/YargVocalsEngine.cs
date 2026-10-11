using System;
using System.Collections.Generic;
using System.Linq;
using YARG.Core.Chart;
using YARG.Core.Input;
using YARG.Core.Logging;

namespace YARG.Core.Engine.Vocals.Engines
{
    public class YargVocalsEngine : VocalsEngine
    {
        public YargVocalsEngine(InstrumentDifficulty<VocalNote> chart, SyncTrack syncTrack,
            VocalsEngineParameters engineParameters, bool isBot)
            : base(chart, syncTrack, engineParameters, isBot)
        {
        }

        protected override void UpdateBot(double songTime)
        {
            if (!IsBot)
            {
                return;
            }

            IsStarPowerInputActive = CanStarPowerActivate && !IsStarPowerInputActive;

            var phrase = Notes[NoteIndex];

            // Handle singing notes
            var singNote = GetNoteInPhraseAtSongTick(phrase, CurrentTick);
            if (singNote is not null)
            {
                // Bots are queued extra updates to account for in-between "inputs"
                PitchSang = singNote.PitchAtSongTime(songTime);
                HasSang = true;
                OnSing?.Invoke(true);
            }
            else
            {
                // Stop hitting to prevent the hit particles from showing up too much
                OnHit?.Invoke(false);
            }

            // Handle percussion notes
            var percussion = GetNextPercussionNote(phrase, CurrentTick);
            if (percussion is not null && songTime >= percussion.Time)
            {
                HasHit = true;
            }
        }

        protected override void MutateStateWithInput(GameInput gameInput)
        {
            var action = gameInput.GetAction<VocalsAction>();

            if (action is VocalsAction.Hit && gameInput.Button)
            {
                HasHit = true;
            }
            else if (action is VocalsAction.Pitch)
            {
                HasSang = true;
                PitchSang = gameInput.Axis;

                OnSing?.Invoke(true);
            }
            else if (action is VocalsAction.StarPower)
            {
                IsStarPowerInputActive = gameInput.Button;
            }
        }

        protected override void UpdateHitLogic(double time)
        {
            // Quit early if there are no notes left
            if (NoteIndex >= Notes.Count)
            {
                HasSang = false;
                return;
            }

            UpdateBot(time);

            var phrase = Notes[NoteIndex];
            PhraseTicksTotal ??= GetTicksInPhrase(phrase);

            CheckForNoteHit();

            if (phrase.IsPercussionPhrase && !IsSoloActive && CurrentTick < phrase.TickEnd)
            {
                YargLogger.LogFormatDebug("Starting percussion phrase at tick {0} (phrase end: {1})", CurrentTick, phrase.TickEnd);
                StartPercussionPhrase();
            }
            else if (phrase.IsPercussionPhrase && IsSoloActive && CurrentTick > phrase.TickEnd)
            {
                YargLogger.LogFormatDebug("Ending percussion phrase at tick {0} (phrase end: {1})", CurrentTick, phrase.TickEnd);
                EndPercussionPhrase();
            }

            // Check for the end of a phrase
            if (CurrentTick > phrase.TickEnd)
            {
                bool hasNotes = PhraseTicksTotal.Value != 0;
                bool isLastPhrase = NoteIndex == Notes.Count - 1;

                var percentHit = PhraseTicksHit / PhraseTicksTotal.Value;
                if (!hasNotes)
                {
                    percentHit = 1.0;
                }

                var ticksHit = (uint) Math.Round(PhraseTicksHit);

                EngineStats.TicksHit += ticksHit;
                EngineStats.TicksMissed += PhraseTicksTotal.Value - ticksHit;

                bool hit = percentHit >= EngineParameters.PhraseHitPercent;
                if (hit)
                {
                    EngineStats.PhrasesHit++;
                    HitNote(phrase);
                }
                else
                {
                    EngineStats.PhrasesHit += YargMath.InverseLerpD(0.0, EngineParameters.PhraseHitPercent, percentHit);
                    MissNote(phrase, percentHit);
                }

                PhraseTicksHit = 0;
                PhraseTicksTotal = null;
                TicksHitInStarPower = 0;
                BandBonusTicks = 0;

                if (hasNotes)
                {
                    OnPhraseHit?.Invoke(percentHit / EngineParameters.PhraseHitPercent, hit, isLastPhrase);
                }

                UpdateCarriedNote(phrase);
            }
        }

        private void StartPercussionPhrase()
        {
            if (CurrentSoloIndex >= Solos.Count)
            {
                return;
            }
            IsSoloActive = true;
            OnSoloStart?.Invoke(Solos[CurrentSoloIndex]);
        }

        private void EndPercussionPhrase()
        {
            if (!IsSoloActive)
            {
                return;
            }

            var currentSolo = Solos[CurrentSoloIndex];

            double soloPercentage = currentSolo.NotesHit / (double) currentSolo.NoteCount;

            if (soloPercentage < 0.6)
            {
                currentSolo.SoloBonus = 0;
            }
            else
            {
                double multiplier = Math.Clamp((soloPercentage - 0.6) / 0.4, 0, 1);

                double points = PointsPerPercussion * currentSolo.NotesHit * multiplier;

                // Round down to nearest 50 (kinda just makes sense I think?)
                points -= points % 50;

                currentSolo.SoloBonus = (int) points;
            }

            EngineStats.SoloBonuses += currentSolo.SoloBonus;

            IsSoloActive = false;

            OnSoloEnd?.Invoke(Solos[CurrentSoloIndex]);
            CurrentSoloIndex++;
        }

        protected override void CheckForNoteHit()
        {
            CheckSingingHit();
            CheckPercussionHit();
        }

        private void CheckSingingHit()
        {
            if (!HasSang)
            {
                return;
            }

            HasSang = false;
            var lastSingTick = LastSingTick;
            // If the last sing detected was on the same tick (or less), skip it
            // since we've already handled that tick.
            if (lastSingTick >= CurrentTick)
            {
                return;
            }

            LastSingTick = CurrentTick;

            var phrase = Notes[NoteIndex];

            // Get the note that we should currently be targeting
            var note = GetNoteInPhraseAtSongTick(phrase, CurrentTick);
            if (note is null)
            {
                // If we're not on a note, we cannot be hitting a note
                OnHit?.Invoke(false);

                return;
            }

            OnTargetNoteChanged?.Invoke(note);

            if (note != PreviousVocalNote)
            {
                TicksHitInCurrentNote = 0;
                PreviousVocalNote = note;
            }

            // This will never be a percussion note here
            if (CanVocalNoteBeHit(note, out float hitPercent))
            {
                if (TicksHitInCurrentNote >= PhraseTicksTotal)
                {
                    // If we've already hit all the ticks in this note, we cannot hit any more
                    OnHit?.Invoke(true);
                    return;
                }

                if (lastSingTick < note.Tick)
                {
                    YargLogger.LogFormatDebug("Delay to first detected sing for note at {1}: {0}", CurrentTime - note.Time, note.Time);
                }
                /*
                 * TODO:
                 *  - Clean up whatever bullshit is going on with this code
                 *  - Make it work with Free Harmonies
                 *  - Add pitch leniency support (on slides and fast pitch changes)
                 *  - Add timing leniency support outside of LenientUnpitched (maybe? try it first)
                 */
                // We will apply a leniency here and assume that it will also hit all other
                // ticks, since the user cannot change pitch between inputs.
                var maxLeniency = 1.0 / EngineParameters.ApproximateVocalFps;
                var lastTick = Math.Max(
                    SyncTrack.TimeToTick(CurrentTime - maxLeniency),
                    lastSingTick);
                var ticksSinceLast = CurrentTick - lastTick;
                var tickProgress = TicksHitInCurrentNote / note.TotalTickLength;
                var tickBoost = DetermineTickBoost(tickProgress);
                YargLogger.LogFormatDebug("Tick boost: {0}, Tick Progress: {1}", tickBoost, tickProgress);
                var ticksHit = Math.Min(ticksSinceLast * hitPercent, PhraseTicksTotal!.Value - TicksHitInCurrentNote) * tickBoost;
                YargLogger.LogFormatDebug("TicksHitInCurrentNote: {0}, TotalTickLength: {1}", TicksHitInCurrentNote, note.TotalTickLength);
                if (TicksHitInCurrentNote >= note.TotalTickLength * DetermineHitLeniency(note.TotalTimeLength))
                {
                    YargLogger.LogFormatDebug("Awarding remaining ticks for note at {0}", note.Time);
                    ticksHit = note.TotalTickEnd - CurrentTick;
                    LastSingTick = note.TotalTickEnd;
                }
                uint maxScoringTicks = (uint) Math.Round(PhraseTicksTotal!.Value * EngineParameters.PhraseHitPercent);
                if (PhraseTicksHit < maxScoringTicks)
                {
                    // Cap at maxScoringTicks
                    var ticksToAward = Math.Min(ticksHit, maxScoringTicks - PhraseTicksHit);
                    if (EngineStats.IsStarPowerActive)
                    {
                        TicksHitInStarPower += ticksToAward;
                    }

                    if (EngineStats.BandBonusMultiplier > 0)
                    {
                        BandBonusTicks += ticksToAward * EngineStats.BandBonusMultiplier;
                    }
                }
                TicksHitInCurrentNote += ticksHit;
                PhraseTicksHit += ticksHit;
                OnHit?.Invoke(true);
            }
            else
            {
                OnHit?.Invoke(false);
            }
        }

        private static double DetermineHitLeniency(double timeLength)
        {
            return 1;
            return timeLength switch
            {
                < 0.18 => 0.01,
                < 0.25 => 0.2 + 0.3 * YargMath.InverseLerpD(0.0, 0.25, timeLength),
                _     => 0.5 + 0.25 * Math.Clamp(YargMath.InverseLerpD(0.1, 1.5, timeLength), 0, 1)
            };
        }

        private static double DetermineTickBoost(double tickProgress)
        {
            // return tickProgress switch
            // {
            //     // Quadruple ticks for first 25% of ticks hit
            //     < 0.25 => 4 - 2 * YargMath.InverseLerpD(0.0, 0.25, tickProgress),
            //     // Double ticks for next 25% of ticks hit
            //     < 0.5 => 2 - 1 * YargMath.InverseLerpD(0.25, 0.5, tickProgress),
            //     // Half ticks for next 25% of ticks hit
            //     < 0.75 => 1 - 0.5 * YargMath.InverseLerpD(0.5, 0.75, tickProgress),
            //     // Quarter ticks for last 25% of ticks hit
            //     _ => 0.5 * YargMath.InverseLerpD(0.75, 1.0, tickProgress)
            // };

            // Idea 2: Even linear scale from 2-1, then 1-0.5
            return tickProgress switch
            {
                < 0.5 => 2 - 1 * YargMath.InverseLerpD(0.0, 0.5, tickProgress),
                _ => 1 - 0.5 * YargMath.InverseLerpD(0.5, 1.0, tickProgress)
            };
        }

        private void CheckPercussionHit()
        {
            var phrase = Notes[NoteIndex];
            var note = GetNextPercussionNote(phrase, CurrentTick);

            if (note is not null)
            {
                if (IsNoteInWindow(note, out var missed))
                {
                    if (HasHit)
                    {
                        HitNote(note);
                    }
                }
                else if (missed)
                {
                    // Miss out the back end
                    MissNote(note);
                }
            }
            else
            {
                // Singing (or any noise) can result in a call to CheckPercussionHit() as well, so we need to check SingToActivateStarPower here.
                if (HasHit && CanStarPowerActivate && EngineParameters.SingToActivateStarPower)
                {
                    ActivateStarPower();
                }
            }

            HasHit = false;
        }

        protected override bool CanVocalNoteBeHit(VocalNote note, out float hitPercent)
        {
            // If it is non-pitched, it is always hittable
            if (note.IsNonPitched)
            {
                hitPercent = 1f;
                return true;
            }

            var expectedPitch = note.PitchAtSongTime(CurrentTime);

            // Formula for calculating the distance to the expected pitch, while ignoring octaves
            float distanceToExpected = Math.Min(
                Mod(PitchSang - expectedPitch, 12f),
                Mod(expectedPitch - PitchSang, 12f));

            // If it is within the full points window, award full points
            if (distanceToExpected <= EngineParameters.PitchWindowPerfect)
            {
                hitPercent = 1f;
                return true;
            }

            // If it is outside of the total pitch window, then award no points
            if (distanceToExpected > EngineParameters.PitchWindow)
            {
                hitPercent = 0f;
                return false;
            }

            hitPercent = YargMath.InverseLerpF(
                EngineParameters.PitchWindow,
                EngineParameters.PitchWindowPerfect,
                distanceToExpected);
            return true;

            // Positive remainder
            static float Mod(float a, float b)
            {
                var remainder = a % b;
                if (remainder < 0)
                {
                    if (b < 0)
                    {
                        return remainder - b;
                    }

                    return remainder + b;
                }

                return remainder;
            }
        }

        protected override bool CanNoteBeHit(VocalNote note) => throw new NotImplementedException();

        protected override bool ProximalLaneForgivesInput(int inputNote, VocalNote laneNote) => throw new NotImplementedException();
    }
}
