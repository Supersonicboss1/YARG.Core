using YARG.Core.Extensions;

namespace YARG.Core.Chart
{
    public partial class InstrumentDifficultyExtensions
    {
        public static InstrumentDifficulty<TNote> Splice<TNote>(this InstrumentDifficulty<TNote> first,
            InstrumentDifficulty<TNote> second, double time) where TNote : Note<TNote>
        {
            var originalNotes = first.Notes;
            var newNotes = second.Notes;

            var originalPhrases = first.Phrases;
            var newPhrases = second.Phrases;

            var originalTextEvents = first.TextEvents;
            var newTextEvents = second.TextEvents;

            // We don't actually care about the old range shifts, they just confuse things
            var newRangeShiftEvents = second.RangeShiftEvents;

            // Do the splicing, taking everything at or before time from first, everything after time from second
            var combinedNotes = originalNotes.SpliceNotes(newNotes, time);
            var combinedPhrases = originalPhrases.Splice(newPhrases, time);
            var combinedTextEvents = originalTextEvents.Splice(newTextEvents, time);

            var newId = new InstrumentDifficulty<TNote>(second.Instrument, second.Difficulty,
                combinedNotes, combinedPhrases, combinedTextEvents, newRangeShiftEvents);

            return newId;
        }
    }
}