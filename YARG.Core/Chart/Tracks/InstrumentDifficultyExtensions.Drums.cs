using System;

namespace YARG.Core.Chart
{
    public static partial class InstrumentDifficultyExtensions
    {
        public static void RemoveKickDrumNotes(this InstrumentDifficulty<DrumNote> difficulty)
        {
            var kickDrumPadIndex = difficulty.Instrument switch
            {
                Instrument.ProDrums      => (int) FourLaneDrumPad.Kick,
                Instrument.FourLaneDrums => (int) FourLaneDrumPad.Kick,
                Instrument.FiveLaneDrums => (int) FiveLaneDrumPad.Kick,
                _ => throw new InvalidOperationException("Cannot remove kick drum notes from non-drum track with " +
                    $"instrument {difficulty.Instrument}!")
            };
            for (int index = 0; index < difficulty.Notes.Count; index++)
            {
                var note = difficulty.Notes[index];
                if (note.Pad != kickDrumPadIndex)
                {
                    // This is not a kick drum note, but we have to check it's children too
                    int? childNoteKickIndex = null;
                    for (int i = 0; i < note.ChildNotes.Count; i++)
                    {
                        var childNote = note.ChildNotes[i];
                        if (childNote.Pad == kickDrumPadIndex)
                        {
                            childNoteKickIndex = i;
                            break;
                        }
                    }

                    if (childNoteKickIndex != null)
                    {
                        var newNote = note.CloneWithoutChildNotes();
                        for (int i = 0; i < note.ChildNotes.Count; i++)
                        {
                            if (i != childNoteKickIndex)
                            {
                                newNote.AddChildNote(note.ChildNotes[i]);
                            }
                        }

                        difficulty.Notes[index] = newNote;
                    }
                }
                else if (note.ChildNotes.Count > 0)
                {
                    // If the drum note has child notes, convert the first child note to a parent note,
                    // then assign the other child notes to this parent note.
                    // Finally, overwrite the drum note with the new parent note.
                    var firstChild = note.ChildNotes[0].CloneWithoutChildNotes();
                    for (int i = 1; i < note.ChildNotes.Count; i++)
                    {
                        firstChild.AddChildNote(note.ChildNotes[i]);
                    }

                    difficulty.Notes[index] = firstChild;
                }
                else
                {
                    // This is a single kick drum note
                    difficulty.Notes.RemoveAt(index);
                    index--;
                }
                difficulty.RemapNotes();
                difficulty.FixFlags();
            }
        }

        public static void SetDrumActivationFlags(this InstrumentDifficulty<DrumNote> difficulty, StarPowerActivationType activationType)
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
    }
}