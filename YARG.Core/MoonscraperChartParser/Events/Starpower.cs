﻿// Copyright (c) 2016-2020 Alexander Ong
// See LICENSE in project root for license information.

using System;

namespace MoonscraperChartEditor.Song
{
    [Serializable]
    public class Starpower : ChartObject
    {
        [Flags]
        public enum Flags
        {
            None = 0,

            // RB Pro Drums
            ProDrums_Activation = 1 << 0,
        }

        private readonly ID _classID = ID.Starpower;
        public override int classID => (int)_classID;

        public uint length;
        public Flags flags = Flags.None;

        public Starpower(uint _position, uint _length, Flags _flags = Flags.None) : base(_position)
        {
            length = _length;
            flags = _flags;
        }

        public uint GetCappedLengthForPos(uint pos)
        {
            uint newLength;
            if (pos > tick)
                newLength = pos - tick;
            else
                newLength = 0;

            Starpower nextSp = null;
            if (moonSong != null && moonChart != null)
            {
                int arrayPos = SongObjectHelper.FindClosestPosition(this, moonChart.starPower);
                if (arrayPos == SongObjectHelper.NOTFOUND)
                    return newLength;

                while (arrayPos < moonChart.starPower.Count - 1 && moonChart.starPower[arrayPos].tick <= tick)
                {
                    ++arrayPos;
                }

                if (moonChart.starPower[arrayPos].tick > tick)
                    nextSp = moonChart.starPower[arrayPos];

                if (nextSp != null)
                {
                    // Cap sustain length
                    if (nextSp.tick < tick)
                        newLength = 0;
                    else if (pos > nextSp.tick)
                        // Cap sustain
                        newLength = nextSp.tick - tick;
                }
                // else it's the only starpower or it's the last starpower 
            }

            return newLength;
        }
    }
}