using System;
using System.Collections.Generic;
using YARG.Core.Engine.Guitar;
using YARG.Core.Extensions;
using YARG.Core.Logging;

namespace YARG.Core.Chart
{
    public static partial class InstrumentDifficultyExtensions
    {
        private static int ChordMask(params FiveFretGuitarFret[] frets)
        {
            int mask = 0;
            foreach (var fret in frets)
            {
                mask |= 1 << ((int) fret - 1);
            }

            return mask;
        }

        private static int[] FretMap(params (FiveFretGuitarFret From, SixFretGuitarFret To)[] pairs)
        {
            var map = new int[(int) FiveFretGuitarFret.Orange + 1];
            foreach (var (from, to) in pairs)
            {
                map[(int) from] = (int) to;
            }

            return map;
        }

        private static void TruncateSustain(GuitarNote note, uint tick)
        {
            if (tick <= note.Tick)
            {
                note.TickLength = 0;
                note.TimeLength = 0;
                return;
            }

            uint oldTickLength = note.TickLength;
            note.TickLength = tick - note.Tick;
            if (oldTickLength > 0)
            {
                note.TimeLength *= (double) note.TickLength / oldTickLength;
            }

            if (note.ParentOrSelf.ChildNotes.Count >= 1 && note.IsChild &&
                note.ParentOrSelf.TickLength != note.TickLength)
            {
                // Mark the chord as disjoint, at least one sustain is a different length than the rest.
                foreach (var subNote in note.ParentOrSelf.AllNotes)
                {
                    subNote.ActivateGuitarFlag(GuitarNoteFlags.Disjoint);
                }
            }
        }

        public static void ConvertToGuitarType(this InstrumentDifficulty<GuitarNote> difficulty, GuitarNoteType type)
        {
            foreach (var note in difficulty.Notes)
            {
                note.Type = type;
                foreach (var child in note.ChildNotes)
                {
                    child.Type = type;
                }
            }
        }

        public static void ConvertFromTypeToType(this InstrumentDifficulty<GuitarNote> difficulty,
            GuitarNoteType from, GuitarNoteType to)
        {
            foreach (var note in difficulty.Notes)
            {
                if (note.Type != from)
                {
                    continue;
                }

                note.Type = to;
                foreach (var child in note.ChildNotes)
                {
                    child.Type = to;
                }
            }
        }

