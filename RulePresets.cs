using System;
using System.Collections.Generic;

namespace PasteGuard
{
    public sealed class TargetPreset
    {
        public string Name;
        public string[] Processes;
        public string[] Sites;
    }

    public static class RulePresets
    {
        public static readonly string[] PatternNames = { "테스트 문자열", "GitHub PAT (classic)", "직접 입력" };

        public static readonly TargetPreset[] Targets = {
            new TargetPreset { Name = "Slack", Processes = new[] { "Slack.exe" }, Sites = new[] { "slack.com", "*.slack.com" } },
            new TargetPreset { Name = "Notion", Processes = new[] { "Notion.exe" }, Sites = new[] { "notion.so", "*.notion.so", "notion.site", "*.notion.site" } },
            new TargetPreset { Name = "GitHub 웹사이트", Processes = new string[0], Sites = new[] { "github.com", "gist.github.com" } }
        };

        public static string Pattern(int index)
        {
            if (index == 0) return "PGTEST_[A-Z0-9]{16}";
            if (index == 1) return "ghp_[A-Za-z0-9]{36}";
            return null;
        }

        public static int PatternIndex(string pattern)
        {
            for (int i = 0; i < 2; i++)
                if (String.Equals(pattern, Pattern(i), StringComparison.Ordinal)) return i;
            return 2;
        }

        public static string Example(int index)
        {
            if (index == 0) return "PGTEST_ABCD1234EFGH5678";
            // An intentionally synthetic example, never a fetched or usable credential.
            if (index == 1) return "ghp_" + new string('0', 36);
            return "";
        }

        public static string Description(int index)
        {
            if (index == 0) return "PGTEST_ 뒤 영문 대문자와 숫자 16자리 형식을 찾습니다. 테스트용 가짜 문자열입니다.";
            if (index == 1) return "ghp_ 뒤 영문과 숫자 36자리 형식을 찾습니다. 토큰의 실제 유효성은 확인하지 않습니다. 예시는 가짜입니다.";
            return "지원하는 패턴 문법으로 직접 입력하세요. 문자열 형식만 검사하며 실제 유효성은 확인하지 않습니다.";
        }

        public static List<string> ParseTargets(string text)
        {
            var result = new List<string>();
            foreach (string item in (text ?? "").Split(','))
            {
                string target = item.Trim();
                if (target.Length > 0) result.Add(target);
            }
            return result;
        }

        public static string MergeTargets(string existing, IEnumerable<string> additions)
        {
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string target in ParseTargets(existing))
                if (seen.Add(target)) result.Add(target);
            if (additions != null)
                foreach (string item in additions)
                {
                    string target = (item ?? "").Trim();
                    if (target.Length > 0 && seen.Add(target)) result.Add(target);
                }
            return String.Join(", ", result);
        }
    }
}
