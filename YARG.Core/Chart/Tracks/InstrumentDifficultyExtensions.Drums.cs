using System;
using System.Collections.Generic;
using System.Linq;
using YARG.Core.Extensions;
using YARG.Core.Logging;

namespace YARG.Core.Chart
{
    public static partial class InstrumentDifficultyExtensions
    {
        private static int ConstructDrumNoteMask(DrumNote note, bool ignoreKickAndWildcard)
        {
            int mask = 0;
            foreach (var subNote in note.AllNotes)
            {
                if (ignoreKickAndWildcard &&
                    subNote.Pad is (int) FourLaneDrumPad.Kick or (int) FourLaneDrumPad.Wildcard)
                {
                    continue;
                }

                mask |= 1 << subNote.Pad;
            }

            return mask;
        }

        private static int GetDrumLaneIndex(FourLaneDrumPad pad)
        {
            return pad switch
            {
                FourLaneDrumPad.Kick    => 0,
                FourLaneDrumPad.RedDrum => 1,
                FourLaneDrumPad.YellowDrum
                    or FourLaneDrumPad.YellowCymbal => 2,
                FourLaneDrumPad.BlueDrum
                    or FourLaneDrumPad.BlueCymbal => 3,
                FourLaneDrumPad.GreenDrum
                    or FourLaneDrumPad.GreenCymbal => 4,
                FourLaneDrumPad.Wildcard => -1,
                _                        => throw new ArgumentOutOfRangeException(nameof(pad), pad, null)
            };
        }

        private static int GetDrumLaneIndex(FiveLaneDrumPad pad)
        {
            return pad switch
            {
                FiveLaneDrumPad.Kick     => 0,
                FiveLaneDrumPad.Red      => 1,
                FiveLaneDrumPad.Yellow   => 2,
                FiveLaneDrumPad.Blue     => 3,
                FiveLaneDrumPad.Orange   => 4,
                FiveLaneDrumPad.Green    => 5,
                FiveLaneDrumPad.Wildcard => -1,
                _                        => throw new ArgumentOutOfRangeException(nameof(pad), pad, null)
            };
        }

        public static void SetDrumActivationFlags(this InstrumentDifficulty<DrumNote> difficulty,
            StarPowerActivationType activationType)
        {
            var notes = difficulty.Notes;

            // Use checkpointing to only iterate through the notes once
            int checkpoint = 0;

            foreach (var phrase in difficulty.Phrases)
            {
                if (phrase.Type != PhraseType.DrumFill)
                {
                    continue;
                }

                for (int i = checkpoint; i < notes.Count; i++)
                {
                    checkpoint = i;

                    // If the current note is outside of the target phrase or if we have exhausted all notes
                    if (notes[i].Time >= phrase.TimeEnd || i == notes.Count - 1)
                    {
                        // Get the rightmost pad
                        var rightmostNote = notes[i].ParentOrSelf;
                        foreach (var note in notes[i].AllNotes)
                        {
                            if (note.Pad > rightmostNote.Pad)
                            {
                                rightmostNote = note;
                            }

                            // Set every note on this tick as an activation note in the case of AllNotes
                            if (activationType == StarPowerActivationType.AllNotes)
                            {
                                note.ActivateFlag(DrumNoteFlags.StarPowerActivator);
                            }
                        }

                        // Only set the rightmost activation note in the case of RightmostNote
                        if (activationType == StarPowerActivationType.RightmostNote)
                        {
                            rightmostNote.ActivateFlag(DrumNoteFlags.StarPowerActivator);
                        }

                        break;
                    }
                }
            }

            // return difficulty;
        }

        // Used for all wildcards and all opens
        public static void ConvertAllToPad(this InstrumentDifficulty<DrumNote> difficulty, int pad)
        {
            if (difficulty.Difficulty is Difficulty.Beginner)
            {
                // No point, Beginner is already all wildcards
                return;
            }

            bool laneActive = false;
            foreach (var note in difficulty.Notes)
            {
                // bool originallyKick = note.Pad is (int) FourLaneDrumPad.Kick;
                note.Pad = pad;
                note.ChildNotes.Clear();

                DrumNoteFlags kickLaneFlags = 0;
                NoteFlags laneFlags = 0;
                if ((note.IsLaneStart | note.IsKickLaneStart) && !laneActive)
                {
                    laneActive = true;
                    kickLaneFlags |= DrumNoteFlags.KickLaneStart;
                    laneFlags |= NoteFlags.LaneStart;
                }

                if ((note.IsLaneEnd | note.IsKickLaneEnd) && note.NextNote is not null && !note.NextNote.IsAnyLane)
                {
                    laneActive = false;
                    kickLaneFlags |= DrumNoteFlags.KickLaneEnd;
                    laneFlags |= NoteFlags.LaneEnd;
                }

                if (note.IsAnyLane)
                {
                    laneFlags |= NoteFlags.Tremolo;
                    kickLaneFlags |= DrumNoteFlags.KickLane;
                }

                // Clear the kick lane flags and lane flags before applying the new ones
                note.ClearFlag(DrumNoteFlags.KickLane | DrumNoteFlags.KickLaneStart | DrumNoteFlags.KickLaneEnd);
                note.ClearFlag(NoteFlags.LaneStart | NoteFlags.LaneEnd | NoteFlags.Tremolo | NoteFlags.Trill);

                if ((FourLaneDrumPad) pad is FourLaneDrumPad.Kick)
                {
                    note.ActivateFlag(kickLaneFlags);
                }
                else
                {
                    note.ActivateFlag(laneFlags);
                }
            }
        }

