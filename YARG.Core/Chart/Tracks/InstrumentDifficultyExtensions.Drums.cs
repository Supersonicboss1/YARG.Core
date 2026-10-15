using System.Collections.Generic;
using System.Linq;
using YARG.Core.Extensions;

namespace YARG.Core.Chart
{
    public partial class InstrumentDifficultyExtensions
    {
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
    }
}