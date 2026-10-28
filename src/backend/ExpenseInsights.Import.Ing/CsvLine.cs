namespace ExpenseInsights.Import.Ing;

internal static class CsvLine
{
    /// <summary>
    /// Splits one line. Fields may be double-quoted (with <c>""</c> as an escaped quote) or bare;
    /// ING mixes both on the same line.
    /// </summary>
    public static List<string> Split(string line, char delimiter)
    {
        var fields = new List<string>(24);
        var i = 0;
        while (true)
        {
            if (i < line.Length && line[i] == '"')
            {
                var value = new System.Text.StringBuilder();
                i++;
                while (i < line.Length)
                {
                    if (line[i] == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"')
                        {
                            value.Append('"');
                            i += 2;
                            continue;
                        }

                        i++;
                        break;
                    }

                    value.Append(line[i++]);
                }

                // Anything between the closing quote and the delimiter is kept, not lost.
                var next = line.IndexOf(delimiter, i);
                value.Append(line.AsSpan(i, (next < 0 ? line.Length : next) - i));
                fields.Add(value.ToString());
                if (next < 0)
                {
                    return fields;
                }

                i = next + 1;
            }
            else
            {
                var next = line.IndexOf(delimiter, i);
                if (next < 0)
                {
                    fields.Add(line[i..]);
                    return fields;
                }

                fields.Add(line[i..next]);
                i = next + 1;
            }
        }
    }
}
