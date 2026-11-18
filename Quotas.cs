using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

internal static class Quotas
{
    internal record Limit(string Label, string Value);
    internal record Group(string Name, List<Limit> Limits);
    internal record Reading(List<Group> Groups, string Note = null)
    {
        internal static Reading Status(string note) => new([], note);
        public override string ToString() => string.Join(" · ", Groups.Select(g =>
            string.Join(" ", new[] { g.Name }.Concat(g.Limits.Select(l => $"{l.Label} {l.Value}"))).Trim())
            .Append(Note).Where(t => !string.IsNullOrEmpty(t)));
    }
    internal record Row(string Name, Reading Data);

    internal static string Percent(JsonNode value, double scale, bool used)
    {
        if (value is not JsonValue number || !number.TryGetValue<double>(out var n) ||
            !double.IsFinite(n) || n < 0 || n > scale) throw new FormatException();
        var pct = n / scale * 100;
        return $"{Math.Round(used ? 100 - pct : pct)}%";
    }

    static Process Start(string exe, params string[] args)
    {
        var info = new ProcessStartInfo(exe) {
            RedirectStandardInput = true, RedirectStandardOutput = true,
            RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
            WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        Process process;
        try { process = Process.Start(info); }
        catch (System.ComponentModel.Win32Exception e) when (e.NativeErrorCode == 2 && exe == "codex.exe")
        {
            var manager = new Windows.Management.Deployment.PackageManager();
            var package = manager.FindPackagesForUser("", "OpenAI.Codex_2p2nqsd0c76g0").First();
            info.FileName = Path.Combine(package.InstalledLocation.Path, "app", "resources", "codex.exe");
            process = Process.Start(info);
        }
        process.BeginErrorReadLine();
        return process;
    }

    static async Task<JsonNode> Reply(Process p, int id, CancellationToken token)
    {
        while (await p.StandardOutput.ReadLineAsync(token) is string line)
        {
            var msg = JsonNode.Parse(line);
            if ((int?)msg?["id"] == id) return msg["result"] ?? throw new FormatException();
        }
        throw new EndOfStreamException();
    }

    static async Task<Reading> Codex(CancellationToken token)
    {
        using var p = Start("codex.exe", "app-server");
        try
        {
            await p.StandardInput.WriteLineAsync("""{"id":1,"method":"initialize","params":{"clientInfo":{"name":"quota-widget","version":"1.0"}}}""");
            await Reply(p, 1, token);
            await p.StandardInput.WriteLineAsync("""{"method":"initialized"}""");
            await p.StandardInput.WriteLineAsync("""{"id":2,"method":"account/rateLimits/read","params":{}}""");
            var limits = (await Reply(p, 2, token))["rateLimits"];
            var parts = new List<Limit>();
            foreach (var key in new[] {"primary", "secondary"})
            {
                if (limits?[key] is not JsonObject window) continue;
                var minutes = (int?)window["windowDurationMins"];
                var label = minutes switch { 300 => "5 giờ", 10080 => "Tuần", > 0 => $"{minutes} phút", _ => key };
                parts.Add(new(label, Percent(window["usedPercent"],100,true)));
            }
            return parts.Count > 0 ? new Reading([new("Work/Codex", parts)]) : Reading.Status("Chưa có hạn mức từ Codex");
        }
        finally { if (!p.HasExited) p.Kill(true); }
    }

    static async Task<Reading> Antigravity(CancellationToken token)
    {
        using var p = Start("agy.exe","-p","/usage","--output-format","json");
        try
        {
            p.StandardInput.Close();
            var json = JsonNode.Parse(await p.StandardOutput.ReadToEndAsync(token));
            var lines = new List<Group>();
            foreach (var group in json["command"]["data"]["groups"].AsArray())
            {
                var parts = new List<Limit>();
                foreach (var bucket in group["buckets"].AsArray())
                {
                    var label = (string)bucket["window"] switch { "5h" => "5 giờ", "weekly" => "Tuần", var other => other };
                    parts.Add(new(label, Percent(bucket["remaining_fraction"],1,false)));
                }
                if (parts.Count > 0) lines.Add(new((string)group["name"], parts));
            }
            return lines.Count > 0 ? new Reading(lines) : Reading.Status("Chưa có hạn mức từ agy");
        }
        finally { if (!p.HasExited) p.Kill(true); }
    }

    internal static string ClaudePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "quota-widget.json");
    internal static void CaptureClaude(string input, string path)
    {
        var limits = JsonNode.Parse(input)?["rate_limits"];
        var saved = new JsonObject();
        foreach (var key in new[] {"five_hour", "seven_day"})
        {
            if (limits?[key] is not JsonObject window || window["used_percentage"] == null) continue;
            Percent(window["used_percentage"], 100, true);
            saved[key] = new JsonObject {
                ["used_percentage"] = window["used_percentage"].DeepClone(),
                ["resets_at"] = window["resets_at"]?.DeepClone()
            };
        }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + Guid.NewGuid() + ".tmp";
        try
        {
            File.WriteAllText(temp, new JsonObject { ["limits"] = saved }.ToJsonString());
            File.Move(temp, path, true);
        }
        finally { File.Delete(temp); }
    }

    internal static Reading Claude(string path)
    {
        if (!File.Exists(path)) return Reading.Status("Chờ dữ liệu từ Claude Code");
        var snapshot = JsonNode.Parse(File.ReadAllText(path));
        var parts = new List<Limit>();
        foreach (var (key,label) in new[]{("five_hour","5 giờ"),("seven_day","Tuần")})
        {
            if (snapshot["limits"]?[key] is not JsonObject w || w["used_percentage"] == null) continue;
            if ((long?)w["resets_at"] is long end && end <= DateTimeOffset.UtcNow.ToUnixTimeSeconds()) continue;
            parts.Add(new(label, Percent(w["used_percentage"],100,true)));
        }
        return parts.Count == 0 ? Reading.Status("Chờ phiên Claude Code mới") :
            new Reading([new("", parts)], $"{File.GetLastWriteTime(path):dd/MM HH:mm}");
    }

    internal static async Task<Row[]> Read()
    {
        async Task<Row> Fetch(string name, Func<CancellationToken,Task<Reading>> load, string failure)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { return new(name, await load(timeout.Token)); }
            catch (Exception) { return new(name, Reading.Status(failure)); }
        }
        return await Task.WhenAll(
            Fetch("ChatGPT", Codex, "Kiểm tra đăng nhập và CLI Codex"),
            Fetch("Antigravity", Antigravity, "Kiểm tra đăng nhập và CLI agy"),
            Fetch("Claude", _ => Task.FromResult(Claude(ClaudePath)), "Chờ dữ liệu Claude Code hợp lệ"));
    }
}

