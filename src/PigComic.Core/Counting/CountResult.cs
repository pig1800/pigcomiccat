namespace PigComic.Core.Counting;

/// <summary>
/// The two billing/progress counts of SPEC §11: MemoQ (code points in the CJK
/// ideograph blocks after ASCII-run collapsing) and MSWord (UTF-16 length minus
/// en/em dashes). Guaranteed MemoQ &lt;= MSWord on the same text.
/// </summary>
public sealed record CountResult(int MemoQ, int MSWord);