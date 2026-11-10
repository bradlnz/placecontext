using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis;

namespace PlaceContext.Architecture.Tests;

public sealed class CodeStyleTests
{
    [Fact]
    public void Authored_csharp_uses_braced_blocks_and_guard_clauses()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "PlaceContext.slnx")))
        {
            directory = directory.Parent;
        }
        Assert.NotNull(directory);
        var violations = new List<string>();
        foreach (var folder in new[] { "src", "tests" })
        {
            foreach (var file in Directory.EnumerateFiles(Path.Combine(directory.FullName, folder), "*.cs", SearchOption.AllDirectories))
            {
                if (file.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj" or "Migrations"))
                {
                    continue;
                }
                var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(file));
                foreach (var node in tree.GetRoot().DescendantNodes())
                {
                    var body = node switch
                    {
                        IfStatementSyntax statement => statement.Statement,
                        ForStatementSyntax statement => statement.Statement,
                        ForEachStatementSyntax statement => statement.Statement,
                        ForEachVariableStatementSyntax statement => statement.Statement,
                        WhileStatementSyntax statement => statement.Statement,
                        DoStatementSyntax statement => statement.Statement,
                        LockStatementSyntax statement => statement.Statement,
                        UsingStatementSyntax statement => statement.Statement,
                        _ => null
                    };
                    var problem = node is ElseClauseSyntax ? "use guard clauses instead of else"
                        : body is not null and not BlockSyntax ? "brace control-flow bodies"
                        : node is BlockSyntax { Statements.Count: > 0 } block
                            && (Line(block.OpenBraceToken) == Line(block.Statements[0].GetFirstToken())
                                || Line(block.CloseBraceToken) == Line(block.Statements[^1].GetLastToken()))
                            ? "put block statements on separate lines" : null;
                    if (problem is not null)
                    {
                        violations.Add($"{Path.GetRelativePath(directory.FullName, file)}:{node.GetLocation().GetLineSpan().StartLinePosition.Line + 1}: {problem}");
                    }
                }
            }
        }
        Assert.True(violations.Count == 0, string.Join('\n', violations));
    }

    private static int Line(SyntaxToken token) => token.GetLocation().GetLineSpan().StartLinePosition.Line;
}
