using CommunityToolkit.Mvvm.ComponentModel;
using PigComic.Core.Domain;

namespace PigComic.App.ViewModels;

/// <summary>Page group header shown above its bubbles (SPEC §14.3).</summary>
public sealed class SegmentPageHeaderViewModel(string pageId, int pageIndex)
{
    public string PageId { get; } = pageId;
    public string Heading => $"Page {pageIndex + 1} — {PageId}";
}