/// <summary>Threshold rules of the disk-space monitor (F46), free of side effects so they can be unit tested.
/// A disabled threshold (0 or less) never triggers; an unknown free size (null, common on UNC shares) never triggers either.</summary>
public static class DiskRules
{
    /// <summary>"ok", "low" (at or below the warning threshold) or "critical" (at or below the critical threshold).</summary>
    public static string Level(long? freeBytes, long warnBytes, long criticalBytes)
    {
        if (freeBytes is not long free)
        {
            return "ok";
        }
        if (criticalBytes > 0 && free <= criticalBytes)
        {
            return "critical";
        }
        if (warnBytes > 0 && free <= warnBytes)
        {
            return "low";
        }
        return "ok";
    }

    /// <summary>GB to bytes.</summary>
    public static long Bytes(int gb) => gb <= 0 ? 0 : gb * 1024L * 1024 * 1024;
}
