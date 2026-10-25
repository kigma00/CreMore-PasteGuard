using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using PasteGuard;

static class BridgeTests
{
    const string Key = "dummy-test-pairing-key-0123456789012345";
    static int count, events;
    static int Request(string method, string path, string key, string origin, string body)
    {
        var request = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:17843" + path);
        request.Proxy = null; request.Method = method; request.Timeout = 2000;
        if (key != null) request.Headers["X-PasteGuard-Key"] = key;
        if (origin != null) request.Headers["Origin"] = origin;
        if (body != null)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(body); request.ContentType = "application/json"; request.ContentLength = bytes.Length;
            using (var stream = request.GetRequestStream()) stream.Write(bytes, 0, bytes.Length);
        }
        try { using (var response = (HttpWebResponse)request.GetResponse()) return (int)response.StatusCode; }
        catch (WebException error) { using (var response = (HttpWebResponse)error.Response) { if (response == null) throw; return (int)response.StatusCode; } }
    }
    static void Check(bool value) { if (!value) throw new Exception("Bridge check failed at " + count); count++; }
    static int Main()
    {
        try
        {
            using (var bridge = new Bridge(Key, () => "{\"version\":1,\"enabled\":false,\"rules\":[]}", (h, r, s) => { if (h == "test.invalid" && r == "dummy") Interlocked.Increment(ref events); }))
            {
                bridge.Start();
                const string extension = "chrome-extension://aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
                Check(Request("GET", "/policy", null, extension, null) == 401);
                Check(Request("GET", "/policy", "wrong-key", extension, null) == 401);
                Check(Request("GET", "/policy", Key, "https://malicious.invalid", null) == 403);
                Check(Request("GET", "/policy", Key, extension, null) == 200);
                Check(Request("GET", "/policy", Key, null, null) == 200);
                Check(Request("OPTIONS", "/policy", null, extension, null) == 204);
                Check(Request("OPTIONS", "/policy", null, "https://malicious.invalid", null) == 403);
                Check(Request("GET", "/missing", Key, extension, null) == 404);
                Check(Request("POST", "/events", Key, extension, "{\"host\":\"test.invalid\",\"rule\":\"dummy\",\"reason\":\"pattern\"}") == 200);
                Check(events == 1);
                Check(Request("POST", "/events", Key, extension, "not-json") == 400);
                Check(Request("POST", "/events", Key, extension, new string('x', 4097)) == 400);
                Check(Request("GET", "/policy", Key, extension, null) == 200);
                Check((DateTime.UtcNow - bridge.LastSeen).TotalSeconds < 3);
            }
            Console.WriteLine("PASS: " + count + " bridge checks"); return 0;
        }
        catch (Exception e) { Console.WriteLine("FAIL: " + e.GetType().Name + " " + e.Message); return 1; }
    }
}
