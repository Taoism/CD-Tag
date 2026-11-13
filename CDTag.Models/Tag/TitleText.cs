using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CDTag.Model.Tag
{
    public enum TitleCasing
    {
        /// <summary>"Don't Shit Where You Eat"</summary>
        CapitalizeEveryWord,
        /// <summary>"DON'T SHIT WHERE YOU EAT"</summary>
        Uppercase,
        /// <summary>"don't shit where you eat"</summary>
        Lowercase,
        /// <summary>"Don't shit where you eat"</summary>
        SentenceCase
    }

    /// <summary>Song name helpers for Tag Album: casing, search and replace, pasted track lists.</summary>
    public static partial class TitleText
    {
        private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

        /// <summary>True when no character of <paramref name="text"/> changes when upper-cased (so "" and "123" count).</summary>
        private static bool IsAllUpper(string text)
        {
            foreach (char c in text)
            {
                if (char.ToUpper(c, Culture) != c)
                    return false;
            }
            return true;
        }

        public static string ApplyCasing(string text, TitleCasing casing)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            switch (casing)
            {
                case TitleCasing.Uppercase:
                    return text.ToUpper(Culture);
                case TitleCasing.Lowercase:
                    return text.ToLower(Culture);
                case TitleCasing.SentenceCase:
                    return SentenceCase(text);
                default:
                    return CapitalizeEveryWord(text);
            }
        }

        /// <summary>
        /// First letter of every word upper case, the rest lower case. Words start after spaces and ( [ { - / " ;
        /// an apostrophe does not start a word ("Don't"). Roman numerals stay upper case ("Part II").
        /// </summary>
        private static string CapitalizeEveryWord(string text)
        {
            // In an all-caps title "MIX" or "DID" are not numerals.
            bool allCaps = IsAllUpper(text);
            var sb = new StringBuilder(text.Length);
            foreach (string word in SplitWords(text))
            {
                if (word.Length == 0 || !char.IsLetter(word[0]) && !word.Any(char.IsLetter))
                {
                    sb.Append(word);
                    continue;
                }
                if (!allCaps && word.Length > 1 && RomanNumeral().IsMatch(word.Trim().Trim('(', ')', '.', ',', '[', ']')) && IsAllUpper(word))
                {
                    sb.Append(word);
                    continue;
                }

                bool first = true;
                foreach (char c in word)
                {
                    if (first && char.IsLetterOrDigit(c))
                    {
                        sb.Append(char.ToUpper(c, Culture));
                        first = false;
                    }
                    else
                    {
                        sb.Append(first ? c : char.ToLower(c, Culture));
                    }
                }
            }
            return sb.ToString();
        }

        /// <summary>Upper case at the start and after . ! ?, lower case elsewhere; "i" / "i'm" stay "I" / "I'm".</summary>
        private static string SentenceCase(string text)
        {
            bool allCaps = IsAllUpper(text);
            var sb = new StringBuilder(text.Length);
            bool capitalizeNext = true;
            foreach (string word in SplitWords(text))
            {
                string lower = word.ToLower(Culture);
                bool isI = PronounI().IsMatch(lower);
                bool keepRoman = !allCaps && word.Length > 1 && IsAllUpper(word) && RomanNumeral().IsMatch(word.Trim('(', ')', '.', ',', '[', ']'));
                if (keepRoman)
                {
                    sb.Append(word);
                }
                else if ((capitalizeNext || isI) && lower.Any(char.IsLetterOrDigit))
                {
                    int index = lower.ToList().FindIndex(char.IsLetterOrDigit);
                    sb.Append(lower.AsSpan(0, index)).Append(char.ToUpper(lower[index], Culture)).Append(lower.AsSpan(index + 1));
                }
                else
                {
                    sb.Append(lower);
                }

                if (lower.Any(char.IsLetterOrDigit))
                    capitalizeNext = false;
                string trimmed = word.TrimEnd();
                if (trimmed.EndsWith('.') || trimmed.EndsWith('!') || trimmed.EndsWith('?'))
                    capitalizeNext = true;
            }
            return sb.ToString();
        }

        /// <summary>Splits into words, keeping the separators attached in front so joining restores the text.</summary>
        private static IEnumerable<string> SplitWords(string text)
        {
            var word = new StringBuilder();
            foreach (char c in text)
            {
                bool separator = char.IsWhiteSpace(c) || "([{-/\"_".Contains(c);
                if (separator)
                {
                    if (word.Length > 0 && word.ToString().Any(ch => !char.IsWhiteSpace(ch) && "([{-/\"_".IndexOf(ch) < 0))
                    {
                        yield return word.ToString();
                        word.Clear();
                    }
                }
                word.Append(c);
            }
            if (word.Length > 0)
                yield return word.ToString();
        }

        /// <summary>Replaces every occurrence of <paramref name="find"/>; returns the text unchanged when it is empty.</summary>
        public static string Replace(string text, string find, string replace, bool matchCase)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(find))
                return text;
            return text.Replace(find, replace ?? "", matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Song names from a pasted track list (web page, CD booklet, Discogs...): one per line; track numbers
        /// ("1.", "01 -", "1-01", "A1"; a number only when it continues the numbering, so "10 Years Gone" stays) and lengths ("3:45", "(3:45)") are removed, disc headers and empty lines skipped.
        /// </summary>
        public static List<string> ParseTracklist(string text)
        {
            var titles = new List<string>();
            if (string.IsNullOrWhiteSpace(text))
                return titles;
            int last = 0;

            foreach (string raw in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            {
                string line = raw.Replace('\t', ' ').Trim();
                if (line.Length == 0 || DiscHeader().IsMatch(line))
                    continue;

                line = TrailingLength().Replace(line, "");
                Match number = LeadingNumber().Match(line);
                int n;
                if (number.Success && int.TryParse(number.Groups["n"].Value, out n) && (n == last + 1 || n == 1 || number.Groups["disc"].Success))
                {
                    line = line.Substring(number.Length);
                    last = n;
                }
                else if (VinylPosition().IsMatch(line))
                {
                    line = VinylPosition().Replace(line, "");
                    last++;
                }
                line = line.Trim();
                if (line.Length > 0)
                    titles.Add(line);
            }
            return titles;
        }

        [GeneratedRegex(@"^(?=[MDCLXVI])M{0,3}(CM|CD|D?C{0,3})(XC|XL|L?X{0,3})(IX|IV|V?I{0,3})$", RegexOptions.CultureInvariant, CDTag.Common.RegexTimeouts.Milliseconds)]
        private static partial Regex RomanNumeral();

        [GeneratedRegex(@"^\s*(cd|disc|disk|side|dvd)\s*[-_#.]?\s*[0-9a-z]{0,2}\s*[:.)-]?\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, CDTag.Common.RegexTimeouts.Milliseconds)]
        private static partial Regex DiscHeader();

        [GeneratedRegex(@"^(?:(?<disc>\d{1,2})[-.])?(?<n>\d{1,3})(?=[\s.):\-]|$)[\s.):\-]*", RegexOptions.CultureInvariant, CDTag.Common.RegexTimeouts.Milliseconds)]
        private static partial Regex LeadingNumber();

        [GeneratedRegex(@"^\W*i('(m|d|ll|ve))?\W*$", RegexOptions.None, CDTag.Common.RegexTimeouts.Milliseconds)]
        private static partial Regex PronounI();

        [GeneratedRegex(@"\s*[-–]?\s*[(\[]?\d{1,3}:\d{2}(?::\d{2})?[)\]]?\s*$", RegexOptions.CultureInvariant, CDTag.Common.RegexTimeouts.Milliseconds)]
        private static partial Regex TrailingLength();

        [GeneratedRegex(@"^[A-Z]\d{1,2}(?=[\s.):\-])[\s.):\-]*", RegexOptions.CultureInvariant, CDTag.Common.RegexTimeouts.Milliseconds)]
        private static partial Regex VinylPosition();
    }
}
