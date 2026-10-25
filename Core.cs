using System;
using System.Collections.Generic;

namespace PasteGuard
{
    public sealed class Policy
    {
        public int version = 1;
        public bool enabled = true;
        public List<Rule> rules = new List<Rule>();
    }

    public sealed class Rule
    {
        public string id;
        public string name;
        public bool enabled = true;
        public List<string> processes = new List<string>();
        public List<string> sites = new List<string>();
        public string pattern;
    }

    // Deliberately does not use System.Text.RegularExpressions for user input.
    public static class PolicyEngine
    {
        public const int MaxPatternLength = 512;
        public const int MaxRepeat = 256;
        public const int MaxStates = 2048;
        public const int MaxTextLength = 65536;
        public const int MaxEvaluationSteps = 8000000;
        private static readonly Dictionary<string, List<Node>> cache = new Dictionary<string, List<Node>>(StringComparer.Ordinal);
        private static readonly Queue<string> cacheOrder = new Queue<string>();

        public static List<string> Validate(Policy policy)
        {
            var errors = new List<string>();
            if (policy == null) { errors.Add("Policy is missing."); return errors; }
            if (policy.version != 1) errors.Add("Only policy version 1 is supported.");
            if (policy.rules == null || policy.rules.Count > 100)
            { errors.Add("Rules must be a list of at most 100 entries."); return errors; }
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < policy.rules.Count; i++)
            {
                Rule rule = policy.rules[i];
                string prefix = "Rule " + (i + 1) + ": ";
                if (rule == null) { errors.Add(prefix + "Rule is missing."); continue; }
                if (String.IsNullOrWhiteSpace(rule.id) || rule.id.Length > 64 || !ids.Add(rule.id))
                    errors.Add(prefix + "ID must be unique and contain 1 to 64 characters.");
                if (String.IsNullOrWhiteSpace(rule.name) || rule.name.Length > 80)
                    errors.Add(prefix + "Name must contain 1 to 80 characters.");
                if (rule.processes == null || rule.sites == null)
                    errors.Add(prefix + "Process and site targets must be lists.");
                else
                {
                    if (rule.processes.Count + rule.sites.Count == 0)
                        errors.Add(prefix + "At least one process or site target is required.");
                    if (rule.processes.Count > 128 || rule.sites.Count > 128)
                        errors.Add(prefix + "At most 128 process and 128 site targets are supported.");
                    foreach (string process in rule.processes)
                        if (!ValidProcess(process)) errors.Add(prefix + "Use an exact executable filename, such as Slack.exe.");
                    foreach (string site in rule.sites)
                        if (!ValidSite(site)) errors.Add(prefix + "Use a hostname or *.hostname without paths or ports.");
                }
                try { Compile(rule.pattern); }
                catch (ArgumentException error) { errors.Add(prefix + error.Message); }
            }
            return errors;
        }

