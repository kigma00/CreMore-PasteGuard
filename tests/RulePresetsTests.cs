using System;
using System.Collections.Generic;
using PasteGuard;

internal static class RulePresetsTests
{
    private static int checks;

    private static void Check(bool condition, string label)
    {
        checks++;
        if (!condition) throw new Exception("FAILED: " + label);
    }

    public static int Main()
    {
        try
        {
            for (int index = 0; index < 2; index++)
            {
                string pattern = RulePresets.Pattern(index);
                string example = RulePresets.Example(index);
                Check(PolicyEngine.MatchText(example, pattern), "synthetic preset example matches " + index);
                Check(PolicyEngine.MatchText("prefix " + example + " suffix", pattern), "preset works inside pasted text " + index);
                Check(!PolicyEngine.MatchText("ordinary text: meeting at 10:30", pattern), "benign text passes " + index);
                Check(!PolicyEngine.MatchText(example.Substring(0, example.Length - 1), pattern), "short example passes " + index);
                Check(RulePresets.PatternIndex(pattern) == index, "saved preset is recognized " + index);
            }
            Check(!PolicyEngine.MatchText("PGTEST_abcd1234EFGH5678", RulePresets.Pattern(0)), "test prefix requires uppercase content");
            Check(!PolicyEngine.MatchText("ghp_" + new string('-', 36), RulePresets.Pattern(1)), "PAT excludes punctuation");
            Check(RulePresets.PatternIndex("CUSTOM_[0-9]{8}") == 2, "custom patterns stay custom");
            Check(RulePresets.PatternIndex(null) == 2, "empty selection stays custom");
            Check(RulePresets.Pattern(2) == null && RulePresets.Pattern(-1) == null, "custom has no replacement pattern");
            Check(RulePresets.Example(2) == "", "custom has no fabricated example");

            Check(RulePresets.ParseTargets(null).Count == 0, "null targets are empty");
            List<string> parsed = RulePresets.ParseTargets(" ,  Custom App.exe,\tSlack.exe  , , ");
            Check(parsed.Count == 2 && parsed[0] == "Custom App.exe" && parsed[1] == "Slack.exe", "parse trims and skips blanks");
            string merged = RulePresets.MergeTargets(" Custom App.exe, SLACK.exe, custom app.EXE , ", new[] { "slack.EXE", " Notion.exe ", null, "" });
            Check(merged == "Custom App.exe, SLACK.exe, Notion.exe", "merge deduplicates case and preserves custom spelling and order");
            Check(RulePresets.MergeTargets("internal.example, *.My-Team.example", RulePresets.Targets[0].Sites)
                == "internal.example, *.My-Team.example, slack.com, *.slack.com", "custom domains survive preset merge");
            Check(RulePresets.MergeTargets("github.com", new[] { "GITHUB.COM", "gist.github.com", "gist.github.com" })
                == "github.com, gist.github.com", "added targets are unique");
            Check(RulePresets.MergeTargets(null, null) == "", "empty merge is safe");

            foreach (TargetPreset preset in RulePresets.Targets)
            {
                var rule = new Rule { id = "preset", name = preset.Name, pattern = RulePresets.Pattern(0),
                    processes = new List<string>(preset.Processes), sites = new List<string>(preset.Sites) };
                Check(PolicyEngine.Validate(new Policy { rules = new List<Rule> { rule } }).Count == 0, "target preset is valid: " + preset.Name);
                foreach (string process in preset.Processes)
                    Check(PolicyEngine.TargetProcess(rule, process.ToUpperInvariant()), "preset process matches: " + process);
                foreach (string site in preset.Sites)
                    Check(PolicyEngine.TargetSite(rule, "https://" + site.Replace("*.", "team.") + "/"), "preset domain matches: " + site);
                Check(!PolicyEngine.TargetSite(rule, "https://unrelated.example/"), "unrelated domain excluded: " + preset.Name);
            }
            Check(RulePresets.Targets[2].Processes.Length == 0, "GitHub preset targets websites only");
            Console.WriteLine("PASS: " + checks + " rule preset checks.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error.Message); return 1; }
    }
}
