namespace DriveVerify.Models;

public enum IssueKind
{
    ChecksumMismatch,
    HeaderMismatch,
    ShortRead,
    Unreadable,
    DuplicateWrap
}

public class VerificationIssue
{
    public int BlockIndex { get; set; }
    public long AbsoluteOffset { get; set; }
    public IssueKind IssueKind { get; set; }
    public string Detail { get; set; } = string.Empty;
}
