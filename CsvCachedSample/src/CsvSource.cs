using System.Collections.Generic;
using System.Globalization;
using System.IO;
using CsvHelper;
using CsvHelper.Configuration;

namespace Curiosity.Library.Recipes;

// Generic, dataset-agnostic CSV reader. Pass a POCO with [Name("column")]
// attributes from CsvHelper and the file is deserialized into a typed list.
// Reuse as-is for any CSV source — only the row class needs to change.
public static class CsvSource
{
    public static IReadOnlyList<T> Load<T>(string path)
    {
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord   = true,
            TrimOptions       = TrimOptions.Trim,
            MissingFieldFound = null,
            HeaderValidated   = null,
        };

        using var reader = new StreamReader(path);
        using var csv    = new CsvReader(reader, config);
        return new List<T>(csv.GetRecords<T>());
    }
}
