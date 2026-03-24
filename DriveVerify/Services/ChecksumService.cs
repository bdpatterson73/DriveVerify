using System.IO.Hashing;

namespace DriveVerify.Services;

public static class ChecksumService
{
    public static uint Compute(ReadOnlySpan<byte> data)
    {
        return Crc32.HashToUInt32(data);
    }
}
