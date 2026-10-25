using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace PasteGuard
{
    // Loopback only. Pairing key is required even for read-only policy access.
    sealed class Bridge : IDisposable
    {
        readonly string key;
        readonly Func<string> policy;
        readonly Action<string, string, string> blocked;
        TcpListener listener;
        long lastSeen;
        public bool IsRunning { get { return listener != null; } }
        public DateTime LastSeen { get { return new DateTime(Interlocked.Read(ref lastSeen), DateTimeKind.Utc); } }

        public Bridge(string key, Func<string> policy, Action<string, string, string> blocked)
        { this.key = key; this.policy = policy; this.blocked = blocked; }

        public void Start()
        {
            listener = new TcpListener(IPAddress.Loopback, 17843);
            try { listener.Start(8); }
            catch { listener = null; throw; }
            var server = listener;
            var thread = new Thread(() =>
            {
                while (listener != null)
                {
                    try
                    {
                        // One short request at a time; a local client cannot create unbounded worker threads.
                        using (var client = server.AcceptTcpClient()) Handle(client);
                    }
                    catch (SocketException) { }
                    catch (ObjectDisposedException) { break; }
                    catch (IOException) { }
                    catch (Exception) { /* A failed request or closing UI must not terminate the application. */ }
                }
            }) { IsBackground = true, Name = "PasteGuard browser bridge" };
            thread.Start();
        }

        void Handle(TcpClient client)
        {
            client.ReceiveTimeout = 1500; client.SendTimeout = 1500;
            using (var stream = client.GetStream())
            {
                var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var first = ReadLine(stream, 2048).Split(' ');
                if (first.Length != 3) return;
                int total = 0;
                while (true)
                {
                    var line = ReadLine(stream, 4096); total += line.Length;
                    if (total > 16384) return;
                    if (line.Length == 0) break;
                    int colon = line.IndexOf(':');
                    if (colon < 1) return;
                    headers[line.Substring(0, colon).Trim()] = line.Substring(colon + 1).Trim();
                }
                string origin, supplied, host;
                headers.TryGetValue("Origin", out origin);
                headers.TryGetValue("X-PasteGuard-Key", out supplied);
                headers.TryGetValue("Host", out host);
                if (host != "127.0.0.1:17843" || !AllowedOrigin(origin)) { Reply(stream, 403, "{}", null); return; }
                if (first[0] == "OPTIONS") { Reply(stream, 204, "", origin); return; }
                if (!SameKey(supplied)) { Reply(stream, 401, "{}", origin); return; }
                if (first[0] == "GET" && first[1] == "/policy")
                {
                    Interlocked.Exchange(ref lastSeen, DateTime.UtcNow.Ticks);
                    Reply(stream, 200, policy(), origin); return;
                }
                if (first[0] == "POST" && (first[1] == "/events" || first[1] == "/heartbeat"))
                {
                    int length; string lengthText;
                    if (headers.ContainsKey("Transfer-Encoding") || !headers.TryGetValue("Content-Length", out lengthText) ||
                        !Int32.TryParse(lengthText, out length) || length < 0 || length > 4096)
                    { Reply(stream, 400, "{}", origin); return; }
                    var bytes = new byte[length]; int read = 0;
                    while (read < length) { int count = stream.Read(bytes, read, length - read); if (count == 0) return; read += count; }
                    try
                    {
                        if (first[1] == "/events")
                        {
                            var data = new JavaScriptSerializer().Deserialize<Dictionary<string, string>>(Encoding.UTF8.GetString(bytes));
                            string hostname, rule, reason;
                            if (data == null || !data.TryGetValue("host", out hostname) || !data.TryGetValue("rule", out rule)) throw new FormatException();
                            data.TryGetValue("reason", out reason);
                            if (hostname.Length > 253 || rule.Length > 128) throw new FormatException();
                            blocked(hostname, rule, reason);
                        }
                        Interlocked.Exchange(ref lastSeen, DateTime.UtcNow.Ticks);
                        Reply(stream, 200, "{\"ok\":true}", origin);
                    }
                    catch (Exception) { Reply(stream, 400, "{}", origin); }
                    return;
                }
                Reply(stream, 404, "{}", origin);
            }
        }

        bool SameKey(string supplied)
        {
            if (supplied == null || supplied.Length != key.Length) return false;
            int difference = 0; for (int i = 0; i < key.Length; i++) difference |= key[i] ^ supplied[i];
            return difference == 0;
        }
        static bool AllowedOrigin(string origin)
        {
            if (String.IsNullOrEmpty(origin)) return true; // Some extension fetches omit Origin; key still required.
            const string prefix = "chrome-extension://";
            if (!origin.StartsWith(prefix, StringComparison.Ordinal) || origin.Length != prefix.Length + 32) return false;
            foreach (char c in origin.Substring(prefix.Length)) if (c < 'a' || c > 'p') return false;
            return true;
        }
        static string ReadLine(Stream stream, int limit)
        {
            var line = new StringBuilder();
            while (line.Length <= limit)
            {
                int b = stream.ReadByte(); if (b < 0) throw new IOException();
                if (b == '\n') return line.ToString().TrimEnd('\r');
                if (b > 127) throw new IOException();
                line.Append((char)b);
            }
            throw new IOException();
        }
        static void Reply(Stream stream, int code, string body, string origin)
        {
            var bytes = Encoding.UTF8.GetBytes(body);
            string status = code == 200 ? "OK" : code == 204 ? "No Content" : code == 401 ? "Unauthorized" : code == 403 ? "Forbidden" : code == 404 ? "Not Found" : "Bad Request";
            var header = "HTTP/1.1 " + code + " " + status + "\r\nContent-Type: application/json; charset=utf-8\r\nCache-Control: no-store\r\nConnection: close\r\nContent-Length: " + bytes.Length + "\r\n";
            if (!String.IsNullOrEmpty(origin)) header += "Access-Control-Allow-Origin: " + origin + "\r\nVary: Origin\r\nAccess-Control-Allow-Methods: GET, POST, OPTIONS\r\nAccess-Control-Allow-Headers: X-PasteGuard-Key, Content-Type\r\nAccess-Control-Allow-Private-Network: true\r\n";
            var head = Encoding.ASCII.GetBytes(header + "\r\n"); stream.Write(head, 0, head.Length); stream.Write(bytes, 0, bytes.Length);
        }
        public void Dispose() { var old = listener; listener = null; if (old != null) old.Stop(); }
    }
}
