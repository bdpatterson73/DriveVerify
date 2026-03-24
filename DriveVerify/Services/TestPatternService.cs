using System.Security.Cryptography;

namespace DriveVerify.Services;

public static class TestPatternService
{
    public static byte[] GeneratePayload(Guid sessionId, int blockIndex, long absoluteOffset, int length)
    {
        byte[] seed = DeriveSeed(sessionId, blockIndex, absoluteOffset);
        return ExpandToLength(seed, length);
    }

    public static byte[] GenerateExpectedPayload(Guid sessionId, int blockIndex, long absoluteOffset, int length)
    {
        return GeneratePayload(sessionId, blockIndex, absoluteOffset, length);
    }

    private static byte[] DeriveSeed(Guid sessionId, int blockIndex, long absoluteOffset)
    {
        Span<byte> input = stackalloc byte[28];
        sessionId.TryWriteBytes(input);
        BitConverter.TryWriteBytes(input[16..], blockIndex);
        BitConverter.TryWriteBytes(input[20..], absoluteOffset);

        return SHA256.HashData(input);
    }

    private static byte[] ExpandToLength(byte[] seed, int length)
    {
        if (length <= 0) return [];

        var result = new byte[length];
        int offset = 0;

        // Use the seed as initial state for a simple counter-mode expansion
        byte[] currentHash = seed;
        uint counter = 0;
        Span<byte> counterInput = stackalloc byte[seed.Length + 4];
        seed.CopyTo(counterInput);

        while (offset < length)
        {
            int toCopy = Math.Min(currentHash.Length, length - offset);
            Buffer.BlockCopy(currentHash, 0, result, offset, toCopy);
            offset += toCopy;

            if (offset < length)
            {
                // Derive next block: SHA256(seed || counter)
                BitConverter.TryWriteBytes(counterInput[seed.Length..], ++counter);
                currentHash = SHA256.HashData(counterInput);
            }
        }

        return result;
    }
}