        public static void ConvertFromOpenToGreen(this InstrumentDifficulty<GuitarNote> difficulty, SyncTrack syncTrack)
        {
            GuitarNote? lastGreenSustain = null;
            GuitarNote? currentGreen = null;
            GuitarNote? currentOpen = null;
            bool openInLastGreenSustainChordBeforeConversion = false;
            int lastNoteMask = 0;
            uint sixteenthTickLength = syncTrack.Resolution / 4;
            int noteMaskGreen = 1 << (FiveFretGuitarFret.Green.Convert() - 1);
            int noteMaskOpen = 1 << (FiveFretGuitarFret.Open.Convert() - 1);
            foreach (var note in difficulty.Notes)
            {
                if (note.Fret == FiveFretGuitarFret.Open.Convert())
                {
                    currentOpen = note;
                }

                if (note.Fret == FiveFretGuitarFret.Green.Convert())
                {
                    currentGreen = note;
                }

                if (note.IsParent)
                {
                    foreach (var childNote in note.ChildNotes)
                    {
                        if (childNote.Fret == FiveFretGuitarFret.Open.Convert())
                        {
                            currentOpen = childNote;
                        }

                        if (childNote.Fret == FiveFretGuitarFret.Green.Convert())
                        {
                            currentGreen = childNote;
                        }
                    }
                }

                if (currentOpen != null || currentGreen != null)
                {
                    //cut off last green sustain early if there is another green note on it
                    if (lastGreenSustain != null &&
                        (currentOpen != null || openInLastGreenSustainChordBeforeConversion) &&
                        lastGreenSustain.Tick + lastGreenSustain.TickLength + sixteenthTickLength > note.Tick)
                    {
                        //if sustain would be cut off before starting remove sustain
                        if (note.Tick - lastGreenSustain.Tick <= sixteenthTickLength)
                        {
                            lastGreenSustain.TickLength = 0;
                            lastGreenSustain.TimeLength = 0;
                        }
                        else
                        {
                            lastGreenSustain.TickLength = note.Tick - sixteenthTickLength - lastGreenSustain.Tick;
                            lastGreenSustain.TimeLength = syncTrack.TickToTime(note.Tick - sixteenthTickLength) -
                                lastGreenSustain.Time;
                        }
                    }
                }

                //P note without G
                if (currentGreen == null && currentOpen != null)
                {
                    currentOpen.Fret = FiveFretGuitarFret.Green.Convert();
                    //or the mask with the mask for green and then And the mask with all bits except purple
                    note.NoteMask = noteMaskGreen | note.NoteMask & ~noteMaskOpen;
                    if (currentOpen.IsChild)
                    {
                        currentOpen.NoteMask = noteMaskGreen;
                    }

                    if (currentOpen.IsSustain)
                    {
                        lastGreenSustain = currentOpen;
                        openInLastGreenSustainChordBeforeConversion = true;
                    }
                }
                //PG chords
                else if (currentGreen != null && currentOpen != null)
                {
                    //open note is the parent note
                    if (currentOpen == note)
                    {
                        currentOpen.Fret = FiveFretGuitarFret.Green.Convert();
                        //or the mask with the mask for green and then And the mask with all bits except purple
                        note.NoteMask = note.NoteMask & ~noteMaskOpen;
                        currentOpen.TickLength = Math.Max(currentOpen.TickLength, currentGreen.TickLength);
                        currentOpen.TimeLength = Math.Max(currentOpen.TimeLength, currentGreen.TimeLength);
                        currentOpen.ChildNotes.Remove(currentGreen);
                        if (currentOpen.IsSustain)
                        {
                            lastGreenSustain = currentOpen;
                            openInLastGreenSustainChordBeforeConversion = true;
                        }
                    }
                    //any note other than open note is the parent note
                    else
                    {
                        //or the mask with the mask for green and then And the mask with all bits except purple
                        note.NoteMask = note.NoteMask & ~noteMaskOpen;
                        currentGreen.TickLength = Math.Max(currentOpen.TickLength, currentGreen.TickLength);
                        currentGreen.TimeLength = Math.Max(currentOpen.TimeLength, currentGreen.TimeLength);
                        note.ChildNotes.Remove(currentOpen);
                        if (currentGreen.IsSustain)
                        {
                            lastGreenSustain = currentGreen;
                            openInLastGreenSustainChordBeforeConversion = true;
                        }
                    }
                }
                //green notes just need to be set as last sustain
                else if (currentGreen != null && currentOpen == null)
                {
                    if (currentGreen.IsSustain)
                    {
                        lastGreenSustain = currentGreen;
                        openInLastGreenSustainChordBeforeConversion = false;
                    }
                }

                //set note to strum if it would be a hopo on the same chord
                if ((noteMaskGreen & note.NoteMask) != 0 && lastNoteMask == note.NoteMask && note.IsHopo)
                {
                    note.Type = GuitarNoteType.Strum;
                    if (note.IsParent)
                    {
                        foreach (var childNote in note.ChildNotes)
                        {
                            childNote.Type = GuitarNoteType.Strum;
                        }
                    }
                }

                //reset current notes for next iteration
                currentGreen = null;
                currentOpen = null;
                lastNoteMask = note.NoteMask;
            }
        }