        public static void RemoveDynamics(this InstrumentDifficulty<DrumNote> difficulty)
        {
            foreach (var i in difficulty.Notes)
            {
                foreach (var note in i.AllNotes)
                {
                    note.Type = DrumNoteType.Neutral;
                }
            }
        }

        public static void RemovePads(this InstrumentDifficulty<DrumNote> difficulty, params int[] pads)
        {
            if (difficulty.Difficulty is Difficulty.Beginner)
            {
                return;
            }

            if (pads.Length == 0)
            {
                return;
            }

            var padSet = new HashSet<int>(pads);
            var notes = difficulty.Notes;
            var survivors = new List<DrumNote>();

            for (int index = 0; index < notes.Count; index++)
            {
                var note = notes[index];

                // Collect every pad of this chord (parent + children) that is NOT being removed
                survivors.Clear();
                if (!padSet.Contains(note.Pad))
                {
                    survivors.Add(note);
                }

                foreach (var childNote in note.ChildNotes)
                {
                    if (!padSet.Contains(childNote.Pad))
                    {
                        survivors.Add(childNote);
                    }
                }

                int totalPads = 1 + note.ChildNotes.Count;

                if (survivors.Count == totalPads)
                {
                    // Nothing to remove from this chord
                    continue;
                }

                if (survivors.Count > 0)
                {
                    // Part of the chord survives: the first surviving note becomes the parent
                    // (this is the original parent if it wasn't removed), the rest become its children.
                    var newNote = survivors[0].CloneWithoutChildNotes();
                    for (int i = 1; i < survivors.Count; i++)
                    {
                        newNote.AddChildNote(survivors[i]);
                    }

                    notes[index] = newNote;
                    continue;
                }

                // The entire chord is removed
                notes.RemoveAt(index);

                if (note.IsStarPowerActivator)
                {
                    // Move the activator flag to the NEXT note and shrink the activation phrase to end there.
                    if (index < notes.Count)
                    {
                        ApplyFlag(notes[index], DrumNoteFlags.StarPowerActivator);
                    }

                    Phrase? activationPhrase = null;
                    foreach (var phrase in difficulty.Phrases)
                    {
                        if (phrase.Type == PhraseType.DrumFill && phrase.Time < note.Time &&
                            phrase.TimeEnd >= note.Time)
                        {
                            activationPhrase = phrase;
                            break;
                        }
                    }

                    if (activationPhrase != null && index < notes.Count)
                    {
                        activationPhrase.TimeLength = notes[index].Time - activationPhrase.Time;
                        activationPhrase.TickLength = notes[index].Tick - activationPhrase.Tick;
                    }
                }

                // Starts move to the NEXT note, ends move to the PREVIOUS note, so sections never get extended.
                if (note is { IsSoloStart: true, IsSoloEnd: false } && index < notes.Count)
                {
                    ApplyFlag(notes[index], NoteFlags.SoloStart);
                }

                if (note.IsSoloEnd && index > 0)
                {
                    ApplyFlag(notes[index - 1], NoteFlags.SoloEnd);
                }

                if (note is { IsStarPowerStart: true, IsStarPowerEnd: false } && index < notes.Count)
                {
                    ApplyFlag(notes[index], NoteFlags.StarPowerStart);
                }

                if (note.IsStarPowerEnd && index > 0)
                {
                    ApplyFlag(notes[index - 1], NoteFlags.StarPowerEnd);
                }

                index--;
            }

            // Re-link the whole list once, now that all modifications are done
            for (int i = 0; i < notes.Count; i++)
            {
                notes[i].PreviousNote = i > 0 ? notes[i - 1] : null;
                notes[i].NextNote = i < notes.Count - 1 ? notes[i + 1] : null;
            }
        }

