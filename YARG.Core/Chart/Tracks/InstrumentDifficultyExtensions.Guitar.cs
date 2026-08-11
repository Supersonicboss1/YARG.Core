using System;
using System.Collections.Generic;
using System.Linq;
using YARG.Core.Engine.Guitar;
using YARG.Core.Extensions;
using YARG.Core.Logging;

namespace YARG.Core.Chart
{
    public static partial class InstrumentDifficultyExtensions
    {
        private static void FixSustains(this InstrumentDifficulty<GuitarNote> track, SyncTrack syncTrack)
        {
            // We need to cut all sustains a 16th note before the next note of that type.
            uint sixteenthTickLength = syncTrack.Resolution / 4;
            var sustainSet = new Dictionary<int, GuitarNote>();
            for (int i = 0; i < track.Notes.Count; i++)
            {
                var currentNote = track.Notes[i];
                foreach (var subNote in currentNote.AllNotes)
                {
                    if (subNote.TickLength == 0)
                    {
                        continue;
                    }
                    // First note of type
                    if (sustainSet.TryAdd(subNote.Fret, subNote))
                    {
                        continue;
                    }
                    if (sustainSet.TryGetValue(subNote.Fret, out var existingNote) && existingNote.Tick + existingNote.TickLength > subNote.Tick)
                    {
                        // This is a sustain, we need to cut it
                        YargLogger.LogFormatDebug("Cutting sustain {0} at time {1} to length {2}", existingNote, existingNote.Time, subNote.Tick - existingNote.Tick - sixteenthTickLength);
                        existingNote.TickLength = subNote.Tick - existingNote.Tick - sixteenthTickLength;
                        sustainSet[subNote.Fret] = existingNote;
                    }
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
            GuitarNote? currentGreen = null;
            GuitarNote? currentOpen = null;
            int lastNoteMask = 0;
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
                    }
                    //any note other than open note is the parent note
                    else
                    {
                        //or the mask with the mask for green and then And the mask with all bits except purple
                        note.NoteMask = note.NoteMask & ~noteMaskOpen;
                        currentGreen.TickLength = Math.Max(currentOpen.TickLength, currentGreen.TickLength);
                        currentGreen.TimeLength = Math.Max(currentOpen.TimeLength, currentGreen.TimeLength);
                        note.ChildNotes.Remove(currentOpen);
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
            difficulty.FixSustains(syncTrack);
        }

        // Contrary to its name, this just adds one note to every chord
        public static void DoubleNotes(this InstrumentDifficulty<GuitarNote> difficulty, SyncTrack syncTrack)
        {
            FiveFretGuitarFret? previousPlacedNote = null;
            GuitarNote? previousNote = null;

            foreach (var note in difficulty.Notes)
            {
                if (previousNote is not null && (previousNote.NoteMask & ~(1 << previousPlacedNote?.Convert() - 1)) == note.NoteMask)
                {
                    // Previous note was the same, apply the same chord and continue
                    var n = new GuitarNote(previousPlacedNote!.Value, note.Type, note.GuitarFlags,
                        note.Flags, note.Time, note.TimeLength, note.Tick, note.TickLength);
                    n.Flags |= note.Flags;
                    note.AddChildNote(n);
                    previousNote = note;
                    continue;
                }
                int noteCountExcludingOpens = note.ChildNotes.Count + 1;
                var usedMask = note.NoteMask;

                if ((note.NoteMask & GuitarEngine.OPEN_MASK) != 0)
                {
                    noteCountExcludingOpens--;
                }

                if (note.NoteMask == GuitarEngine.OPEN_MASK)
                {
                    // kinda sucks, but open chords are not really in the spirit of doubling
                    continue;
                }

                if (noteCountExcludingOpens <= 3 && previousPlacedNote is not null &&
                    (previousNote?.NoteMask | (previousPlacedNote.Value.Convert() - 1)) == (note.NoteMask | (previousPlacedNote.Value.Convert() - 1)))
                {
                    // Avoid using the previous double notes chord on a different chord if possible
                    YargLogger.LogFormatDebug("Avoiding fret {0} at time {1} because of previous chord", previousPlacedNote, note.Time);
                    // Figure out what fret would lead to an identical chord if placed on
                    var differenceMask = previousNote.NoteMask ^ note.NoteMask;
                    if (differenceMask != 0)
                    {
                        var fretToAvoid = (FiveFretGuitarFret) (Math.Log(differenceMask, 2) + 1);
                        YargLogger.LogFormatDebug("Avoiding fret {0} at time {1} because it would lead to an identical chord", fretToAvoid, note.Time);
                        usedMask |= 1 << (fretToAvoid.Convert() - 1);
                    }
                }

                var fret = FiveFretGuitarFret.Open;
                bool encounteredUsedFret = false;
                for (int i = 1; i <= 5; i++)
                {
                    if ((usedMask & (1 << (i - 1))) != 0)
                    {
                        encounteredUsedFret = true;
                        continue;
                    }

                    fret = (FiveFretGuitarFret) i;
                    if (encounteredUsedFret)
                    {
                        break;
                    }
                }
                var newNote = new GuitarNote(fret, note.Type, note.GuitarFlags,
                    note.Flags, note.Time, note.TimeLength, note.Tick, note.TickLength);
                YargLogger.LogFormatDebug("Doubling note {0} at time {1} with fret {2}", note, note.Time, fret);
                newNote.Flags |= note.Flags;
                note.AddChildNote(newNote);
                previousPlacedNote = fret;
                previousNote = note;
            }

            difficulty.FixSustains(syncTrack);
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
                    int maxFretAllowed = (int)FiveFretGuitarFret.Orange - shiftAmount;

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
                            note.NoteMask     = ((note.NoteMask     & ~GuitarEngine.OPEN_MASK) << shiftAmount) | GuitarEngine.OPEN_MASK;
                            note.DisjointMask = ((note.DisjointMask & ~GuitarEngine.OPEN_MASK) << shiftAmount) | GuitarEngine.OPEN_MASK;
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
                    int minFretAllowed = (int)FiveFretGuitarFret.Green + shiftAmount;

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
                            note.NoteMask     = ((note.NoteMask     & ~GuitarEngine.OPEN_MASK) >> shiftAmount) | GuitarEngine.OPEN_MASK;
                            note.DisjointMask = ((note.DisjointMask & ~GuitarEngine.OPEN_MASK) >> shiftAmount) | GuitarEngine.OPEN_MASK;
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
    }
}