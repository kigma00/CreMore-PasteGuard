using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Web.Script.Serialization;
using PasteGuard;

internal static class CoreTests
{
    private static int checks;
    private static void Check(bool condition, string label)
    {
        checks++;
        if (!condition) throw new Exception("FAILED: " + label);
    }
    private static void Match(string text, string pattern, bool expected, string label)
    { Check(PolicyEngine.MatchText(text, pattern) == expected, label); }
    private static void Reject(string pattern)
    {
        bool rejected = false;
        try { PolicyEngine.MatchText("example", pattern); }
        catch (ArgumentException) { rejected = true; }
        Check(rejected, "unsafe or invalid pattern rejected");
    }
    private static Rule ExampleRule()
    {
        return new Rule { id = "demo", name = "Demo token", pattern = "PGTEST_[A-Z0-9]{16}",
            processes = new List<string> { "Slack.exe", "Notion.exe" },
            sites = new List<string> { "slack.com", "*.slack.com", "notion.so", "*.notion.so", "notion.site", "*.notion.site" } };
    }

    public static int Main()
    {
        try
        {
            Match("prefix PGTEST_ABCD1234EFGH5678 suffix", "PGTEST_[A-Z0-9]{16}", true, "demo token");
            Match("PGTEST_abcd1234EFGH5678", "PGTEST_[A-Z0-9]{16}", false, "case sensitive content");
            Match("PGTEST_ABCD1234EFGH567", "PGTEST_[A-Z0-9]{16}", false, "short token");
            Match("ordinary text", "PGTEST_[A-Z0-9]{16}", false, "ordinary text");
            Match("prefix abc suffix", "abc", true, "unanchored search");
            Match("ABC", "abc", false, "literal case sensitivity");
            Match("abc", "^abc$", true, "anchors");
            Match("abc\n", "^abc$", false, "strict end anchor");
            Match("xabc", "^abc", false, "strict start anchor");
            Match("x\nabc", "^abc", false, "no multiline mode");
            Match("abc", "a^bc", false, "interior start assertion");
            Match("", "^$", true, "empty anchored input");
            Match(null, "^$", true, "null text is empty");
            Match("", "a*", true, "star accepts zero");
            Match("ab", "^a*b$", true, "star accepts one");
            Match("aaaab", "^a*b$", true, "star accepts many");
            Match("b", "^a+b$", false, "plus needs one");
            Match("aaab", "^a+b$", true, "plus accepts many");
            Match("color", "^colou?r$", true, "optional absent");
            Match("colour", "^colou?r$", true, "optional present");
            Match("colouur", "^colou?r$", false, "optional bounded");
            Match("aaa", "^a{3}$", true, "exact repetition");
            Match("aa", "^a{2,4}$", true, "minimum repetition");
            Match("aaaa", "^a{2,4}$", true, "maximum repetition");
            Match("aaaaa", "^a{2,4}$", false, "repetition upper bound");
            Match("", "a{0}", true, "zero repetition");
            Match("", "^a{0,0}$", true, "zero interval");
            Match(new string('a', 256), "^a{256}$", true, "maximum repeat accepted");
            Match("123", @"^\d+$", true, "ASCII digits");
            Match("١٢٣", @"^\d+$", false, "Unicode digits excluded");
            Match("a_Z09", @"^\w+$", true, "ASCII word");
            Match("한글", @"^\w+$", false, "Unicode word excluded");
            Match(" \t\r\n\v\f", @"^\s+$", true, "ASCII whitespace");
            Match("\u00a0", @"^\s$", false, "nonbreaking space excluded");
            Match("\n\r\t\f\v", @"^\n\r\t\f\v$", true, "literal control escapes");
            Match("a.b+(c)|d[0]{1}\\", @"^a\.b\+\(c\)\|d\[0\]\{1\}\\$", true, "escaped punctuation");
            Match("b7_", @"^[a-c\d_]+$", true, "class literal range and escape");
            Match("xyz", "^[^a-c0-9]+$", true, "negated class");
            Match("y2z", "^[^a-c0-9]+$", false, "negated class excludes");
            Match("-a", "^[-a]+$", true, "leading class hyphen");
            Match("a-", "^[a-]+$", true, "trailing class hyphen");
            Match("]", @"^[\]]$", true, "escaped closing bracket");
            Match("a cat!", @"\bcat\b", true, "word boundaries");
            Match("cats", @"\bcat\b", false, "word suffix boundary");
            Match("_cat", @"\bcat\b", false, "underscore word boundary");
            Match("écaté", @"\bcat\b", true, "ASCII boundary semantics");
            Match("", @"\b", false, "no empty word boundary");
            Match("한글 😀", "한글", true, "Unicode literal");
            Match("😀", "^😀$", true, "UTF16 literal pair");
            Match("aaab", "^a*aab$", true, "overlapping star states");
            Match("abc", "^a?b?c?$", true, "epsilon chain");
            Match("", "^a?b?c?$", true, "empty epsilon chain");

            string[] invalid = { null, "", ".", "a|b", "(a)", "(?:a)", "(?=a)", "(?<=a)",
                @"\1", @"\x61", @"\u0061", @"\D", @"\W", @"\S", @"\q", "\\", "[", "[]", "[^]",
                "[z-a]", @"[\d-z]", @"[a-\w]", @"[\b]", "a{", "a{}", "a{1,}", "a{,2}", "a{2,1}",
                "a{257}", "a{999999999999}", "a**", "a+?", "a??", "*a", "a}", "a]", "^*", "$?", @"\b+",
                new string('a', 513), "a{256}b{256}c{256}d{256}e{256}f{256}g{256}h{256}" };
            foreach (string pattern in invalid) Reject(pattern);
            Match(new string('x', PolicyEngine.MaxTextLength), "PGTEST_[A-Z0-9]{16}", false, "large safe text stays allowed");
            Match(new string('x', PolicyEngine.MaxTextLength + 1), "absent", true, "oversized input fails closed");
            string adversarial = "";
            for (int i = 0; i < 64; i++) adversarial += "a*";
            var timer = Stopwatch.StartNew();
            Match(new string('a', PolicyEngine.MaxTextLength), adversarial + "b", true, "operation budget fails closed");
            Check(timer.ElapsedMilliseconds < 10000, "adversarial matching completes within bounded time");
            for (int i = 0; i < 150; i++) Match("cache" + i, "cache" + i, true, "bounded cache eviction");

            Rule rule = ExampleRule();
            Check(PolicyEngine.TargetProcess(rule, "slack.EXE"), "case insensitive executable");
            Check(!PolicyEngine.TargetProcess(rule, "fakeSlack.exe"), "no partial executable matching");
            Check(!PolicyEngine.TargetProcess(rule, @"C:\Apps\Slack.exe"), "process must be basename");
            Check(!PolicyEngine.TargetProcess(rule, "chrome.exe"), "unlisted executable");
            Check(PolicyEngine.TargetSite(rule, "https://slack.com/path?q=1"), "exact domain with path");
            Check(PolicyEngine.TargetSite(rule, "https://APP.SLACK.COM:8443/path"), "subdomain case and port");
            Check(PolicyEngine.TargetSite(rule, "https://a.b.notion.so"), "nested subdomain");
            Check(PolicyEngine.TargetSite(rule, "https://slack.com./"), "canonical DNS root dot");
            Check(!PolicyEngine.TargetSite(rule, "https://evilslack.com"), "domain left boundary");
            Check(!PolicyEngine.TargetSite(rule, "https://slack.com.evil.example"), "domain right boundary");
            Check(!PolicyEngine.TargetSite(rule, "https://slack.com@evil.example/"), "userinfo is not hostname");
            Check(!PolicyEngine.TargetSite(rule, "https://evil.example/?next=https://slack.com"), "query is not hostname");
            Check(!PolicyEngine.TargetSite(rule, "file://slack.com/a"), "HTTP and HTTPS only");
            Check(!PolicyEngine.TargetSite(rule, "slack.com"), "absolute URL required");
            Check(!PolicyEngine.TargetSite(rule, null), "missing URL");
            var wildcard = new Rule { sites = new List<string> { "*.slack.com" } };
            Check(!PolicyEngine.TargetSite(wildcard, "https://slack.com/"), "wildcard excludes apex");
            Check(PolicyEngine.TargetSite(wildcard, "https://app.slack.com/"), "wildcard includes subdomain");
            Check(PolicyEngine.TargetSite(new Rule { sites = new List<string> { "localhost" } }, "http://localhost:8080"), "localhost");

            var policy = new Policy { rules = new List<Rule> { rule } };
            Check(PolicyEngine.Validate(policy).Count == 0, "valid policy");
            string token = "PGTEST_ABCD1234EFGH5678";
            Check(PolicyEngine.Matches(policy, token, "Slack.exe", null).Count == 1, "native matching");
            Check(PolicyEngine.Matches(policy, token, "chrome.exe", "https://notion.site").Count == 1, "website matching");
            Check(PolicyEngine.Matches(policy, token, "notepad.exe", "https://example.com").Count == 0, "target required");
            Check(PolicyEngine.Matches(policy, "ordinary", "Slack.exe", null).Count == 0, "content match required");
            rule.enabled = false;
            Check(PolicyEngine.Matches(policy, token, "Slack.exe", null).Count == 0, "disabled rule");
            rule.enabled = true; policy.enabled = false;
            Check(PolicyEngine.Matches(policy, token, "Slack.exe", null).Count == 0, "disabled policy");
            policy.enabled = true;
            rule.pattern = "(unsafe)+";
            Check(PolicyEngine.Matches(policy, "ordinary", "Slack.exe", null).Count == 1, "invalid active pattern fails closed");
            Check(PolicyEngine.Validate(policy).Count > 0, "invalid pattern rejected in policy");
            rule.pattern = "PGTEST_[A-Z0-9]{16}";
            string[] badSites = { "https://slack.com", "slack.com/path", "slack.com:443", "*slack.com", "foo.*.com", "slack.com.", "-slack.com", "slack-.com", "slack..com", "slack", "슬랙.com", "" };
            foreach (string site in badSites)
            {
                rule.sites = new List<string> { site };
                Check(PolicyEngine.Validate(policy).Count > 0, "invalid domain scope rejected");
            }
            rule.sites = new List<string> { "slack.com" };
            string[] badProcesses = { "*.exe", "Slack", @"C:\Slack.exe", " Slack.exe", "Slack.exe ", ".exe", "a?b.exe", "a\nb.exe" };
            foreach (string process in badProcesses)
            {
                rule.processes = new List<string> { process };
                Check(PolicyEngine.Validate(policy).Count > 0, "invalid executable target rejected");
            }
            rule.processes = new List<string>(); rule.sites = new List<string>();
            Check(PolicyEngine.Validate(policy).Count > 0, "targetless rule rejected");
            policy.rules = new List<Rule> { ExampleRule(), ExampleRule() };
            Check(PolicyEngine.Validate(policy).Count > 0, "duplicate IDs rejected");
            policy.rules = new List<Rule> { ExampleRule() }; policy.version = 2;
            Check(PolicyEngine.Validate(policy).Count > 0, "unknown version rejected");
            policy.version = 1;
            var serializer = new JavaScriptSerializer();
            string json = serializer.Serialize(policy);
            Check(json.Contains("\"version\":1") && json.Contains("\"processes\"") && json.Contains("\"pattern\""), "camelCase JSON contract");
            var roundtrip = serializer.Deserialize<Policy>(json);
            Check(PolicyEngine.Validate(roundtrip).Count == 0, "JSON round trip");
            Check(PolicyEngine.Matches(roundtrip, token, "Notion.exe", null).Count == 1, "round trip rule works");
            Check(PolicyEngine.Validate(null).Count > 0, "missing policy rejected");
            Check(PolicyEngine.Validate(new Policy { rules = null }).Count > 0, "missing rules rejected");
            Check(PolicyEngine.Validate(new Policy { rules = new List<Rule> { null } }).Count > 0, "missing rule rejected");
            Console.WriteLine("PASS: " + checks + " policy and safe matcher checks.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error.Message); return 1; }
    }
}
