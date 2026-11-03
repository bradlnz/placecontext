using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace PlaceContext.Application.Shared;

public static class PromptText
{
    private static readonly ConcurrentDictionary<string, string> Templates = new(StringComparer.Ordinal);
    private static readonly Regex Placeholder = new(@"\{\{(?<name>\w+)\}\}", RegexOptions.CultureInvariant);

    public static string Read(string name) => Templates.GetOrAdd(name, static resourceName =>
    {
        using var stream = typeof(PromptText).Assembly.GetManifestResourceStream($"PlaceContext.Application.{resourceName}.md")
            ?? throw new InvalidOperationException($"Embedded Markdown prompt '{resourceName}' was not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    });

    public static string Render(string name, params (string Name, string Value)[] values)
    {
        var parameters = values.ToDictionary(value => value.Name, value => value.Value, StringComparer.Ordinal);
        // Substitute once so untrusted values cannot introduce new template instructions.
        return Placeholder.Replace(Read(name), match => parameters.TryGetValue(match.Groups["name"].Value, out var value)
            ? value
            : throw new ArgumentException($"Missing prompt parameter '{match.Groups["name"].Value}'.", nameof(values)));
    }
}
