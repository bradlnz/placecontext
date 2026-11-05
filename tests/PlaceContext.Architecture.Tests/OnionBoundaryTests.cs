using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.CSharp;

namespace PlaceContext.Architecture.Tests;

public sealed class OnionBoundaryTests
{
    [Theory]
    [InlineData("PlaceContext.Host")]
    public void Presentation_uses_application_contracts_and_only_composition_references_infrastructure(string project)
    {
        var violations = new List<string>();
        foreach (var path in SourceFiles(project))
        {
            if (Path.GetFileName(path) == "Program.cs")
            {
                continue;
            }
            var source = File.ReadAllText(path);
            foreach (var forbidden in new[] { "PlaceContext.Infrastructure", "Microsoft.EntityFrameworkCore", "PlaceContext.Domain.Repositories" })
            {
                var syntax = CSharpSyntaxTree.ParseText(source).GetRoot();
                if (syntax.DescendantNodes().OfType<NameSyntax>().Any(name => name.ToString().StartsWith(forbidden, StringComparison.Ordinal)))
                {
                    violations.Add($"{Path.GetRelativePath(Root, path)}: {forbidden}");
                }
            }
        }
        Assert.True(violations.Count == 0, string.Join('\n', violations));
    }

    [Fact]
    public void Owned_types_and_folders_are_named_for_capabilities()
    {
        string[] vendors = ["OpenSearch", "Slack", "Stripe", "Kubernetes", "Docker", "Qemu", "Qdrant", "Voyage", "Redis", "Npgsql", "Tailscale", "Postmark", "SendGrid", "Twilio", "TypeSafe", "Jev", "Codex", "Anthropic", "OpenAi", "ChatGpt"];
        var violations = new List<string>();
        foreach (var project in new[] { "PlaceContext.Domain", "PlaceContext.Application", "PlaceContext.Infrastructure", "PlaceContext.Host" })
        {
            foreach (var path in SourceFiles(project).Where(path => !path.Split(Path.DirectorySeparatorChar).Contains("Migrations")))
            {
                var syntax = CSharpSyntaxTree.ParseText(File.ReadAllText(path)).GetRoot();
                foreach (var type in syntax.DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
                {
                    if (vendors.Any(vendor => type.Identifier.ValueText.Contains(vendor, StringComparison.OrdinalIgnoreCase)))
                    {
                        violations.Add($"{Path.GetRelativePath(Root, path)}: {type.Identifier.ValueText}");
                    }
                }
                if (path.Split(Path.DirectorySeparatorChar).Any(part => vendors.Contains(part, StringComparer.OrdinalIgnoreCase)))
                {
                    violations.Add(Path.GetRelativePath(Root, path));
                }
            }
        }
        Assert.True(violations.Count == 0, string.Join('\n', violations));
    }

    private static IEnumerable<string> SourceFiles(string project) => Directory.EnumerateFiles(Path.Combine(Root, "src", project), "*.cs", SearchOption.AllDirectories)
        .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"));

    private static string Root
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "PlaceContext.slnx")))
            {
                directory = directory.Parent;
            }
            return directory?.FullName ?? throw new DirectoryNotFoundException("Solution root not found.");
        }
    }
}
