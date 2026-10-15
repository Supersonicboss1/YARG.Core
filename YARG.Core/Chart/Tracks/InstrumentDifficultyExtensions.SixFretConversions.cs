using System.Collections.Generic;

namespace YARG.Core.Chart
{
    public static partial class InstrumentDifficultyExtensions
    {
        /// <summary>
        /// Converts a 5-fret guitar difficulty into a 6-fret copy suitable for six-fret gameplay.
        ///
        /// The conversion is a direct per-chord lookup: 5-fret and 6-fret fret values coincide
        /// numerically (Green-Orange = Black1-White2, Open is shared), so chords map 1:1 except
        /// for a fixed table of chord substitutions (see Docs/5fret_to_6fret_conversion.md).
        /// The full GRYBO chord maps to holding all six frets.
        ///
        /// When a chord strikes into a column that already carries an active sustain, the earlier
        /// sustain is truncated at that tick (real charters cut sustains when a pattern needs
        /// the space). See Docs/5fret_to_6fret_conversion.md for the full description.
        /// </summary>
        public static InstrumentDifficulty<GuitarNote> ConvertFiveFretToSixFret(
            this InstrumentDifficulty<GuitarNote> difficulty)
        {
            var converted = new InstrumentDifficulty<GuitarNote>(difficulty);

            var activeSustains = new List<(uint TickEnd, int Fret, GuitarNote Source)>();
            foreach (var note in converted.Notes)
            {
                // Drop sustains that ended before this chord
                activeSustains.RemoveAll(s => s.TickEnd <= note.Tick);

                // Collect fretted members; Open/Wildcard members keep their shared value (7/8)
                int frettedMask = 0;
                foreach (var member in note.AllNotes)
                {
                    if (member.Fret is (int) FiveFretGuitarFret.Open or (int) FiveFretGuitarFret.Wildcard)
                    {
                        continue;
                    }

                    frettedMask |= 1 << (member.Fret - 1);
                }

                if (frettedMask == 0)
                {
                    continue;
                }

                FiveFretToSixFretSubstitutions.TryGetValue(frettedMask, out var substitution);

                // Apply the direct mapping: identity unless this chord shape has a substitution
                var placedMembers = new List<(GuitarNote Note, int Fret)>();
                int placedMask = 0;
                int placedColumns = 0;
                foreach (var member in note.AllNotes)
                {
                    if (member.Fret is (int) FiveFretGuitarFret.Open or (int) FiveFretGuitarFret.Wildcard)
                    {
                        continue;
                    }

                    int placedFret = substitution is null ? member.Fret : substitution[member.Fret];
                    SetSixFret(member, placedFret);
                    placedMask |= 1 << (placedFret - 1);
                    placedColumns |= 1 << ColumnOf(placedFret);
                    placedMembers.Add((member, placedFret));
                }

                // The full GRYBO chord maps to holding all six frets: add a real sixth member
                // on the remaining pad so the chord is complete both visually and mechanically.
                // Added after the mapping so its fret (White3) cannot collide with a member.
                if (frettedMask == FIVE_FRET_GRYBO_MASK)
                {
                    var sixthFret = new GuitarNote((int) SixFretGuitarFret.White3, note.Type,
                        GuitarNoteFlags.None, NoteFlags.None, note.Time, 0, note.Tick, 0);
                    note.AddChildNote(sixthFret);
                    placedMask |= 1 << ((int) SixFretGuitarFret.White3 - 1);
                    placedColumns |= 1 << ColumnOf((int) SixFretGuitarFret.White3);
                }

                // Truncate any active sustains this chord strikes into — a new sustain in a
                // column replaces the older one rather than stacking onto it
                for (int i = activeSustains.Count - 1; i >= 0; i--)
                {
                    var (_, fret, source) = activeSustains[i];
                    if ((placedColumns & (1 << ColumnOf(fret))) == 0)
                    {
                        continue;
                    }

                    TruncateSustain(source, note.Tick);
                    activeSustains.RemoveAt(i);
                }

                // Track this chord's sustains so later chords can truncate them
                foreach (var (member, placedFret) in placedMembers)
                {
                    if (member.TickLength > 0)
                    {
                        activeSustains.Add((member.Tick + member.TickLength, placedFret, member));
                    }
                }

                // Parent NoteMask: replace the fretted bits with the placed ones, keep open-like bits
                int openBits = note.NoteMask & ~SIX_FRET_FRETS_MASK;
                note.NoteMask = openBits | placedMask;
            }

            return converted;
        }

        /// <summary>
        /// Returns a copy of a 6-fret difficulty with every note's pads color-flipped
        /// (Black1↔White1, Black2↔White2, Black3↔White3). Used for lefty flip: the mirrored
        /// highway physically swaps the two pad rows, so notes must swap rows with it.
        /// Open and wildcard notes are unchanged.
        /// </summary>
        public static InstrumentDifficulty<GuitarNote> FlipSixFretColors(
            this InstrumentDifficulty<GuitarNote> difficulty)
        {
            var flipped = new InstrumentDifficulty<GuitarNote>(difficulty);
            foreach (var note in flipped.Notes)
            {
                int memberMaskUnion = 0;
                foreach (var member in note.AllNotes)
                {
                    if (member.Fret is >= (int) SixFretGuitarFret.Black1 and <= (int) SixFretGuitarFret.White3)
                    {
                        member.Fret = (member.Fret - 1 + 3) % 6 + 1;
                    }

                    member.NoteMask = FlipSixFretColorBits(member.NoteMask);
                    member.DisjointMask = FlipSixFretColorBits(member.DisjointMask);
                    memberMaskUnion |= member.NoteMask;
                }

                // The parent's NoteMask is the union of its members' masks
                note.NoteMask = memberMaskUnion;
            }

            return flipped;
        }

