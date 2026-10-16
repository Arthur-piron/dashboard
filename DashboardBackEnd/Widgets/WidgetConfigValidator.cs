using System.Text.Json;

namespace DashboardBackEnd.Widgets;

public static class WidgetConfigValidator
{
    public static List<string> Validate(IWidget widget, JsonElement config)
    {
        var errors = new List<string>();

        if (config.ValueKind != JsonValueKind.Object)
        {
            errors.Add("Config must be a JSON object.");
            return errors;
        }

        foreach (var param in widget.Parameters)
        {
            if (!config.TryGetProperty(param.Name, out var value) || value.ValueKind == JsonValueKind.Null)
            {
                if (param.Required) errors.Add($"'{param.Name}' is required.");
                continue;
            }

            switch (param.Type)
            {
                case ParamType.Integer:
                    if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var n))
                    {
                        errors.Add($"'{param.Name}' must be an integer.");
                        break;
                    }
                    if (param.Min is { } min && n < min) errors.Add($"'{param.Name}' must be >= {min}.");
                    if (param.Max is { } max && n > max) errors.Add($"'{param.Name}' must be <= {max}.");
                    break;

                case ParamType.String:
                    if (value.ValueKind != JsonValueKind.String)
                    {
                        errors.Add($"'{param.Name}' must be a string.");
                        break;
                    }
                    var s = value.GetString()!;
                    if (string.IsNullOrWhiteSpace(s) && param.Required)
                        errors.Add($"'{param.Name}' must not be empty.");
                    if (param.Options is { Length: > 0 } && !param.Options.Contains(s))
                        errors.Add($"'{param.Name}' must be one of: {string.Join(", ", param.Options)}.");
                    break;
            }
        }

        var known = widget.Parameters.Select(p => p.Name).ToHashSet();
        foreach (var prop in config.EnumerateObject())
        {
            if (!known.Contains(prop.Name)) errors.Add($"'{prop.Name}' is not a parameter of this widget.");
        }

        return errors;
    }
}
