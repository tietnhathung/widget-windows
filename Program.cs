using Microsoft.Windows.Widgets;
using Microsoft.Windows.Widgets.Providers;
using System.Runtime.InteropServices;

using WidgetHelper;

[Guid("9D71363E-AEB1-4F50-BFD5-648BF87618CF")]
public sealed class QuotaProvider : IWidgetProvider
{
    static readonly object Gate = new();
    static Task<Quotas.Row[]> pending;
    static DateTime last;
    static Task<Quotas.Row[]> Snapshot(bool refresh)
    {
        lock (Gate)
        {
            if (pending == null || pending.IsCompleted && (refresh || DateTime.UtcNow - last > TimeSpan.FromMinutes(1)))
            {
                last = DateTime.UtcNow;
                pending = Quotas.Read();
            }
            return pending;
        }
    }

    static async Task Update(string id, WidgetSize size, bool refresh = false)
    {
        try
        {
            var rows = await Snapshot(refresh);
            WidgetManager.GetDefault().UpdateWidget(new WidgetUpdateRequestOptions(id) {
                Template=QuotaCard.Render(rows, size), Data="{}" });
        }
        catch (COMException) { /* The host may remove a widget while its query is in flight. */ }
    }
    public void CreateWidget(WidgetContext c) => _ = Update(c.Id, c.Size);
    public void Activate(WidgetContext c) => _ = Update(c.Id, c.Size);
    public void OnWidgetContextChanged(WidgetContextChangedArgs a) => _ = Update(a.WidgetContext.Id, a.WidgetContext.Size);
    public void OnActionInvoked(WidgetActionInvokedArgs a) { if (a.Verb == "refresh") _ = Update(a.WidgetContext.Id, a.WidgetContext.Size, true); }
    public void Deactivate(string id) {}
    public void DeleteWidget(string id, string state) {}
}

internal class Program
{
    [MTAThread]
    static void Main(string[] args)
    {
        if (args.Contains("--claude-status"))
        {
            Quotas.CaptureClaude(Console.In.ReadToEnd(), Quotas.ClaudePath);
            Console.WriteLine("Claude · " + Quotas.Claude(Quotas.ClaudePath));
            return;
        }
        if (args.Contains("--self-test"))
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
            try
            {
                Quotas.CaptureClaude("""{"rate_limits":{"five_hour":{"used_percentage":0,"resets_at":1},"seven_day":{"used_percentage":100,"resets_at":4102444800}}}""", path);
                var text = Quotas.Claude(path).ToString();
                if (!text.Contains("Tuần 0%") || text.Contains("5 giờ")) throw new InvalidDataException(text);
                Quotas.CaptureClaude("{}", path);
                if (Quotas.Claude(path).ToString().Contains('%')) throw new InvalidDataException();
                var card = QuotaCard.Render([new("Test", new([new("Model", [new("Tuần", "0%"), new("12 giờ", "50%")])]))]);
                using var json = System.Text.Json.JsonDocument.Parse(card);
                var block = json.RootElement.GetProperty("body")[1].GetProperty("items");
                var padded = block[1].GetProperty("columns");
                if (block[0].GetProperty("minHeight").GetString() != "4px" || padded[0].GetProperty("width").GetString() != "4px")
                    throw new InvalidDataException(card);
                var cells = padded[1].GetProperty("items")[0].GetProperty("columns");
                if (cells[1].GetProperty("items")[0].GetProperty("text").GetString() != "—" ||
                    cells[2].GetProperty("items")[0].GetProperty("text").GetString() != "0%") throw new InvalidDataException(card);
                Quotas.Row[] rows = [new("Antigravity", new([
                    new("A", [new("Tuần", "80%"), new("5 giờ", "9%")]), new("B", [new("Tuần", "6%")])])),
                    new("Claude", Quotas.Reading.Status("Chưa có dữ liệu"))];
                var mediumCard = QuotaCard.Render(rows, WidgetSize.Medium);
                if (mediumCard != QuotaCard.Render(rows, WidgetSize.Large) || !mediumCard.Contains("Action.Execute"))
                    throw new InvalidDataException(mediumCard);
                using var detailed = System.Text.Json.JsonDocument.Parse(mediumCard);
                if (detailed.RootElement.GetProperty("body")[2].GetProperty("minHeight").GetString() != "6px")
                    throw new InvalidDataException(mediumCard);
                var compact = QuotaCard.Render(rows, WidgetSize.Small);
                using var small = System.Text.Json.JsonDocument.Parse(compact);
                var lines = small.RootElement.GetProperty("body");
                string Cell(int row, int col) => lines[row].GetProperty("columns")[col].GetProperty("items")[0].GetProperty("text").GetString();
                if (small.RootElement.TryGetProperty("actions", out _) || Cell(1, 1) != "9%" ||
                    Cell(1, 2) != "6%" || Cell(2, 1) != "—") throw new InvalidDataException(compact);
                Console.WriteLine("OK");
            }
            finally { File.Delete(path); }
            return;
        }
        if (args.Contains("--probe"))
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            foreach (var row in Quotas.Read().GetAwaiter().GetResult()) Console.WriteLine($"{row.Name}: {row.Data}");
            return;
        }
        if (!args.Contains("-RegisterProcessAsComServer")) return;
        WinRT.ComWrappersSupport.InitializeComWrappers();
        using var manager = RegistrationManager<QuotaProvider>.RegisterProvider();
        manager.GetDisposedEvent().WaitOne();
    }
}


