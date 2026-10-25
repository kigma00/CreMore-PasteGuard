using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
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
            // Reused state buffers must not retain accepting states after an early
            // return, a smaller pattern, or an empty match.
            for (int i = 0; i < 20; i++)
            {
                Match(new string('a', 256), "^a{256}$", true, "large buffer match");
                Match("a", "b", false, "shorter pattern clears old state");
                Match("", "a{0}", true, "empty compiled pattern");
                Match("PPPGTEST_ABCD1234EFGH5678", "PGTEST_[A-Z0-9]{16}", true, "overlapping literal prefix");
                Match("PGTEST_ABCD1234EFGH5678", "PGTEST_[A-Z0-9]{17}", false, "prior accepting state cleared");
            }
            var workers = new Thread[4];
            var failures = new Exception[workers.Length];
            for (int i = 0; i < workers.Length; i++)
            {
                int worker = i;
                workers[i] = new Thread(delegate()
                {
                    try
                    {
                        string ownText = new string((char)('a' + worker), 128);
                        string ownPattern = "^" + (char)('a' + worker) + "{128}$";
                        for (int iteration = 0; iteration < 200; iteration++)
                            if (!PolicyEngine.MatchText(ownText, ownPattern) || PolicyEngine.MatchText("x", ownPattern))
                                throw new Exception("Concurrent matcher state leaked between threads.");
                    }
                    catch (Exception error) { failures[worker] = error; }
                });
                workers[i].Start();
            }
            for (int i = 0; i < workers.Length; i++)
            {
                Check(workers[i].Join(10000), "concurrent matcher completes");
                Check(failures[i] == null, "thread-local matcher workspace");
            }

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
            policy.rules = new List<Rule> { ExampleRule() }; policy.version = 3;
            Check(PolicyEngine.Validate(policy).Count > 0, "unknown version rejected");
            policy.version = 1;
            var serializer = new JavaScriptSerializer();
            string json = serializer.Serialize(policy);
            Check(json.Contains("\"version\":1") && json.Contains("\"processes\"") && json.Contains("\"pattern\""), "camelCase JSON contract");
            var roundtrip = serializer.Deserialize<Policy>(json);
            Check(PolicyEngine.Validate(roundtrip).Count == 0, "JSON round trip");
            Check(PolicyEngine.Matches(roundtrip, token, "Notion.exe", null).Count == 1, "round trip rule works");
            Check(roundtrip.rules[0].patterns == null && PolicyEngine.GetPatterns(roundtrip.rules[0])[0] == rule.pattern,
                "legacy policy preserves its single pattern");
            var absentPatterns = serializer.Deserialize<Policy>(json.Replace(",\"patterns\":null", ""));
            Check(PolicyEngine.Validate(absentPatterns).Count == 0 && PolicyEngine.RuleMatches(absentPatterns.rules[0], token),
                "legacy JSON without a patterns field remains readable");

            var multi = ExampleRule();
            multi.pattern = "LEGACY-IGNORED";
            multi.patterns = new List<string> { "FIRST_[0-9]{4}", "SECOND_[A-Z]{4}" };
            var multiPolicy = new Policy { rules = new List<Rule> { multi } };
            Check(multiPolicy.version == 2 && PolicyEngine.Validate(multiPolicy).Count == 0, "new policy supports pattern lists");
            Check(PolicyEngine.RuleMatches(multi, "FIRST_1234"), "first pattern blocks");
            Check(PolicyEngine.RuleMatches(multi, "SECOND_ABCD"), "second pattern blocks");
            Check(!PolicyEngine.RuleMatches(multi, "LEGACY-IGNORED"), "pattern list takes precedence over legacy field");
            multi.pattern = null;
            Check(PolicyEngine.Validate(multiPolicy).Count == 0, "v2 pattern list does not require a legacy field");
            Check(!PolicyEngine.RuleMatches(multi, "ordinary"), "all nonmatching patterns permit text");
            Check(PolicyEngine.Matches(multiPolicy, "FIRST_1234 SECOND_ABCD", "Slack.exe", null).Count == 1,
                "multiple matches produce one event per rule");
            Check(PolicyEngine.Matches(multiPolicy, "SECOND_ABCD", "notepad.exe", "https://example.com").Count == 0,
                "multiple patterns do not expand target scope");
            multi.enabled = false;
            Check(PolicyEngine.Matches(multiPolicy, "SECOND_ABCD", "Slack.exe", null).Count == 0, "disabled multipattern rule permits text");
            multi.enabled = true;
            string multiJson = serializer.Serialize(multiPolicy);
            var multiRoundtrip = serializer.Deserialize<Policy>(multiJson);
            Check(multiJson.Contains("\"version\":2") && multiJson.Contains("\"patterns\""), "v2 pattern-list serialization");
            Check(PolicyEngine.Validate(multiRoundtrip).Count == 0 && PolicyEngine.RuleMatches(multiRoundtrip.rules[0], "SECOND_ABCD"),
                "pattern list JSON round trip");
            multiPolicy.version = 1;
            Check(PolicyEngine.Validate(multiPolicy).Count > 0, "v1 cannot silently carry v2 patterns");
            multiPolicy.version = 2;
            multi.patterns.Add("(unsupported)");
            Check(PolicyEngine.Validate(multiPolicy).Count > 0, "one invalid additional pattern rejects saving");
            multi.patterns[2] = null;
            Check(PolicyEngine.Validate(multiPolicy).Count > 0, "null additional pattern rejects saving");
            multi.patterns[2] = "";
            Check(PolicyEngine.Validate(multiPolicy).Count > 0, "empty additional pattern rejects saving");
            multi.patterns.Clear();
            Check(PolicyEngine.Validate(multiPolicy).Count > 0, "explicit empty list does not fall back to legacy field");
            for (int i = 0; i < PolicyEngine.MaxPatternsPerRule; i++) multi.patterns.Add("a*a*a*a*b");
            Check(PolicyEngine.Validate(multiPolicy).Count == 0, "sixteen patterns are allowed");
            string costly = new string('a', PolicyEngine.MaxTextLength);
            Check(!PolicyEngine.MatchText(costly, multi.patterns[0]), "single costly nonmatch stays within budget");
            Check(PolicyEngine.RuleMatches(multi, costly), "patterns share one conservative rule inspection budget");
            multi.patterns.Add("seventeenth");
            Check(PolicyEngine.Validate(multiPolicy).Count > 0, "seventeenth pattern is rejected");
            multi.patterns.RemoveAt(16);
            var boundedPolicy = new Policy();
            for (int i = 0; i < 6; i++)
            {
                Rule bounded = ExampleRule(); bounded.id = "limit-" + i;
                bounded.patterns = new List<string>(multi.patterns); boundedPolicy.rules.Add(bounded);
            }
            Rule last = ExampleRule(); last.id = "limit-last"; last.patterns = new List<string> { "a", "b", "c", "d" };
            boundedPolicy.rules.Add(last);
            Check(PolicyEngine.Validate(boundedPolicy).Count == 0, "one hundred total patterns allowed");
            last.enabled = false; last.patterns.Add("e");
            Check(PolicyEngine.Validate(boundedPolicy).Count > 0, "total pattern cap includes disabled rules");
            Check(PolicyEngine.Validate(null).Count > 0, "missing policy rejected");
            Check(PolicyEngine.Validate(new Policy { rules = null }).Count > 0, "missing rules rejected");
            Check(PolicyEngine.Validate(new Policy { rules = new List<Rule> { null } }).Count > 0, "missing rule rejected");
            Console.WriteLine("PASS: " + checks + " policy and safe matcher checks.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error.Message); return 1; }
    }
}
