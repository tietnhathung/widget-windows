using Microsoft.Windows.Widgets;
using System.Text.Json;

internal static class QuotaCard
{
    static object Text(string text, bool bold = false, bool muted = false,
        bool right = false, string color = "Default") => new {
        type = "TextBlock", text, size = "Small", wrap = true, spacing = "None",
        weight = bold ? "Bolder" : "Default", isSubtle = muted, color,
        horizontalAlignment = right ? "Right" : "Left"
    };

    static object Column(string width, object content) =>
        new { type = "Column", width, spacing = "Small", items = new[] { content } };

    static object Line(object label, object session, object week) => new {
        type = "ColumnSet", spacing = "Small",
        columns = new[] { Column("stretch", label), Column("44px", session), Column("44px", week) }
    };

    static object Spacer(string height) =>
        new { type = "Container", spacing = "None", minHeight = height, items = Array.Empty<object>() };

    static object ServiceBlock(List<object> content)
    {
        var edge = new { type = "Column", width = "4px", spacing = "None", items = Array.Empty<object>() };
        var center = new { type = "Column", width = "stretch", spacing = "None", items = content };
        var row = new { type = "ColumnSet", spacing = "None", columns = new object[] { edge, center, edge } };
        return new { type = "Container", style = "emphasis", spacing = "None",
            items = new object[] { Spacer("4px"), row, Spacer("4px") } };
    }

    static object Value(IEnumerable<Quotas.Limit> limits, string label)
    {
        var value = limits.Where(l => l.Label == label).MinBy(l => int.Parse(l.Value.TrimEnd('%')))?.Value;
        var color = int.TryParse(value?.TrimEnd('%'), out var percent) && percent <= 10 ? "Attention" : "Default";
        return Text(value ?? "—", bold: value != null, muted: value == null, right: true, color: color);
    }

    internal static string Render(Quotas.Row[] rows, WidgetSize size = WidgetSize.Large)
    {
        var body = new List<object> {
            Line(Text(size == WidgetSize.Small ? "Còn lại thấp nhất" : "Hạn mức còn lại", muted: true), Text("5 giờ", muted: true, right: true),
                Text("Tuần", muted: true, right: true))
        };
        foreach (var row in rows)
        {
            if (size == WidgetSize.Small)
            {
                var limits = row.Data.Groups.SelectMany(g => g.Limits);
                body.Add(Line(Text(row.Name, bold: true), Value(limits, "5 giờ"), Value(limits, "Tuần")));
                continue;
            }
            var items = new List<object>();
            if (row.Data.Groups.Count == 1)
            {
                var group = row.Data.Groups[0];
                items.Add(Line(Text(row.Name, bold: true), Value(group.Limits, "5 giờ"), Value(group.Limits, "Tuần")));
                if (!string.IsNullOrEmpty(group.Name)) items.Add(Text(group.Name, muted: true));
                AddOtherLimits(items, group);
            }
            else
            {
                items.Add(Text(row.Name, bold: true));
                foreach (var group in row.Data.Groups)
                {
                    items.Add(Line(Text(group.Name), Value(group.Limits, "5 giờ"), Value(group.Limits, "Tuần")));
                    AddOtherLimits(items, group);
                }
            }
            if (row.Data.Note != null) items.Add(Text(row.Data.Note, muted: true));
            if (body.Count > 1) body.Add(Spacer("6px"));
            body.Add(ServiceBlock(items));
        }
        if (size == WidgetSize.Small)
            return JsonSerializer.Serialize(new { type = "AdaptiveCard", version = "1.5", body });
        return JsonSerializer.Serialize(new { type = "AdaptiveCard", version = "1.5", body,
            actions = new[] { new { type = "Action.Execute", title = "Làm mới", verb = "refresh" } } });
    }

    static void AddOtherLimits(List<object> items, Quotas.Group group)
    {
        foreach (var limit in group.Limits.Where(l => l.Label is not ("5 giờ" or "Tuần")))
            items.Add(Text($"{limit.Label} · {limit.Value}", muted: true));
    }
}
