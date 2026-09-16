using System;
using System.Collections.Generic;
using System.Formats.Nrbf;
using System.Linq;

namespace DownKyi.Services.Migration;

internal static class LegacyDownloadTaskMapper
{
    public static Dictionary<string, bool> ReadRequestedAssets(ClassRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        var values = record
            .GetArrayRecord("KeyValuePairs")?
            .GetArray(typeof(KeyValuePair<string, bool>[]));
        var result = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var item in values?.Cast<ClassRecord>() ?? [])
        {
            result[item.GetString("key") ?? string.Empty] = item.GetBoolean("value");
        }

        return result;
    }
}
