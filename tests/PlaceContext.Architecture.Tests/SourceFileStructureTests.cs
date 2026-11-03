using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis;

namespace PlaceContext.Architecture.Tests;

public sealed class SourceFileStructureTests
{
    [Fact]
    public void Each_source_file_has_at_most_one_top_level_type()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "PlaceContext.slnx")))
        {
            root = root.Parent;
        }
        Assert.NotNull(root);
        var violations = new List<string>();
        foreach (var directory in new[] { "src", "tests" })
        {
            foreach (var path in Directory.EnumerateFiles(Path.Combine(root.FullName, directory), "*.cs", SearchOption.AllDirectories))
            {
                if (path.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"))
                {
                    continue;
                }
                var syntax = CSharpSyntaxTree.ParseText(File.ReadAllText(path)).GetRoot();
                var types = syntax.DescendantNodes().OfType<BaseTypeDeclarationSyntax>()
                    .Where(type => !type.Ancestors().OfType<BaseTypeDeclarationSyntax>().Any())
                    .ToArray();
                if (types.Length > 1)
                {
                    violations.Add($"{Path.GetRelativePath(root.FullName, path)}: {string.Join(", ", types.Select(type => type.Identifier.Text))}");
                }
                if (types.Length == 1 && !path.Split(Path.DirectorySeparatorChar).Contains("Migrations")
                                && Path.GetFileName(path).Split('.')[0] != types[0].Identifier.Text)
                {
                    violations.Add($"{Path.GetRelativePath(root.FullName, path)}: name the file after {types[0].Identifier.Text}");
                }
                if (path.Split(Path.DirectorySeparatorChar).Contains("Ports"))
                {
                    violations.Add($"{Path.GetRelativePath(root.FullName, path)}: place contracts in their owning feature's Interfaces or Models folder");
                }
            }
        }
        Assert.True(violations.Count == 0, "Source organization violations:\n" + string.Join('\n', violations));
    }
}
