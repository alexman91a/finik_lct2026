using System.Collections.Generic;
using System.Text;

namespace Finik.UI
{
    /// <summary>
    /// Russian line-breaking rules for UI copy, applied with non-breaking spaces so TextMeshPro keeps
    /// the words together: a short preposition or conjunction never hangs at the end of a line, a dash
    /// never starts one, a number stays with its noun, and a paragraph does not end on a lone short word.
    /// </summary>
    public static class FinikTypography
    {
        const char Nbsp = ' ';
        const int WidowWordMax = 5;      // letters in a last word that may not sit alone on a line
        const int WidowPairMax = 14;     // …as long as the glued pair stays short enough to wrap cleanly

        static readonly HashSet<string> Clingy = new(System.StringComparer.OrdinalIgnoreCase)
        {
            "а", "в", "и", "к", "о", "с", "у", "я", "во", "за", "из", "ко", "на", "не", "ни", "но", "об", "от",
            "по", "со", "до", "же", "ли", "бы", "да", "уж", "без", "для", "при", "про", "под", "над", "или", "что"
        };

        public static string Fix(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            var words = text.Split(' ');
            if (words.Length < 2) return text;

            var result = new StringBuilder(text.Length);
            for (int i = 0; i < words.Length; i++)
            {
                result.Append(words[i]);
                if (i == words.Length - 1) break;
                result.Append(KeepTogether(words[i], words[i + 1]) ? Nbsp : ' ');
            }

            // No widow: glue a short last word to the previous one ("полную цену.»").
            string fixedText = result.ToString();
            int last = fixedText.LastIndexOf(' ');
            if (last > 0)
            {
                string tail = fixedText.Substring(last + 1);
                int prev = fixedText.LastIndexOf(' ', last - 1);
                int pairLength = fixedText.Length - (prev + 1);
                if (Letters(tail) <= WidowWordMax && pairLength <= WidowPairMax)
                    fixedText = fixedText.Substring(0, last) + Nbsp + tail;
            }
            return fixedText;
        }

        /// <summary>Russian plural form for <paramref name="count"/>: 1 монета, 2 монеты, 5 монет, 21 монета.</summary>
        public static string Plural(int count, string one, string few, string many)
        {
            int n = System.Math.Abs(count) % 100;
            if (n >= 11 && n <= 14) return many;
            return (n % 10) switch
            {
                1 => one,
                2 or 3 or 4 => few,
                _ => many
            };
        }

        static bool KeepTogether(string word, string next)
        {
            string bare = word.Trim('«', '»', '(', ')', '"');
            if (Clingy.Contains(bare)) return true;                       // «в комнату», «и поесть»
            if (next.Length > 0 && (next[0] == '—' || next[0] == '–' || next[0] == '=' || next[0] == '+')) return true; // dash stays on the line
            if (bare.Length > 0 && char.IsDigit(bare[bare.Length - 1])) return true;  // «150 монет»
            return false;
        }

        static int Letters(string word)
        {
            int count = 0;
            foreach (char c in word) if (char.IsLetterOrDigit(c)) count++;
            return count;
        }
    }
}