        // Transposes all ranges into the first range.
        // For example, if the song starts in the GRY range and later shifts to the RYB or YBO ranges
        // the notes in the later ranges are transposed into the first range. (If there was a case where the
        // original range was GRY and a subsequent range was RYBO, which shouldn't actually happen, RYBO would
        // be transposed into GRYB)
        public static void CompressGuitarRange(this InstrumentDifficulty<GuitarNote> difficulty)
        {
            // Bail if there aren't actually any range shift events
            if (difficulty.RangeShiftEvents.Count == 0)
            {
                return;
            }

            // Bail if the first shift event is after the first note. We could try to guess, but we may well end up
            // with a really bad chart if we do.
            if (difficulty.RangeShiftEvents[0].Time > difficulty.Notes[0].Time)
            {
                return;
            }

            var shifts = difficulty.RangeShiftEvents;

            int firstRange = shifts[0].Range;

            // `+ 1` because all the lane indices in the enum are offset by one... for some reason
            Span<uint> laneEndTicks = new uint[EnumExtensions<FiveFretGuitarFret>.Count + 1];

            for (int noteIndex = 0, shiftIndex = 0; noteIndex < difficulty.Notes.Count;)
            {
                var note = difficulty.Notes[noteIndex];

                while (shiftIndex + 1 < shifts.Count && note.Time >= shifts[shiftIndex + 1].Time)
                {
                    shiftIndex++;
                }

                int shiftAmount = firstRange - shifts[shiftIndex].Range;
                if (shiftAmount > 0)
                {
                    int maxFretAllowed = (int) FiveFretGuitarFret.Orange - shiftAmount;

                    for (int j = 0; j < note.ChildNotes.Count;)
                    {
                        var child = note.ChildNotes[j];
                        if (child.Fret != (int) FiveFretGuitarFret.Open)
                        {
                            if (child.Fret > maxFretAllowed || note.Tick < laneEndTicks[child.Fret + shiftAmount])
                            {
                                note.NoteMask &= ~child.NoteMask;
                                note.DisjointMask &= ~child.DisjointMask;
                                note.ChildNotes.RemoveAt(j);
                                continue;
                            }

                            child.Fret += shiftAmount;
                            child.NoteMask <<= shiftAmount;
                            child.DisjointMask <<= shiftAmount;
                        }

                        ++j;
                    }

                    if (note.Fret != (int) FiveFretGuitarFret.Open &&
                        (note.Fret > maxFretAllowed || note.Tick < laneEndTicks[note.Fret - shiftAmount]))
                    {
                        // This will automatically create a mask with all the frets pre-shifted
                        // if child notes still exist.
                        difficulty.Notes.RemoveNoteAt(noteIndex);
                        if (note.ChildNotes.Count == 0)
                        {
                            continue;
                        }

                        note = difficulty.Notes[noteIndex];
                    }
                    else
                    {
                        if (note.Fret != (int) FiveFretGuitarFret.Open)
                        {
                            note.Fret += shiftAmount;
                        }

                        if ((note.NoteMask & GuitarEngine.OPEN_MASK) != 0)
                        {
                            note.NoteMask = ((note.NoteMask & ~GuitarEngine.OPEN_MASK) << shiftAmount) |
                                GuitarEngine.OPEN_MASK;
                            note.DisjointMask = ((note.DisjointMask & ~GuitarEngine.OPEN_MASK) << shiftAmount) |
                                GuitarEngine.OPEN_MASK;
                        }
                        else
                        {
                            note.NoteMask <<= shiftAmount;
                            note.DisjointMask <<= shiftAmount;
                        }
                    }
                }
                else if (shiftAmount < 0)
                {
                    shiftAmount = -shiftAmount;
                    int minFretAllowed = (int) FiveFretGuitarFret.Green + shiftAmount;

                    for (int j = 0; j < note.ChildNotes.Count;)
                    {
                        var child = note.ChildNotes[j];
                        if (child.Fret != (int) FiveFretGuitarFret.Open)
                        {
                            if (child.Fret < minFretAllowed || note.Tick < laneEndTicks[child.Fret - shiftAmount])
                            {
                                note.NoteMask &= ~child.NoteMask;
                                note.DisjointMask &= ~child.DisjointMask;
                                note.ChildNotes.RemoveAt(j);
                                continue;
                            }

                            child.Fret -= shiftAmount;
                            child.NoteMask >>= shiftAmount;
                            child.DisjointMask >>= shiftAmount;
                        }

                        ++j;
                    }

                    if (note.Fret != (int) FiveFretGuitarFret.Open &&
                        (note.Fret < minFretAllowed || note.Tick < laneEndTicks[note.Fret - shiftAmount]))
                    {
                        // This will automatically create a mask with all the frets pre-shifted
                        // if child notes still exist.
                        difficulty.Notes.RemoveNoteAt(noteIndex);
                        if (note.ChildNotes.Count == 0)
                        {
                            continue;
                        }

                        note = difficulty.Notes[noteIndex];
                    }
                    else
                    {
                        if (note.Fret != (int) FiveFretGuitarFret.Open)
                        {
                            note.Fret -= shiftAmount;
                        }

                        if ((note.NoteMask & GuitarEngine.OPEN_MASK) != 0)
                        {
                            note.NoteMask = ((note.NoteMask & ~GuitarEngine.OPEN_MASK) >> shiftAmount) |
                                GuitarEngine.OPEN_MASK;
                            note.DisjointMask = ((note.DisjointMask & ~GuitarEngine.OPEN_MASK) >> shiftAmount) |
                                GuitarEngine.OPEN_MASK;
                        }
                        else
                        {
                            note.NoteMask >>= shiftAmount;
                            note.DisjointMask >>= shiftAmount;
                        }
                    }
                }

                // Don't add the trackers for open fret
                if (note.Fret != (int) FiveFretGuitarFret.Open)
                {
                    laneEndTicks[note.Fret] = note.Tick + note.TickLength;
                }

                foreach (var childNote in note.ChildNotes)
                {
                    if (note.Fret != (int) FiveFretGuitarFret.Open)
                    {
                        laneEndTicks[childNote.Fret] = note.Tick + childNote.TickLength;
                    }
                }

                ++noteIndex;
            }

            shifts.RemoveRange(1, shifts.Count - 1);
        }

