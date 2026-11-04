/*
**  sagetools
**  Copyright 2026 Jonathan Wilson
**
**  This program is free software: you can redistribute it and/or modify
**  it under the terms of the GNU General Public License as published by
**  the Free Software Foundation, either version 3 of the License, or
**  (at your option) any later version.
**
**  This program is distributed in the hope that it will be useful,
**  but WITHOUT ANY WARRANTY; without even the implied warranty of
**  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
**  GNU General Public License for more details.
**
**  You should have received a copy of the GNU General Public License
**  along with this program.  If not, see <http://www.gnu.org/licenses/>.
*/

#include "mpegcommon.h"
void Bit_Reserve::reset()
{
    mInPtr = 0;
    mOutPtr = 0;
    mCached = 0;
}

unsigned int Bit_Reserve::hsstell() const
{
    return (mOutPtr << 3) - mCached;
}

unsigned int Bit_Reserve::hgetbits(unsigned int N)
{
    unsigned int val = 0;

    while (N > 0)
    {
        if (mCached == 0)
        {
            mCacheData = static_cast<unsigned int>(mBuffer[mOutPtr & (2048 - 1)] << 24);
            mOutPtr++;
            mCached = 8;
        }

        if (N < mCached)
        {
            val = (val << N) | (mCacheData >> (32 - N));
            mCacheData <<= N;
            mCached -= N;
            break;
        }
        else
        {
            val = (val << mCached) | (mCacheData >> (32 - mCached));
            N -= mCached;
            mCached = 0;
        }
    }

    return val;
}

unsigned int Bit_Reserve::hget1bit()
{
    if (mCached == 0)
    {
        mCacheData = static_cast<unsigned int>(mBuffer[mOutPtr & (2048 - 1)] << 24);
        mOutPtr++;
        mCached = 8;
    }

    const int bit = static_cast<int>(mCacheData >> 31);
    mCacheData <<= 1;
    mCached--;
    return static_cast<unsigned int>(bit);
}

void Bit_Reserve::hputbuf(int val)
{
    mBuffer[mInPtr] = static_cast<unsigned char>(val);
    mInPtr = (mInPtr + 1) & (2048 - 1);
}

void Bit_Reserve::rewindNbits(int n)
{
    mCached += n;
    mOutPtr -= (mCached >> 3);
    mCached &= 7;

    if (mCached)
    {
        mCacheData = static_cast<unsigned int>(mBuffer[(mOutPtr - 1) & (2048 - 1)] << (32 - mCached));
    }
}

void Bit_Reserve::rewindNbytes(int N)
{
    mOutPtr -= N;

    if (mCached)
    {
        mCacheData = static_cast<unsigned int>(mBuffer[(mOutPtr - 1) & (2048 - 1)] << (32 - mCached));
    }
}