        public static void RemoveAllExceptPads(this InstrumentDifficulty<DrumNote> difficulty, params int[] pads)
        {
            var allPadsSet = new HashSet<int>();
            foreach (var fourLanePad in EnumExtensions<FourLaneDrumPad>.Values)
            {
                allPadsSet.Add((int) fourLanePad);
            }

            foreach (var fiveLanePad in EnumExtensions<FiveLaneDrumPad>.Values)
            {
                allPadsSet.Add((int) fiveLanePad);
            }

            foreach (var removePads in pads)
            {
                allPadsSet.Remove(removePads);
            }

            difficulty.RemovePads(allPadsSet.ToArray());
        }

        private static void ApplyFlag(DrumNote note, NoteFlags flag)
        {
            note.ActivateFlag(flag);
            foreach (var childNote in note.ChildNotes)
            {
                childNote.ActivateFlag(flag);
            }
        }

        private static void ApplyFlag(DrumNote note, DrumNoteFlags flag)
        {
            note.ActivateFlag(flag);
            foreach (var childNote in note.ChildNotes)
            {
                childNote.ActivateFlag(flag);
            }
        }

        public static void ShuffleNotes(this InstrumentDifficulty<DrumNote> difficulty, SyncTrack syncTrack, int seed,
            bool shuffleInGroups)
        {
            if (difficulty.Difficulty is Difficulty.Beginner)
            {
                // Beginner is only wildcards. No.
                return;
            }

            List<int> basePool;
            Func<int, int> getLane;
            if (difficulty.Instrument is Instrument.FiveLaneDrums)
            {
                basePool = new List<int>
                {
                    (int) FiveLaneDrumPad.Red,
                    (int) FiveLaneDrumPad.Yellow,
                    (int) FiveLaneDrumPad.Blue,
                    (int) FiveLaneDrumPad.Orange,
                    (int) FiveLaneDrumPad.Green,
                };
                getLane = pad => GetDrumLaneIndex((FiveLaneDrumPad) pad);
            }
            else
            {
                // What to do about elite drums...?
                if (difficulty.Instrument is Instrument.ProDrums)
                {
                    basePool = new List<int>
                    {
                        (int) FourLaneDrumPad.RedDrum,
                        (int) FourLaneDrumPad.YellowDrum,
                        (int) FourLaneDrumPad.YellowCymbal,
                        (int) FourLaneDrumPad.BlueDrum,
                        (int) FourLaneDrumPad.BlueCymbal,
                        (int) FourLaneDrumPad.GreenDrum,
                        (int) FourLaneDrumPad.GreenCymbal,
                    };
                }
                else
                {
                    basePool = new List<int>
                    {
                        (int) FourLaneDrumPad.RedDrum,
                        (int) FourLaneDrumPad.YellowDrum,
                        (int) FourLaneDrumPad.BlueDrum,
                        (int) FourLaneDrumPad.GreenDrum,
                    };
                }

                getLane = pad => GetDrumLaneIndex((FourLaneDrumPad) pad);
            }

            // Quarter note
            uint maxIdenticalNoteTickDistance = syncTrack.Resolution;

            var random = new Random(seed);
            var currentNotes = new List<DrumNote>();
            var availablePads = new List<int>();
            var selectedPads = new List<int>();

            int lastShuffledMask = 0;

            for (int noteIndex = 0; noteIndex < difficulty.Notes.Count; noteIndex++)
            {
                var parent = difficulty.Notes[noteIndex];

                int identicalNoteCount = 0;
                if (shuffleInGroups)
                {
                    var previousNote = parent;
                    // Scan through the next notes to find how many are identical to this one, and if they are close enough.
                    // If they are, we will shuffle them all the same way to avoid making something nightmarish to play
                    // (I mean, this is drums note shuffle, so it's already not fun lol)
                    for (int i = noteIndex + 1; i < difficulty.Notes.Count; i++)
                    {
                        var currentNote = difficulty.Notes[i];
                        if (currentNote.Pad is (int) FourLaneDrumPad.Kick or (int) FourLaneDrumPad.Wildcard)
                        {
                            // They will be skipped when copying too, but it needs to still know the length to loop over
                            if (identicalNoteCount > 0)
                            {
                                identicalNoteCount++;
                            }

                            continue;
                        }

                        if (ConstructDrumNoteMask(currentNote, true) == ConstructDrumNoteMask(parent, true) &&
                            currentNote.ChildNotes.Count == parent.ChildNotes.Count &&
                            // ugh, uint overflow
                            (previousNote.TickEnd > currentNote.Tick ||
                                currentNote.Tick - previousNote.TickEnd <= maxIdenticalNoteTickDistance))
                        {
                            identicalNoteCount++;
                            previousNote = currentNote;
                        }
                        else
                        {
                            break;
                        }
                    }
                }

                // Reset state
                // Must be done first and not last, otherwise skipping a note won't reset state
                currentNotes.Clear();
                selectedPads.Clear();
                availablePads.Clear();

                foreach (var note in parent.AllNotes)
                {
                    if (note.Pad is (int) FourLaneDrumPad.Kick or (int) FourLaneDrumPad.Wildcard)
                    {
                        continue;
                    }

                    if (!basePool.Contains(note.Pad))
                    {
                        continue;
                    }

                    currentNotes.Add(note);
                }

                if (currentNotes.Count == 0)
                {
                    // likely just a kick note, ignore
                    continue;
                }

                availablePads.AddRange(basePool);
                availablePads.Shuffle(random);

                if (availablePads.Count - lastShuffledMask.CountBits() >= currentNotes.Count)
                {
                    // If removing the pads from the last shuffled chord would still leave
                    // enough pads for this chord, then remove them to avoid repetition.
                    foreach (int pad in basePool)
                    {
                        if ((lastShuffledMask & (1 << pad)) != 0)
                        {
                            availablePads.Remove(pad);
                        }
                    }
                }

                // once a pad is picked the lane is removed to prevent tom+cymbal "chords" on the same lane.
                for (int i = 0; i < currentNotes.Count; i++)
                {
                    if (availablePads.Count < 1)
                    {
                        break;
                    }

                    int randomPad = availablePads.PopRandom(random);
                    selectedPads.Add(randomPad);

                    int lane = getLane(randomPad);
                    for (int j = availablePads.Count - 1; j >= 0; j--)
                    {
                        if (getLane(availablePads[j]) == lane)
                        {
                            availablePads.RemoveAt(j);
                        }
                    }
                }

                if (selectedPads.Count != currentNotes.Count)
                {
                    // Really this should not be possible, unless I screwed up the logic
                    YargLogger.LogFormatWarning("Cannot shuffle note at {0:0.000} ({1})",
                        parent.Time, parent.Tick);
                    continue;
                }

                // Sort by lane to preserve the general lane order, if that matters?
                currentNotes.Sort((left, right) =>
                {
                    int laneComparison = getLane(left.Pad).CompareTo(getLane(right.Pad));
                    return laneComparison != 0 ? laneComparison : left.Pad.CompareTo(right.Pad);
                });
                selectedPads.Sort((left, right) =>
                {
                    int laneComparison = getLane(left).CompareTo(getLane(right));
                    return laneComparison != 0 ? laneComparison : left.CompareTo(right);
                });

                for (int i = 0; i < currentNotes.Count; i++)
                {
                    currentNotes[i].Pad = selectedPads[i];
                }

                // This is lifted from the guitar code, and it's a little hacky since drums does not have chord masks
                int newChordMask = 0;
                foreach (int pad in selectedPads)
                {
                    newChordMask |= 1 << pad;
                }

                lastShuffledMask = newChordMask;

                if (shuffleInGroups && identicalNoteCount > 0)
                {
                    for (int j = 1; j <= identicalNoteCount; j++)
                    {
                        var currentNote = difficulty.Notes[noteIndex + j];
                        if (currentNote.Pad is (int) FourLaneDrumPad.Kick or (int) FourLaneDrumPad.Wildcard)
                        {
                            continue;
                        }

                        CopyNotePads(parent, currentNote);
                    }

                    noteIndex += identicalNoteCount;
                }

                if (shuffleInGroups && parent.IsTrill)
                {
                    HandleTrillAfterShuffle();
                    // Second trill note, both lanes have now been handled
                    if (noteIndex != 0 && parent.PreviousNote!.IsTrill)
                    {
                        // Advance noteIndex to past the trill
                        while (noteIndex < difficulty.Notes.Count && difficulty.Notes[noteIndex] is
                            { IsTrill: true, IsLaneEnd: false })
                        {
                            noteIndex++;
                        }
                    }
                }
                // These will cause nothing but problems in true shuffle mode, so just strip them out
                else if (!shuffleInGroups)
                {
                    const NoteFlags stripFlags = (NoteFlags.Trill | NoteFlags.Tremolo);
                    foreach (var subNote in parent.AllNotes)
                    {
                        subNote.ClearFlag(stripFlags);
                    }
                }

                continue;

                static void CopyNotePads(DrumNote source, DrumNote destination)
                {
                    destination.Pad = source.Pad;
                    for (int i = 0; i < source.ChildNotes.Count; i++)
                    {
                        destination.ChildNotes[i].Pad = source.ChildNotes[i].Pad;
                    }
                }

                void HandleTrillAfterShuffle()
                {
                    var parentNote = difficulty.Notes[noteIndex];
                    if (!parentNote.IsTrill)
                    {
                        return;
                    }

                    for (var i = noteIndex + 2; i < difficulty.Notes.Count; i += 2)
                    {
                        var currentNote = difficulty.Notes[i];
                        if (!currentNote.IsTrill)
                        {
                            return;
                        }

                        CopyNotePads(parentNote, currentNote);
                    }
                }
            }
        }
    }
}