        public static bool TargetProcess(Rule rule, string exe)
        {
            if (rule == null || rule.processes == null || !ValidProcess(exe)) return false;
            foreach (string target in rule.processes)
                if (String.Equals(target, exe, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public static bool TargetSite(Rule rule, string url)
        {
            Uri parsed;
            if (rule == null || rule.sites == null || !Uri.TryCreate(url, UriKind.Absolute, out parsed)
                || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps)) return false;
            string host;
            try { host = parsed.IdnHost.ToLowerInvariant(); }
            catch (UriFormatException) { return false; }
            if (host.EndsWith(".", StringComparison.Ordinal)) host = host.Substring(0, host.Length - 1);
            foreach (string scope in rule.sites)
            {
                if (!ValidSite(scope)) continue;
                string target = scope.ToLowerInvariant();
                if (target.StartsWith("*.", StringComparison.Ordinal))
                {
                    string suffix = target.Substring(1);
                    if (host.Length > suffix.Length && host.EndsWith(suffix, StringComparison.Ordinal)) return true;
                }
                else if (host == target) return true;
            }
            return false;
        }

        public static List<string> Matches(Policy policy, string text, string process, string url)
        {
            var matches = new List<string>();
            if (policy == null || !policy.enabled || policy.rules == null) return matches;
            foreach (Rule rule in policy.rules)
            {
                if (rule == null || !rule.enabled || (!TargetProcess(rule, process) && !TargetSite(rule, url))) continue;
                bool match;
                try { match = MatchText(text, rule.pattern); }
                catch (ArgumentException) { match = true; } // Reject unsafe policy changes conservatively.
                if (match) matches.Add(rule.name ?? "Unnamed rule");
            }
            return matches;
        }

        // A true result on either limit means "block conservatively", not a proven content match.
        // UTF-16 code units and ASCII classes make these semantics reproducible in JavaScript.
        public static bool MatchText(string text, string pattern)
        {
            List<Node> nodes = CachedCompile(pattern);
            text = text ?? "";
            if (text.Length > MaxTextLength) return true;
            var current = new bool[nodes.Count + 1];
            var next = new bool[nodes.Count + 1];
            int steps = 0;
            int high = 0;
            for (int position = 0; position <= text.Length; position++)
            {
                current[0] = true; // Unanchored search starts at every position.
                for (int i = 0; i <= high && i < nodes.Count; i++)
                {
                    if (++steps > MaxEvaluationSteps) return true;
                    if (!current[i]) continue;
                    Node node = nodes[i];
                    if (node.kind == Kind.Optional || node.kind == Kind.Star
                        || (node.kind == Kind.Begin && position == 0)
                        || (node.kind == Kind.End && position == text.Length)
                        || (node.kind == Kind.Boundary && WordAt(text, position - 1) != WordAt(text, position)))
                    { current[i + 1] = true; if (i + 1 > high) high = i + 1; }
                }
                if (current[nodes.Count]) return true;
                if (position == text.Length) return false;
                Array.Clear(next, 0, next.Length);
                int nextHigh = 0;
                for (int i = 0; i <= high && i < nodes.Count; i++)
                {
                    if (++steps > MaxEvaluationSteps) return true;
                    Node node = nodes[i];
                    if (current[i] && node.atom != null && node.atom.Matches(text[position], ref steps))
                    {
                        int destination = node.kind == Kind.Star ? i : i + 1;
                        next[destination] = true;
                        if (destination > nextHigh) nextHigh = destination;
                    }
                    if (steps > MaxEvaluationSteps) return true;
                }
                bool[] swap = current; current = next; next = swap;
                high = nextHigh;
            }
            return false;
        }

        private static bool ValidProcess(string value)
        {
            if (String.IsNullOrWhiteSpace(value) || value.Length > 260 || value != value.Trim()
                || !value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                || value.Length == 4 || value.IndexOfAny("<>:\"/\\|?*".ToCharArray()) >= 0) return false;
            foreach (char c in value) if (Char.IsControl(c)) return false;
            return true;
        }

        private static bool ValidSite(string value)
        {
            if (String.IsNullOrEmpty(value)) return false;
            string host = value.StartsWith("*.", StringComparison.Ordinal) ? value.Substring(2) : value;
            if (host.Length > 253 || (host.IndexOf('.') < 0 && !host.Equals("localhost", StringComparison.OrdinalIgnoreCase))) return false;
            foreach (string label in host.Split('.'))
            {
                if (label.Length == 0 || label.Length > 63 || label[0] == '-' || label[label.Length - 1] == '-') return false;
                foreach (char c in label)
                    if (!AsciiLetter(c) && !Digit(c) && c != '-') return false;
            }
            return true;
        }

        private static bool AsciiLetter(char c) { return (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z'); }
        private static bool Digit(char c) { return c >= '0' && c <= '9'; }
        private static bool WordAt(string text, int i)
        { return i >= 0 && i < text.Length && (AsciiLetter(text[i]) || Digit(text[i]) || text[i] == '_'); }

        private enum Kind { Atom, Optional, Star, Begin, End, Boundary }
        private sealed class Node
        {
            public Kind kind;
            public Atom atom;
            public Node(Kind kind, Atom atom) { this.kind = kind; this.atom = atom; }
        }

        private sealed class Atom
        {
            public bool negate;
            public List<int> ranges = new List<int>();
            public void Add(char start, char end) { ranges.Add(start); ranges.Add(end); }
            public bool Matches(char c, ref int steps)
            {
                bool found = false;
                for (int i = 0; i < ranges.Count; i += 2)
                {
                    if (++steps > MaxEvaluationSteps) return false;
                    if (c >= ranges[i] && c <= ranges[i + 1]) { found = true; break; }
                }
                return negate ? !found : found;
            }
        }

        private static ArgumentException Bad(string message) { return new ArgumentException(message); }

        private static List<Node> CachedCompile(string pattern)
        {
            if (pattern == null) throw Bad("Pattern must contain 1 to 512 characters.");
            lock (cache)
            {
                List<Node> nodes;
                if (cache.TryGetValue(pattern, out nodes)) return nodes;
                nodes = Compile(pattern);
                if (cache.Count >= 128) cache.Remove(cacheOrder.Dequeue());
                cache.Add(pattern, nodes); cacheOrder.Enqueue(pattern);
                return nodes;
            }
        }

        private static List<Node> Compile(string pattern)
        {
            if (String.IsNullOrEmpty(pattern) || pattern.Length > MaxPatternLength)
                throw Bad("Pattern must contain 1 to 512 characters.");
            var nodes = new List<Node>();
            int at = 0;
            while (at < pattern.Length)
            {
                char c = pattern[at++];
                Kind kind = Kind.Atom;
                Atom atom = null;
                if (c == '^') kind = Kind.Begin;
                else if (c == '$') kind = Kind.End;
                else if (c == '\\' && at < pattern.Length && pattern[at] == 'b') { at++; kind = Kind.Boundary; }
                else if (c == '[') atom = ReadClass(pattern, ref at);
                else if (c == '\\') atom = ReadEscape(pattern, ref at);
                else
                {
                    if (".()|*+?{}]".IndexOf(c) >= 0) throw Bad("Unsupported or misplaced pattern operator.");
                    atom = Literal(c);
                }
                int minimum = 1, maximum = 1;
                bool unbounded = false;
                if (at < pattern.Length && "?*+{".IndexOf(pattern[at]) >= 0)
                {
                    if (kind != Kind.Atom) throw Bad("Assertions cannot be repeated.");
                    char quantifier = pattern[at++];
                    if (quantifier == '?') { minimum = 0; maximum = 1; }
                    else if (quantifier == '*') { minimum = 0; unbounded = true; }
                    else if (quantifier == '+') { minimum = 1; unbounded = true; }
                    else
                    {
                        minimum = ReadNumber(pattern, ref at); maximum = minimum;
                        if (at < pattern.Length && pattern[at] == ',') { at++; maximum = ReadNumber(pattern, ref at); }
                        if (at >= pattern.Length || pattern[at++] != '}' || maximum < minimum)
                            throw Bad("Use {n} or {n,m}, with 0 <= n <= m <= 256.");
                    }
                }
                if (kind != Kind.Atom) nodes.Add(new Node(kind, null));
                else
                {
                    for (int i = 0; i < minimum; i++) nodes.Add(new Node(Kind.Atom, atom));
                    if (unbounded) nodes.Add(new Node(Kind.Star, atom));
                    else for (int i = minimum; i < maximum; i++) nodes.Add(new Node(Kind.Optional, atom));
                }
                if (nodes.Count + 1 > MaxStates) throw Bad("Expanded pattern exceeds 2048 states.");
            }
            return nodes;
        }

        private static int ReadNumber(string pattern, ref int at)
        {
            int start = at, value = 0;
            while (at < pattern.Length && Digit(pattern[at]))
            {
                value = value * 10 + pattern[at++] - '0';
                if (value > MaxRepeat) throw Bad("Repeat counts cannot exceed 256.");
            }
            if (at == start) throw Bad("A numeric repeat count is required.");
            return value;
        }

        private static Atom Literal(char c) { var atom = new Atom(); atom.Add(c, c); return atom; }

        private static Atom ReadEscape(string pattern, ref int at)
        {
            if (at >= pattern.Length) throw Bad("Trailing escape is incomplete.");
            char c = pattern[at++];
            var atom = new Atom();
            if (c == 'd') atom.Add('0', '9');
            else if (c == 'w') { atom.Add('A', 'Z'); atom.Add('a', 'z'); atom.Add('0', '9'); atom.Add('_', '_'); }
            else if (c == 's') { atom.Add('\t', '\r'); atom.Add(' ', ' '); }
            else if (c == 'n') atom.Add('\n', '\n');
            else if (c == 'r') atom.Add('\r', '\r');
            else if (c == 't') atom.Add('\t', '\t');
            else if (c == 'f') atom.Add('\f', '\f');
            else if (c == 'v') atom.Add('\v', '\v');
            else
            {
                if (AsciiLetter(c) || Digit(c)) throw Bad("Unsupported escape; use d, w, s, b, n, r, t, f, v, or escaped punctuation.");
                atom.Add(c, c);
            }
            return atom;
        }

        private static Atom ReadClass(string pattern, ref int at)
        {
            var result = new Atom();
            if (at < pattern.Length && pattern[at] == '^') { result.negate = true; at++; }
            int count = 0;
            while (at < pattern.Length && pattern[at] != ']')
            {
                Atom first = ReadClassPart(pattern, ref at);
                if (at + 1 < pattern.Length && pattern[at] == '-' && pattern[at + 1] != ']')
                {
                    at++;
                    Atom last = ReadClassPart(pattern, ref at);
                    if (first.ranges.Count != 2 || first.ranges[0] != first.ranges[1]
                        || last.ranges.Count != 2 || last.ranges[0] != last.ranges[1]
                        || first.ranges[0] > last.ranges[0]) throw Bad("Class ranges need ascending literal endpoints.");
                    result.ranges.Add(first.ranges[0]); result.ranges.Add(last.ranges[0]);
                }
                else result.ranges.AddRange(first.ranges);
                count++;
            }
            if (at >= pattern.Length || count == 0) throw Bad("Character class must be nonempty and closed.");
            at++;
            return result;
        }

        private static Atom ReadClassPart(string pattern, ref int at)
        {
            char c = pattern[at++];
            return c == '\\' ? ReadEscape(pattern, ref at) : Literal(c);
        }
    }
}