        public static void ShuffleNotes(this InstrumentDifficulty<GuitarNote> difficulty, SyncTrack syncTrack, int seed, bool shuffleInGroups)
        {
            // Quarter note
            uint maxIdenticalNoteTickDistance = syncTrack.Resolution;
            if (difficulty.Difficulty is Difficulty.Beginner)
            {
                // Beginner is only wildcards. No.
                return;
            }

            var random = new Random(seed);
            var activeNotes = new GuitarNote?[(int) FiveFretGuitarFret.Wildcard + 1]; // GRYBO + 6th fret on 6F + Open + Wildcard

            var currentNotes = new List<GuitarNote>();
            var availableFrets = new List<int>();
            var selectedFrets = new List<int>();

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
                    for (int i = noteIndex + 1; i < difficulty.Notes.Count; i++)
                    {
                        var currentNote = difficulty.Notes[i];
                        if (currentNote.NoteMask == parent.NoteMask &&
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
                selectedFrets.Clear();
                availableFrets.Clear();

                // Clear no-longer-active notes
                for (int i = 0; i < activeNotes.Length; i++)
                {
                    if (activeNotes[i] is not { } activeNote) continue; // Already inactive

                    bool isActive = activeNote.TickLength == 0
                        ? activeNote.TickEnd == parent.Tick // If the note has no length, its end is inclusive
                        : activeNote.TickEnd > parent.Tick; // Otherwise, the end is exclusive

                    if (!isActive) activeNotes[i] = null;
                }

                // Set up grab bag
                if (activeNotes[1] == null) availableFrets.Add(1);
                if (activeNotes[2] == null) availableFrets.Add(2);
                if (activeNotes[3] == null) availableFrets.Add(3);
                if (activeNotes[4] == null) availableFrets.Add(4);
                if (activeNotes[5] == null) availableFrets.Add(5);
                availableFrets.Shuffle(random);

                // Retrieve notes to shuffle
                foreach (var note in parent.AllNotes)
                {
                    // Don't shuffle open notes
                    if (note.Fret == (int) FiveFretGuitarFret.Open)
                    {
                        continue;
                    }

                    currentNotes.Add(note);
                }

                for (int i = 1; i <= 5; i++)
                {
                    if ((lastShuffledMask & (1 << (i - 1))) != 0) availableFrets.Remove(i);
                }

                // Pick a set of random notes for the chord
                for (int i = selectedFrets.Count; i < currentNotes.Count; i++)
                {
                    if (availableFrets.Count < 1)
                    {
                        // Ignore un-shuffleable notes
                        var note = currentNotes[i];
                        YargLogger.LogFormatWarning("Cannot shuffle note at {0:0.000} ({1}), removing.", note.Time,
                            note.Tick);
                        continue;
                    }

                    int randomFret = availableFrets.PopRandom(random);
                    selectedFrets.Add(randomFret);
                }

                // Remove any notes that didn't make the cut
                for (int i = 0; i < parent.ChildNotes.Count; i++)
                {
                    var child = parent.ChildNotes[i];
                    if (!currentNotes.Contains(child) &&
                        child.Fret != (int) FiveFretGuitarFret.Open) // Don't remove open notes
                        parent.RemoveChildNote(child);
                }

                // Skip open notes and 5-note chords
                if (currentNotes.Count is < 1 or >= 5) continue;

                // Sort notes/frets to prepare for the next step
                currentNotes.Sort((left, right) => left.Fret.CompareTo(right.Fret));
                selectedFrets.Sort();

                // Push all notes to the right, to prevent intermediate overlaps
                for (int i = 0; i < currentNotes.Count; i++)
                {
                    currentNotes[^(i + 1)].Fret = 5 - i;
                }

                // Apply shuffled frets
                YargLogger.Assert(currentNotes.Count == selectedFrets.Count,
                    "Current notes and selected frets must be the same size!");
                var newChordMask = 0;
                for (int i = 0; i < selectedFrets.Count; i++)
                {
                    int fret = selectedFrets[i];
                    var note = currentNotes[i];

                    note.Fret = fret;
                    newChordMask |= 1 << (fret - 1);
                    activeNotes[fret] = note;
                }

                // Fix the note mask to match the new frets, but keep the open/wildcard bits
                parent.NoteMask = (parent.NoteMask & ~0b11111) | (newChordMask & 0b11111);
                parent.DisjointMask = 1 << (parent.Fret - 1);
                lastShuffledMask = parent.NoteMask;

                if (shuffleInGroups && identicalNoteCount > 0)
                {
                    for (int j = 1; j <= identicalNoteCount; j++)
                    {
                        var currentNote = difficulty.Notes[noteIndex + j];
                        // Copy everything from the parent note to the next note, except for the time/tick
                        CopyNoteFrets(parent, currentNote);
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

                static void CopyNoteFrets(GuitarNote source, GuitarNote destination)
                {
                    destination.Fret = source.Fret;
                    destination.NoteMask = source.NoteMask;
                    destination.DisjointMask = source.DisjointMask;
                    for (int i = 0; i < source.ChildNotes.Count; i++)
                    {
                        var sourceChild = source.ChildNotes[i];
                        var destChild = destination.ChildNotes[i];
                        destChild.Fret = sourceChild.Fret;
                        destChild.NoteMask = sourceChild.NoteMask;
                        destChild.DisjointMask = sourceChild.DisjointMask;
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

                        CopyNoteFrets(parentNote, currentNote);
                    }
                }
            }
        }

        // Used for all wildcards and all opens
        public static void ConvertAllToFret(this InstrumentDifficulty<GuitarNote> difficulty, SyncTrack syncTrack,
            int fret)
        {
            if (difficulty.Difficulty is Difficulty.Beginner)
            {
                // No point, Beginner is just easy + all wildcards. just use easy if you want to play a specific fret.
                return;
            }

            var sixteenthTickLength = syncTrack.Resolution / 4;
            GuitarNote? lastSustain = null;
            foreach (var note in difficulty.Notes)
            {
                note.Fret = fret;
                note.NoteMask = 1 << (fret - 1);
                note.DisjointMask = 1 << (fret - 1);
                note.ChildNotes.Clear();
                note.GuitarFlags = GuitarNoteFlags.None;
                if (note.Flags.HasFlag(NoteFlags.Trill))
                {
                    note.ClearFlag(NoteFlags.Trill);
                    note.ActivateFlag(NoteFlags.Tremolo);
                }

                if (lastSustain != null && note.Tick - sixteenthTickLength <= lastSustain.TickEnd)
                {
                    TruncateSustain(lastSustain, note.Tick - sixteenthTickLength);
                }

                if (note.IsSustain)
                {
                    lastSustain = note;
                }
                else
                {
                    lastSustain = null;
                }
            }
        }

        // Contrary to its name, this just adds one note to every chord
        public static void DoubleNotes(this InstrumentDifficulty<GuitarNote> difficulty, SyncTrack syncTrack)
        {
            // Fixed mapping for any chords that the default handling does weirdly
            // ...yes, it's just one right now. blame its11
            Dictionary<int, int> chordMaskToAddedFret = new()
            {
                [ChordMask(FiveFretGuitarFret.Green, FiveFretGuitarFret.Orange)] = (int) FiveFretGuitarFret.Blue,
            };

            var activeNotes = new GuitarNote?[(int) FiveFretGuitarFret.Wildcard + 1]; // GRYBO + 6th fret on 6F + Open + Wildcard

            var sixteenthTick = syncTrack.Resolution / 4;

            int? previousPlacedFret = null;
            GuitarNote? previousNote = null;
            int previousNoteOriginalMask = 0;

            foreach (var note in difficulty.Notes)
            {
                if (previousNote is not null)
                {
                    // Update the active sustains set
                    foreach (var subNote in previousNote.AllNotes)
                    {
                        if (subNote.TickLength > 0)
                        {
                            activeNotes[subNote.Fret] = subNote;
                        }
                    }

                    for (int i = 0; i < activeNotes.Length; i++)
                    {
                        var activeNote = activeNotes[i];
                        if (activeNote is not null && activeNote.TickEnd < note.Tick)
                        {
                            activeNotes[i] = null;
                        }
                    }
                }

                if (previousNote is not null && previousPlacedFret.HasValue &&
                    previousNoteOriginalMask == note.NoteMask &&
                    activeNotes[previousPlacedFret.Value] is null)
                {
                    // Previous note was the same, apply the same chord and continue
                    AddFret(note, previousPlacedFret.Value);
                    previousNote = note;
                    MarkExtendedSustains(note);
                    continue;
                }

                previousNoteOriginalMask = note.NoteMask;

                // Fixed mapping for any chords that the default handling does weirdly, IF there is no active sustain on that fret
                if (chordMaskToAddedFret.TryGetValue(note.NoteMask & 0b11111, out int addedFret) &&
                    activeNotes[addedFret] == null)
                {
                    AddFret(note, addedFret);
                    previousNote = note;
                    previousPlacedFret = addedFret;
                    MarkExtendedSustains(note);
                    continue;
                }

                if (note.NoteMask == GuitarEngine.OPEN_MASK)
                {
                    previousNote = note;
                    previousPlacedFret = null;
                    continue;
                }

                int noteCountExcludingOpens = note.ChildNotes.Count + 1;
                var usedMask = note.NoteMask;

                if ((note.NoteMask & GuitarEngine.OPEN_MASK) != 0)
                {
                    noteCountExcludingOpens--;
                }

                int fret = (int) FiveFretGuitarFret.Open;
                bool encounteredUsedFret = false;
                bool avoidedFret = false;
                int avoidedActiveSustain = -1;
                // Loop through the frets and find a fret that is not used in the chord and does not have an active sustain on it
                for (int i = (int) FiveFretGuitarFret.Green; i <= (int) FiveFretGuitarFret.Orange; i++)
                {
                    if ((usedMask & (1 << (i - 1))) != 0)
                    {
                        encounteredUsedFret = true;
                        continue;
                    }

                    if (activeNotes[i] is not null)
                    {
                        YargLogger.LogFormatTrace("Avoiding fret {0} at time {1} because of active sustain", i,
                            note.Time);
                        avoidedActiveSustain = i;
                        continue;
                    }

                    if (noteCountExcludingOpens <= 3 && previousNote is not null && previousPlacedFret.HasValue &&
                        previousNote.NoteMask ==
                        (note.NoteMask | (1 << (i - 1))))
                    {
                        // Avoid using the previous double notes chord on a different chord if possible
                        YargLogger.LogFormatTrace("Avoiding fret {0} at time {1} because of previous chord",
                            previousPlacedFret, note.Time);
                        avoidedFret = true;
                        continue;
                    }

                    fret = i;
                    if (encounteredUsedFret)
                    {
                        break;
                    }
                }

                if (fret is (int) FiveFretGuitarFret.Open)
                {
                    if (avoidedFret)
                    {
                        // No other option, just use the previous chord's fret
                        YargLogger.LogFormatTrace("Forced to place note at fret {0} on previous chord at time {1}",
                            previousPlacedFret, note.Time);
                        fret = previousPlacedFret!.Value;
                    }
                    else if (avoidedActiveSustain != -1)
                    {
                        // No other option AND cannot use the previous chord's fret, so just use the active sustain's fret
                        YargLogger.LogFormatDebug("Forced to place note at fret {0} on active sustain at time {1}",
                            avoidedActiveSustain, note.Time);
                        fret = avoidedActiveSustain;
                        TruncateSustain(activeNotes[avoidedActiveSustain]!, note.Tick - sixteenthTick);
                    }
                    // REALLY no option (5-note chord) - make it a 5-note open chord instead!
                }

                var newNote = AddFret(note, fret);
                if (newNote.TickLength > 0)
                {
                    // Add the new note to the active sustains set
                    activeNotes[fret] = newNote;
                }

                previousPlacedFret = fret;
                previousNote = note;

                MarkExtendedSustains(note);
            }

            return;

            void MarkExtendedSustains(GuitarNote current)
            {
                // A sustain overlapped by a later note is extended (forwards),
                // and a sustain placed on top of a previous sustain is extended as well
                // (backwards, only if it is itself a sustain).
                bool overlappedPreviousSustain = false;
                foreach (var activeNote in activeNotes)
                {
                    if (activeNote is null || activeNote.Tick == current.Tick)
                    {
                        continue;
                    }

                    if (activeNote.TickEnd > current.Tick)
                    {
                        activeNote.ActivateGuitarFlag(GuitarNoteFlags.ExtendedSustain);
                        overlappedPreviousSustain = true;
                    }
                }

                if (overlappedPreviousSustain)
                {
                    foreach (var subNote in current.AllNotes)
                    {
                        if (subNote.TickLength > 0)
                        {
                            subNote.ActivateGuitarFlag(GuitarNoteFlags.ExtendedSustain);
                        }
                    }
                }
            }

            GuitarNote AddFret(GuitarNote note, int fret)
            {
                var currentNote = note;
                uint tickEnd = note.TickEnd;
                while (currentNote.NextNote is not null && currentNote.NextNote.Tick < tickEnd)
                {
                    currentNote = currentNote.NextNote;
                    if (currentNote.Fret != fret)
                    {
                        continue;
                    }

                    tickEnd = currentNote.Tick - sixteenthTick;
                    break;
                }

                var timeEnd = syncTrack.TickToTime(tickEnd);
                var newNote = new GuitarNote(fret, note.Type, note.GuitarFlags,
                    note.Flags, note.Time, timeEnd - note.Time, note.Tick, tickEnd - note.Tick);
                newNote.ActivateFlag(note.Flags);
                note.AddChildNote(newNote);
                newNote.PreviousNote = note.PreviousNote;
                newNote.NextNote = note.NextNote;
                if (tickEnd != note.TickEnd)
                {
                    // At least one note ends at a different time to the rest, so mark all notes in the chord as disjoint
                    foreach (var subNote in note.ParentOrSelf.AllNotes)
                    {
                        subNote.ActivateGuitarFlag(GuitarNoteFlags.Disjoint);
                    }
                }

                return newNote;
            }
        }
    }
}