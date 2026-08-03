using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Chat.Infrastructure.HealthChecks;

/// <summary>
/// Renders a <see cref="HealthReport"/> as JSON. Lives here rather than in a host so Chat.Web and
/// Chat.Bot report health in exactly the same shape. Takes no dependency on ASP.NET Core — the hosts
/// own the HTTP concerns, this owns the payload.
/// </summary>
public static class HealthReportSerializer
{
    private static readonly JsonWriterOptions WriterOptions = new() { Indented = true };

    /// <summary>Serialises the report to the JSON body returned by the health endpoints.</summary>
    public static string ToJson(HealthReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream, WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteString("status", report.Status.ToString());
            writer.WriteNumber("totalDurationMs", report.TotalDuration.TotalMilliseconds);

            writer.WriteStartArray("checks");
            foreach (KeyValuePair<string, HealthReportEntry> entry in report.Entries)
            {
                writer.WriteStartObject();
                writer.WriteString("name", entry.Key);
                writer.WriteString("status", entry.Value.Status.ToString());
                writer.WriteString("description", entry.Value.Description);
                writer.WriteNumber("durationMs", entry.Value.Duration.TotalMilliseconds);

                // The exception message only — never the stack trace, which would leak internals
                // (server names, file paths) to anyone who can reach the endpoint.
                writer.WriteString("error", entry.Value.Exception?.Message);

                writer.WriteStartArray("tags");
                foreach (string tag in entry.Value.Tags)
                {
                    writer.WriteStringValue(tag);
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }
}
