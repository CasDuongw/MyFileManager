using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace FM.Core.Operations.MultiRename;

/// <summary>
/// Evaluates rename pattern templates, tokens, extension casing, and find/replace operations.
/// </summary>
public static class MultiRenameTokenEvaluator
{
    private static readonly Regex TokenRegex = new(@"\[([^\]]+)\]", RegexOptions.Compiled);
    private static readonly Regex SubstringNameRegex = new(@"^N(\d+)?-(\d+)?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex CounterParamRegex = new(@"^C(?::(\d+)(?:,(\d+))?(?:,(\d+))?)?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex DateParamRegex = new(@"^D(?::(.+))?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string Evaluate(
        MultiRenameItemInput item,
        int itemIndex,
        MultiRenameOptions options)
    {
        string rawName = item.IsDirectory
            ? item.Name
            : Path.GetFileNameWithoutExtension(item.Name);

        string rawExt = item.IsDirectory
            ? string.Empty
            : Path.GetExtension(item.Name).TrimStart('.');

        // 1. Transform extension case
        string formattedExt = FormatExtension(rawExt, options.ExtCase);

        // 2. Compute counter value
        int counterValue = options.CounterStart + (itemIndex * options.CounterStep);
        int defaultPadding = Math.Max(1, options.CounterPadding);

        // 3. Prepare pattern
        string pattern = string.IsNullOrWhiteSpace(options.Pattern) ? "[N].[E]" : options.Pattern;

        // If extension is empty, remove ".[E]" or ".[ext]" to avoid producing trailing dots
        if (string.IsNullOrEmpty(formattedExt))
        {
            pattern = Regex.Replace(pattern, @"\.(?:\[E\]|\[ext\])", string.Empty, RegexOptions.IgnoreCase);
        }

        // 4. Evaluate tokens
        string evaluated = TokenRegex.Replace(pattern, match =>
        {
            string token = match.Groups[1].Value.Trim();

            // [N] or [name]
            if (string.Equals(token, "N", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(token, "name", StringComparison.OrdinalIgnoreCase))
            {
                return rawName;
            }

            // [N#-#] Substring
            var subMatch = SubstringNameRegex.Match(token);
            if (subMatch.Success)
            {
                return ExtractSubstring(rawName, subMatch.Groups[1].Value, subMatch.Groups[2].Value);
            }

            // [E] or [ext]
            if (string.Equals(token, "E", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(token, "ext", StringComparison.OrdinalIgnoreCase))
            {
                return formattedExt;
            }

            // [C] or [counter]
            var counterMatch = CounterParamRegex.Match(token);
            if (counterMatch.Success || string.Equals(token, "counter", StringComparison.OrdinalIgnoreCase))
            {
                int pad = defaultPadding;
                int currentVal = counterValue;

                if (counterMatch.Success && counterMatch.Groups[1].Success)
                {
                    string g1 = counterMatch.Groups[1].Value;
                    bool hasG2 = counterMatch.Groups[2].Success;
                    bool hasG3 = counterMatch.Groups[3].Success;

                    if (!hasG2 && !hasG3)
                    {
                        // Single parameter, e.g. [C:001] or [C:3]
                        if (g1.StartsWith('0'))
                        {
                            pad = Math.Max(1, g1.Length);
                        }
                        else if (int.TryParse(g1, out int pInt))
                        {
                            pad = Math.Max(1, pInt);
                        }
                    }
                    else
                    {
                        // Multi parameter: [C:start,step] or [C:start,step,padding]
                        if (int.TryParse(g1, out int customStart))
                        {
                            int step = hasG2 && int.TryParse(counterMatch.Groups[2].Value, out int s) ? s : options.CounterStep;
                            currentVal = customStart + (itemIndex * step);
                            if (hasG3 && int.TryParse(counterMatch.Groups[3].Value, out int p))
                            {
                                pad = Math.Max(1, p);
                            }
                        }
                    }
                }

                return currentVal.ToString(CultureInfo.InvariantCulture).PadLeft(pad, '0');
            }

            // [D] or [date]
            var dateMatch = DateParamRegex.Match(token);
            if (dateMatch.Success || string.Equals(token, "date", StringComparison.OrdinalIgnoreCase))
            {
                string format = dateMatch.Groups[1].Success && !string.IsNullOrWhiteSpace(dateMatch.Groups[1].Value)
                    ? dateMatch.Groups[1].Value
                    : "yyyy-MM-dd";

                try
                {
                    return item.DateModified.ToString(format, CultureInfo.InvariantCulture);
                }
                catch
                {
                    return item.DateModified.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                }
            }

            // Unknown token: keep as literal
            return match.Value;
        });

        // 5. Apply Find and Replace if configured
        if (!string.IsNullOrEmpty(options.FindText))
        {
            evaluated = ApplyFindReplace(evaluated, options.FindText, options.ReplaceText ?? string.Empty, options.MatchCase, options.UseRegex);
        }

        return evaluated;
    }

    private static string FormatExtension(string ext, ExtensionCase extCase)
    {
        if (string.IsNullOrEmpty(ext)) return string.Empty;

        return extCase switch
        {
            ExtensionCase.Lower => ext.ToLowerInvariant(),
            ExtensionCase.Upper => ext.ToUpperInvariant(),
            ExtensionCase.Title => ext.Length > 0 ? char.ToUpperInvariant(ext[0]) + ext[1..].ToLowerInvariant() : ext,
            _ => ext
        };
    }

    private static string ExtractSubstring(string source, string startStr, string endStr)
    {
        if (string.IsNullOrEmpty(source)) return string.Empty;

        int len = source.Length;
        int start = 1;
        int end = len;

        if (!string.IsNullOrEmpty(startStr) && int.TryParse(startStr, out int s))
        {
            start = Math.Clamp(s, 1, len);
        }

        if (!string.IsNullOrEmpty(endStr) && int.TryParse(endStr, out int e))
        {
            end = Math.Clamp(e, start, len);
        }

        int startIndex = start - 1;
        int count = Math.Max(0, end - start + 1);

        if (startIndex >= len || count <= 0) return string.Empty;
        return source.Substring(startIndex, Math.Min(count, len - startIndex));
    }

    private static string ApplyFindReplace(string input, string find, string replace, bool matchCase, bool useRegex)
    {
        if (useRegex)
        {
            try
            {
                var regexOptions = matchCase ? RegexOptions.None : RegexOptions.IgnoreCase;
                return Regex.Replace(input, find, replace, regexOptions);
            }
            catch
            {
                // Invalid regex - return original
                return input;
            }
        }

        var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        return input.Replace(find, replace, comparison);
    }
}
