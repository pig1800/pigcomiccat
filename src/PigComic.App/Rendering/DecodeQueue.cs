using System.Runtime.CompilerServices;
using PigComic.Core.Imaging;
using SkiaSharp;

namespace PigComic.App.Rendering;

/// <summary>
/// M2.3 decode pipeline: dedicated worker threads (2) consuming a priority
/// queue; priority = distance from the viewport center (nearest first). Stale
/// requests (superseded by a newer viewport or a page switch) are dropped.
/// Tile arrival raises <see cref="TileReady"/> on the dispatcher (UI) thread.
/// </summary>
public sealed class DecodeQueue : IDisposable
{
    public const int WorkerCount = 2;

    private readonly object _lock = new();
    private readonly PriorityQueue<Work, double> _queue = new();
    private readonly HashSet<(string PageId, TileKey Key)> _queued = [];
    private readonly Dictionary<string, long> _pageTags = [];
    private readonly Thread[] _workers;
    private readonly SynchronizationContext? _ui;
    private volatile bool _stopping;
    private long _generation;

    public sealed record Work(string PageId, TileKey Key, long Tag, double Priority, Func<CancellationToken, SKImage> Decode);

    public event Action<(string PageId, TileKey Key, SKImage Image)>? TileReady;
    public event Action<(string PageId, TileKey Key, Exception Error)>? TileFailed;

    public DecodeQueue(SynchronizationContext? ui = null)
    {
        _ui = ui ?? SynchronizationContext.Current;
        _workers = new Thread[WorkerCount];
        for (var i = 0; i < WorkerCount; i++)
        {
            _workers[i] = new Thread(WorkerLoop) { IsBackground = true, Name = $"tile-decode-{i}" };
            _workers[i].Start();
        }
    }

    /// <summary>
    /// Marks a page switch: pending decodes submitted for other pages become
    /// stale and are dropped at once (their caches keep already-decoded tiles).
    /// </summary>
    public void SetPage(string pageId)
    {
        lock (_lock)
        {
            _generation++;
            _pageTags[pageId] = _generation;
            DropStaleLocked();
        }
    }

    /// <summary>Removes queued items whose page tag is no longer current.</summary>
    private void DropStaleLocked()
    {
        var rebuild = new PriorityQueue<Work, double>();
        while (_queue.Count > 0)
        {
            var item = _queue.Dequeue();
            var current = _pageTags.TryGetValue(item.PageId, out var tag) && tag == item.Tag;
            if (current)
            {
                rebuild.Enqueue(item, item.Priority);
            }
            else
            {
                _queued.Remove((item.PageId, item.Key));
            }
        }

        while (rebuild.Count > 0)
        {
            var item = rebuild.Dequeue();
            _queue.Enqueue(item, item.Priority);
        }
    }

    /// <summary>Queues a tile decode unless one is queued/in-flight for the same (page, key).</summary>
    public void Submit(string pageId, TileKey key, double priority, Func<CancellationToken, SKImage> decode)
    {
        bool shouldQueue;
        Work work;
        lock (_lock)
        {
            shouldQueue = _queued.Add((pageId, key));
            if (!shouldQueue)
            {
                return;
            }

            _pageTags.TryGetValue(pageId, out var tag);
            work = new Work(pageId, key, tag, priority, decode);
            _queue.Enqueue(work, priority);
            Monitor.Pulse(_lock);
        }
    }

    public int PendingCount
    {
        get { lock (_lock) { return _queue.Count; } }
    }

    private void WorkerLoop()
    {
        while (true)
        {
            Work? work = null;
            lock (_lock)
            {
                while (!_stopping && _queue.Count == 0)
                {
                    Monitor.Wait(_lock);
                }

                if (_stopping)
                {
                    return;
                }

                work = _queue.Dequeue();
                _queued.Remove((work.PageId, work.Key));
            }

            bool isCurrentPage;
            lock (_lock)
            {
                _pageTags.TryGetValue(work.PageId, out var tag);
                isCurrentPage = tag == work.Tag;
            }

            if (!isCurrentPage)
            {
                continue; // stale page: drop silently
            }

            try
            {
                var image = work.Decode(CancellationToken.None);
                Raise(TileReady, (work.PageId, work.Key, image));
            }
            catch (Exception ex)
            {
                Raise(TileFailed, (work.PageId, work.Key, ex));
            }
        }
    }

    private void Raise<T>(Action<T>? handler, T arg)
    {
        if (handler is null)
        {
            return;
        }

        if (_ui is { } ui)
        {
            ui.Post(_ => handler(arg), null);
        }
        else
        {
            handler(arg);
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _stopping = true;
            Monitor.PulseAll(_lock);
        }

        foreach (var t in _workers)
        {
            t.Join(TimeSpan.FromSeconds(2));
        }
    }
}