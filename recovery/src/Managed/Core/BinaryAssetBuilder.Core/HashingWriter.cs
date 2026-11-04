using System.IO;
using System.Text;

namespace BinaryAssetBuilder.Core;

internal class HashingWriter : TextWriter
{
	private uint m_runningHash;

	private const int HashingSize = 512;
	private readonly char[] characters = new char[HashingSize];
	private readonly byte[] asciiBytes = new byte[HashingSize];
	private int characterCount;

	public override Encoding Encoding => Encoding.Default;

	public HashingWriter(uint runningHash)
	{
		m_runningHash = runningHash;
	}

	public override void Write(char c)
	{
		characters[characterCount++] = c;
		if (characterCount == HashingSize) FlushBlock();
	}

	public override void Write(char[] buffer, int index, int count)
	{
		while (count > 0)
		{
			int copied = System.Math.Min(count, HashingSize - characterCount);
			System.Array.Copy(buffer, index, characters, characterCount, copied);
			characterCount += copied; index += copied; count -= copied;
			if (characterCount == HashingSize) FlushBlock();
		}
	}

	public override void Write(string str)
	{
		int index = 0;
		while (index < str.Length)
		{
			int copied = System.Math.Min(str.Length - index, HashingSize - characterCount);
			str.CopyTo(index, characters, characterCount, copied);
			characterCount += copied; index += copied;
			if (characterCount == HashingSize) FlushBlock();
		}
	}

	private uint HashBlock(int count)
	{
		for (int i = 0; i < count; i++)
		{
			char value = characters[i];
			// Preserve the SDK's ANSI conversion and UTF-16 byte-count behavior
			// for every non-ASCII block, including split surrogate pairs/DBCS.
			if (value >= 128) return HashProvider.GetTextHash(m_runningHash, new string(characters, 0, count));
			asciiBytes[i] = (byte)value;
		}
		return EALA.Hash.FastHash.GetHashCode(m_runningHash, asciiBytes, count);
	}

	private void FlushBlock()
	{
		m_runningHash = HashBlock(HashingSize);
		characterCount = 0;
	}

	public uint GetFinalHash()
	{
		// Finalization is observational: callers can finalize twice or append
		// more XML afterwards without changing the original 512-char grouping.
		return characterCount == 0 ? m_runningHash : HashBlock(characterCount);
	}
}
