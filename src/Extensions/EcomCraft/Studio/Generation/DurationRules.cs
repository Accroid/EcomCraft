namespace EcomCraftStudio.Studio.Generation;

/// <summary>Pure rules of the time estimates (F70): a window of recent durations, its typical value and the seconds left.
/// Only durations are kept, never what the operation worked on.</summary>
public static class DurationRules
{
    /// <summary>How many recent durations one kind of operation keeps.</summary>
    public const int WindowSize = 20;

    /// <summary>Longest duration accepted; anything longer is a stuck or abandoned operation, not a useful sample.</summary>
    public const double MaxSeconds = 4 * 3600;

    /// <summary>The recent durations of one kind of operation, oldest first. Not thread-safe: callers lock the window.</summary>
    public sealed class Window
    {
        /// <summary>The kept durations in seconds.</summary>
        private readonly Queue<double> Samples = new();

        /// <summary>Adds one duration; non-positive or overlong values are ignored, and the oldest goes once the window is full.</summary>
        public void Add(double seconds)
        {
            if (!(seconds > 0) || seconds > MaxSeconds)
            {
                return;
            }
            Samples.Enqueue(seconds);
            while (Samples.Count > WindowSize)
            {
                Samples.Dequeue();
            }
        }

        /// <summary>How long the operation typically takes: the median of the kept durations, or null without any.</summary>
        public double? Typical() => Median([.. Samples]);
    }

    /// <summary>The median of the values, so one unusually slow run does not skew the estimate; null for no values.</summary>
    public static double? Median(IReadOnlyList<double> values)
    {
        if (values is null || values.Count == 0)
        {
            return null;
        }
        List<double> sorted = [.. values.Order()];
        int middle = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
    }

    /// <summary>Per-image seconds of a finished generation: its time from submit to result spread over the images queued ahead of it on the
    /// server and its own images, which the server ran one after another.</summary>
    public static double PerImage(double wallSeconds, int ahead, int images) => wallSeconds / (Math.Max(0, ahead) + Math.Max(1, images));

    /// <summary>Seconds left of an operation that typically takes <paramref name="estimate"/> seconds and has run <paramref name="elapsed"/>:
    /// never negative (zero means it is taking longer than usual); null without an estimate.</summary>
    public static int? Remaining(double? estimate, double elapsed) => estimate is double total ? (int)Math.Max(0, Math.Round(total - elapsed)) : null;
}
