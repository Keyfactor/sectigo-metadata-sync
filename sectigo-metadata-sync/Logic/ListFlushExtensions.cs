using System.Collections.Generic;
using NLog;

namespace SectigoMetadataSync.Logic;

public static class ListFlushExtensions
{
    /// <summary>
    ///     Flush any remaining items after a page/loop ends.
    /// </summary>
    public static void FlushRemainder(this List<string> buffer,
        Logger log,
        string label,
        ref int totalCount)
    {
        FlushToTrace(buffer, log, label, ref totalCount);
    }

    private static void FlushToTrace(List<string> buffer,
        Logger log,
        string label,
        ref int totalCount)
    {
        if (buffer.Count == 0) return;

        // One line per item keeps logs searchable and prevents jumbo lines.
        foreach (var s in buffer)
            log.Trace("{Label}: {Item}", label, s);

        totalCount += buffer.Count;
        buffer.Clear(); // release memory
        buffer.TrimExcess(); // optional: shrink backing array
    }
}