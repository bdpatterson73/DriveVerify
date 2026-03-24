using System.Security.Cryptography;

namespace DriveVerify.Services;

public static class TestPatternService
{
    public static byte[] GeneratePayload(Guid sessionId, int blockIndex, long absoluteOffset, int length)
    {
        if (length <= 0) return [];
        var result = new byte[length];
        GeneratePayloadInto(sessionId, blockIndex, absoluteOffset, result);
        return result;
    }

    public static void GeneratePayloadInto(Guid sessionId, int blockIndex, long absoluteOffset, Span<byte> destination)
    {
        if (destination.IsEmpty) return;
        byte[] seed = DeriveSeed(sessionId, blockIndex, absoluteOffset);
        int intSeed = BitConverter.ToInt32(seed, 0);
        new Random(intSeed).NextBytes(destination);
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
}
