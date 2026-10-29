using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace PlaceContext.Architecture.Tests;

public sealed class SourceFileStructureTests
{
    [Fact]
    public void Each_source_file_has_at_most_one_top_level_class_or_record_class()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "PlaceContext.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        var violations = new List<string>();
        foreach (var directory in new[] { "src", "tests" })
        foreach (var path in Directory.EnumerateFiles(Path.Combine(root.FullName, directory), "*.cs", SearchOption.AllDirectories))
        {
            if (path.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj")) continue;
            var syntax = CSharpSyntaxTree.ParseText(File.ReadAllText(path)).GetRoot();
            var types = syntax.DescendantNodes().OfType<BaseTypeDeclarationSyntax>()
                .Where(type => !type.Ancestors().OfType<BaseTypeDeclarationSyntax>().Any())
                .Where(type => type is ClassDeclarationSyntax || type is RecordDeclarationSyntax record
                    && !record.ClassOrStructKeyword.IsKind(SyntaxKind.StructKeyword))
                .Select(type => type.Identifier.Text).ToArray();
            if (types.Length > 1) violations.Add($"{Path.GetRelativePath(root.FullName, path)}: {string.Join(", ", types)}");
        }
        Assert.True(violations.Count == 0, "Move each class or record class into its own file:\n" + string.Join('\n', violations));
    }
}
