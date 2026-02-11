using System.Text.RegularExpressions;

namespace BaselineMode.WPF.Core.Models
{
    public static partial class RegexPatterns
    {
        [GeneratedRegex(@"\s+")]
        public static partial Regex Whitespace();

        [GeneratedRegex("E225[0-9A-F]+")]
        public static partial Regex E225Header();
    }
}
