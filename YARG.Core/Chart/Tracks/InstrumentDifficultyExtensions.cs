using System;
using System.Collections.Generic;
using System.Linq;

namespace YARG.Core.Chart
{
    public static partial class InstrumentDifficultyExtensions
    {
        private static void FixFlags<TNote>(this InstrumentDifficulty<TNote> track)
            where TNote : Note<TNote>
        {
            const NoteFlags clearMask = NoteFlags.Solo | NoteFlags.SoloStart | NoteFlags.SoloEnd |
                NoteFlags.StarPower | NoteFlags.StarPowerStart | NoteFlags.StarPowerEnd
                | NoteFlags.BigRockEnding | NoteFlags.CodaEnd | NoteFlags.CodaStart | NoteFlags.Tremolo
                | NoteFlags.Trill | NoteFlags.LaneEnd | NoteFlags.LaneStart;
            var phraseIndexTracker = new Dictionary<PhraseType, int>
            {
                {
                    PhraseType.Solo, track.Phrases.FindIndex(p => p.Type == PhraseType.Solo)
                },
                {
                    PhraseType.StarPower, track.Phrases.FindIndex(p => p.Type == PhraseType.StarPower)
                },
                {
                    PhraseType.BigRockEnding, track.Phrases.FindIndex(p => p.Type == PhraseType.BigRockEnding)
                },
                {
                    PhraseType.Coda, track.Phrases.FindIndex(p => p.Type == PhraseType.Coda)
                },
                {
                    PhraseType.TrillLane, track.Phrases.FindIndex(p => p.Type == PhraseType.TrillLane)
                },
                {
                    PhraseType.TremoloLane, track.Phrases.FindIndex(p => p.Type == PhraseType.TremoloLane)
                },
                {
                    PhraseType.DrumFill, track.Phrases.FindIndex(p => p.Type == PhraseType.DrumFill)
                }
            };
            for (int i = 0; i < track.Notes.Count; i++)
            {
                var note = track.Notes[i];
                note.Flags &= ~clearMask;
                for (int phraseTypeIndex = 0; phraseTypeIndex < phraseIndexTracker.Count; phraseTypeIndex++)
                {
                    (var phraseType, int index) = phraseIndexTracker.ElementAt(phraseTypeIndex);
                    if (index > track.Phrases.Count - 1 || index < 0)
                    {
                        continue;
                    }
                    var phrase = track.Phrases[index];
                    if (note.Tick < phrase.Tick)
                    {
                        continue;
                    }

                    var flagsToAdd = NoteFlags.None;
                    if (i == 0 || track.Notes[i - 1].Tick < phrase.Tick)
                    {
                        switch (phraseType)
                        {
                            case PhraseType.Solo:
                                flagsToAdd |= NoteFlags.SoloStart;
                                break;
                            case PhraseType.StarPower:
                                flagsToAdd |= NoteFlags.StarPowerStart;
                                break;
                            case PhraseType.Coda:
                                flagsToAdd |= NoteFlags.CodaStart;
                                break;
                            case PhraseType.TremoloLane:
                            case PhraseType.TrillLane:
                                flagsToAdd |= NoteFlags.LaneStart;
                                break;
                        }
                    }

                    switch (phraseType)
                    {
                        case PhraseType.Solo:
                            flagsToAdd |= NoteFlags.Solo;
                            break;
                        case PhraseType.StarPower:
                            flagsToAdd |= NoteFlags.StarPower;
                            break;
                        case PhraseType.BigRockEnding:
                            flagsToAdd |= NoteFlags.BigRockEnding;
                            break;
                        case PhraseType.TrillLane:
                            flagsToAdd |= NoteFlags.Trill;
                            break;
                        case PhraseType.TremoloLane:
                            flagsToAdd |= NoteFlags.Tremolo;
                            break;
                    }

                    if (i == track.Notes.Count - 1 || track.Notes[i + 1].Tick < phrase.Tick)
                    {
                        switch (phraseType)
                        {
                            case PhraseType.Solo:
                                flagsToAdd |= NoteFlags.SoloEnd;
                                break;
                            case PhraseType.StarPower:
                                flagsToAdd |= NoteFlags.StarPowerEnd;
                                break;
                            case PhraseType.Coda:
                                flagsToAdd |= NoteFlags.CodaEnd;
                                break;
                            case PhraseType.TremoloLane:
                            case PhraseType.TrillLane:
                                flagsToAdd |= NoteFlags.LaneEnd;
                                break;
                            case PhraseType.DrumFill:
                                if (note is DrumNote drumNote)
                                {
                                    foreach (var subNote in drumNote.AllNotes)
                                    {
                                        subNote.DrumFlags |= DrumNoteFlags.StarPowerActivator;
                                    }
                                }
                                break;
                        }
                        phraseIndexTracker[phraseType] = track.Phrases.FindIndex(phraseIndexTracker[phraseType] + 1, p => p.Type == phraseType);
                    }

                    foreach (var subNote in note.AllNotes)
                    {
                        subNote.Flags |= flagsToAdd;
                    }
                }
            }
        }

        private static void RemapNotes<TNote>(this InstrumentDifficulty<TNote> track)
            where TNote : Note<TNote>
        {
            for (int i = 0; i < track.Notes.Count; i++)
            {
                if (i == 0)
                {
                    track.Notes[i].PreviousNote = null;
                    continue;
                }
                if (i == track.Notes.Count - 1)
                {
                    track.Notes[i].NextNote = null;
                    continue;
                }
                track.Notes[i].PreviousNote = track.Notes[i - 1];
                track.Notes[i].NextNote = track.Notes[i + 1];
            }
        }
    }
}