        /// <summary>Swaps the black-row bits (1-3) with the white-row bits (4-6), keeping open/wildcard bits.</summary>
        private static int FlipSixFretColorBits(int mask)
        {
            return ((mask & 0x38) >> 3) | ((mask & 0x07) << 3) | (mask & ~SIX_FRET_FRETS_MASK);
        }

        /// <summary>Masks for the six fretted 6-fret notes (Black1 through White3).</summary>
        private const int SIX_FRET_FRETS_MASK  = 0x3F;
        /// <summary>Mask of all five fretted 5-fret notes (Green through Orange).</summary>
        private const int FIVE_FRET_GRYBO_MASK = 0b11111;
        /// <summary>
        /// Substitutions for chord shapes whose direct mapping is undesirable, keyed by the
        /// chord's 5-fret fret mask. Each entry maps 5-fret fret values to 6-fret fret values;
        /// chords not listed here map 1:1. See Docs/5fret_to_6fret_conversion.md.
        /// </summary>
        private static readonly Dictionary<int, int[]> FiveFretToSixFretSubstitutions = new()
        {
            [ChordMask(FiveFretGuitarFret.Green, FiveFretGuitarFret.Red, FiveFretGuitarFret.Orange)] =
                FretMap((FiveFretGuitarFret.Green, SixFretGuitarFret.Black1),
                    (FiveFretGuitarFret.Red, SixFretGuitarFret.White1),
                    (FiveFretGuitarFret.Orange, SixFretGuitarFret.White3)),
            [ChordMask(FiveFretGuitarFret.Red, FiveFretGuitarFret.Blue, FiveFretGuitarFret.Orange)] =
                FretMap((FiveFretGuitarFret.Red, SixFretGuitarFret.Black2),
                    (FiveFretGuitarFret.Blue, SixFretGuitarFret.White2),
                    (FiveFretGuitarFret.Orange, SixFretGuitarFret.White3)),
            [ChordMask(FiveFretGuitarFret.Yellow, FiveFretGuitarFret.Blue, FiveFretGuitarFret.Orange)] =
                FretMap((FiveFretGuitarFret.Yellow, SixFretGuitarFret.White1),
                    (FiveFretGuitarFret.Blue, SixFretGuitarFret.White2),
                    (FiveFretGuitarFret.Orange, SixFretGuitarFret.Black3)),
            [ChordMask(FiveFretGuitarFret.Green, FiveFretGuitarFret.Red, FiveFretGuitarFret.Yellow, FiveFretGuitarFret.Orange)] =
                FretMap((FiveFretGuitarFret.Green, SixFretGuitarFret.Black1),
                    (FiveFretGuitarFret.Red, SixFretGuitarFret.White1),
                    (FiveFretGuitarFret.Yellow, SixFretGuitarFret.Black2),
                    (FiveFretGuitarFret.Orange, SixFretGuitarFret.White3)),
            [ChordMask(FiveFretGuitarFret.Green, FiveFretGuitarFret.Red, FiveFretGuitarFret.Blue, FiveFretGuitarFret.Orange)] =
                FretMap((FiveFretGuitarFret.Green, SixFretGuitarFret.Black1),
                    (FiveFretGuitarFret.Red, SixFretGuitarFret.White1),
                    (FiveFretGuitarFret.Blue, SixFretGuitarFret.Black3),
                    (FiveFretGuitarFret.Orange, SixFretGuitarFret.White3)),
            [ChordMask(FiveFretGuitarFret.Red, FiveFretGuitarFret.Yellow, FiveFretGuitarFret.Blue, FiveFretGuitarFret.Orange)] =
                FretMap((FiveFretGuitarFret.Red, SixFretGuitarFret.Black1),
                    (FiveFretGuitarFret.Yellow, SixFretGuitarFret.White1),
                    (FiveFretGuitarFret.Blue, SixFretGuitarFret.White2),
                    (FiveFretGuitarFret.Orange, SixFretGuitarFret.White3)),
            [ChordMask(FiveFretGuitarFret.Green, FiveFretGuitarFret.Red, FiveFretGuitarFret.Yellow, FiveFretGuitarFret.Blue, FiveFretGuitarFret.Orange)] =
                FretMap((FiveFretGuitarFret.Green, SixFretGuitarFret.Black1),
                    (FiveFretGuitarFret.Red, SixFretGuitarFret.White1),
                    (FiveFretGuitarFret.Yellow, SixFretGuitarFret.Black2),
                    (FiveFretGuitarFret.Blue, SixFretGuitarFret.White2),
                    (FiveFretGuitarFret.Orange, SixFretGuitarFret.Black3)),
        };

        /// <summary>Column index (0-2) of a fretted 6-fret value: Black1/White1 = 0, etc.</summary>
        private static int ColumnOf(int fret)
        {
            return (fret - 1) % 3;
        }

        private static void SetSixFret(GuitarNote member, int fret)
        {
            member.Fret = fret;
            int mask = 1 << (fret - 1);
            member.DisjointMask = (member.DisjointMask & ~SIX_FRET_FRETS_MASK) | mask;
            if (member.IsChild)
            {
                member.NoteMask = (member.NoteMask & ~SIX_FRET_FRETS_MASK) | mask;
            }
        }
    }
}