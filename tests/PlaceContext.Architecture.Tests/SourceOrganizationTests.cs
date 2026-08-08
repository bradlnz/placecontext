using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace PlaceContext.Architecture.Tests;

public sealed class SourceOrganizationTests
{
    private static readonly IReadOnlyDictionary<string, string> RoleSuffixes =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Assemblers"] = "Assembler",
            ["Bridges"] = "Bridge",
            ["Builders"] = "Builder",
            ["Calculators"] = "Calculator",
            ["Catalogs"] = "Catalog",
            ["Commands"] = "Command",
            ["Controllers"] = "Controller",
            ["Dispatchers"] = "Dispatcher",
            ["Executors"] = "Executor",
            ["Factories"] = "Factory",
            ["Guards"] = "Guard",
            ["Handlers"] = "Handler",
            ["Hooks"] = "Hook",
            ["Importers"] = "Importer",
            ["Mappers"] = "Mapper",
            ["Parsers"] = "Parser",
            ["Policies"] = "Policy",
            ["Providers"] = "Provider",
            ["Queries"] = "Query",
            ["Recorders"] = "Recorder",
            ["Repositories"] = "Repository",
            ["Resolvers"] = "Resolver",
            ["Runners"] = "Runner",
            ["Services"] = "Service",
            ["Validators"] = "Validator",
        };

    [Fact]
    public void Source_files_contain_at_most_one_top_level_type_and_match_its_name()
    {
        var failures = new List<string>();

        foreach (var file in SourceFiles())
        {
            var declarations = TopLevelTypes(file).ToList();
            if (declarations.Count > 1)
            {
                failures.Add($"{Relative(file)} declares {string.Join(", ", declarations.Select(TypeName))}");
                continue;
            }

            if (declarations.Count == 1)
            {
                var typeName = TypeName(declarations[0]);
                var fileName = Path.GetFileNameWithoutExtension(file);
                if (!fileName.Equals(typeName, StringComparison.Ordinal)
                    && !fileName.StartsWith(typeName + ".", StringComparison.Ordinal)
                    && !fileName.EndsWith("_" + typeName, StringComparison.Ordinal))
                {
                    failures.Add($"{Relative(file)} contains {typeName}");
                }
            }
        }

        Assert.True(failures.Count == 0, Environment.NewLine + string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Source_files_contain_at_most_one_declared_type_including_nested_types()
    {
        var failures = new List<string>();

        foreach (var file in SourceFiles())
        {
            var root = CSharpSyntaxTree.ParseText(File.ReadAllText(file)).GetCompilationUnitRoot();
            var types = root.DescendantNodes()
                .OfType<MemberDeclarationSyntax>()
                .Where(declaration => declaration is BaseTypeDeclarationSyntax or DelegateDeclarationSyntax)
                .Select(TypeName)
                .ToList();

            if (types.Count > 1)
                failures.Add($"{Relative(file)} declares {string.Join(", ", types)}");
        }

        Assert.True(failures.Count == 0, Environment.NewLine + string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Role_folders_only_contain_types_with_the_matching_role()
    {
        var failures = new List<string>();

        foreach (var file in SourceFiles())
        {
            var folder = Directory.GetParent(file)?.Name;
            if (folder is null || !RoleSuffixes.TryGetValue(folder, out var suffix))
                continue;

            foreach (var declaration in TopLevelTypes(file))
            {
                var typeName = TypeName(declaration);
                if (!typeName.EndsWith(suffix, StringComparison.Ordinal))
                    failures.Add($"{Relative(file)} contains {typeName}; {folder} requires *{suffix}");
            }
        }

        Assert.True(failures.Count == 0, Environment.NewLine + string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Service_projects_do_not_reference_other_service_implementations_or_runtimes()
    {
        var serviceNames = new[] { "AgentChat", "Agents", "Artifacts", "Crm", "Data", "Jobs", "Search", "Vault" };
        var failures = new List<string>();

        foreach (var service in serviceNames)
        {
            var serviceDirectory = Path.Combine(Root, "src", $"PlaceContext.{service}");
            var projects = new[]
            {
                Path.Combine(serviceDirectory, $"PlaceContext.{service}.csproj"),
                Path.Combine(serviceDirectory, $"PlaceContext.{service}.Infrastructure.csproj"),
                Path.Combine(serviceDirectory, $"PlaceContext.{service}.Api.csproj"),
            };

            foreach (var project in projects)
            {
                var text = File.ReadAllText(project);
                foreach (var other in serviceNames.Where(name => name != service))
                {
                    var forbidden = new[]
                    {
                        $"PlaceContext.{other}.csproj",
                        $"PlaceContext.{other}.Infrastructure.csproj",
                        $"PlaceContext.{other}.Api.csproj",
                    };
                    foreach (var reference in forbidden)
                    {
                        if (text.Contains(reference, StringComparison.OrdinalIgnoreCase))
                            failures.Add($"{Relative(project)} references {reference}");
                    }
                }
            }
        }

        Assert.Empty(failures);
    }

    [Fact]
    public void Every_service_has_owned_layers_controller_runtime_and_tests()
    {
        var serviceNames = new[] { "AgentChat", "Agents", "Artifacts", "Crm", "Data", "Jobs", "Search", "Vault" };
        var missing = new List<string>();

        foreach (var service in serviceNames)
        {
            var serviceDirectory = Path.Combine(Root, "src", $"PlaceContext.{service}");
            var required = new[]
            {
                Path.Combine(serviceDirectory, $"PlaceContext.{service}.csproj"),
                Path.Combine(serviceDirectory, $"PlaceContext.{service}.Contracts.csproj"),
                Path.Combine(serviceDirectory, $"PlaceContext.{service}.Domain.csproj"),
                Path.Combine(serviceDirectory, $"PlaceContext.{service}.Infrastructure.csproj"),
                Path.Combine(serviceDirectory, $"PlaceContext.{service}.Api.csproj"),
                Path.Combine(serviceDirectory, "Controllers", $"{service}Controller.cs"),
                Path.Combine(serviceDirectory, "Runtime", "Program.cs"),
                Path.Combine(Root, "tests", $"PlaceContext.{service}.Tests", $"PlaceContext.{service}.Tests.csproj"),
            };

            missing.AddRange(required.Where(path => !File.Exists(path)).Select(Relative));
        }

        Assert.Empty(missing);
    }

    [Fact]
    public void Vault_persistence_is_owned_by_vault_infrastructure()
    {
        var vaultDirectory = Path.Combine(Root, "src", "PlaceContext.Vault");
        var infrastructureProject = Path.Combine(
            vaultDirectory,
            "PlaceContext.Vault.Infrastructure.csproj");
        var apiProject = Path.Combine(vaultDirectory, "PlaceContext.Vault.Api.csproj");
        var sharedContext = Path.Combine(
            Root,
            "src",
            "PlaceContext.Infrastructure",
            "Persistence",
            "AppDbContext.cs");
        var required = new[]
        {
            Path.Combine(vaultDirectory, "Infrastructure", "Persistence", "VaultDbContext.cs"),
            Path.Combine(vaultDirectory, "Infrastructure", "Persistence", "ProjectSecretRow.cs"),
            Path.Combine(vaultDirectory, "Domain", "Persistence", "IVaultUnitOfWork.cs"),
            Path.Combine(
                vaultDirectory,
                "Infrastructure",
                "Persistence",
                "Migrations",
                "VaultDbContextModelSnapshot.cs"),
        };

        Assert.All(required, path => Assert.True(File.Exists(path), $"Missing {Relative(path)}"));
        Assert.DoesNotContain(
            "PlaceContext.Infrastructure.csproj",
            File.ReadAllText(infrastructureProject),
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "PlaceContext.Infrastructure.csproj",
            File.ReadAllText(apiProject),
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("JobSecretRow", File.ReadAllText(sharedContext), StringComparison.Ordinal);
        Assert.DoesNotContain("job_secrets", File.ReadAllText(sharedContext), StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(
            Root,
            "src",
            "PlaceContext.Infrastructure",
            "Persistence",
            "JobSecretRow.cs")));
    }

    [Fact]
    public void AgentChat_persistence_is_owned_by_agent_chat_infrastructure()
    {
        var serviceDirectory = Path.Combine(Root, "src", "PlaceContext.AgentChat");
        var infrastructureProject = Path.Combine(
            serviceDirectory,
            "PlaceContext.AgentChat.Infrastructure.csproj");
        var apiProject = Path.Combine(serviceDirectory, "PlaceContext.AgentChat.Api.csproj");
        var sharedPersistence = Path.Combine(
            Root,
            "src",
            "PlaceContext.Infrastructure",
            "Persistence");
        var sharedContextText = File.ReadAllText(Path.Combine(sharedPersistence, "AppDbContext.cs"));
        var required = new[]
        {
            Path.Combine(serviceDirectory, "Infrastructure", "Persistence", "AgentChatDbContext.cs"),
            Path.Combine(serviceDirectory, "Infrastructure", "Persistence", "AgentConfigRow.cs"),
            Path.Combine(serviceDirectory, "Infrastructure", "Persistence", "AgentChatSessionRow.cs"),
            Path.Combine(serviceDirectory, "Infrastructure", "Persistence", "McpConnectionRow.cs"),
            Path.Combine(serviceDirectory, "Infrastructure", "Persistence", "ChatCommandRow.cs"),
            Path.Combine(serviceDirectory, "Domain", "Persistence", "IAgentChatUnitOfWork.cs"),
            Path.Combine(
                serviceDirectory,
                "Infrastructure",
                "Persistence",
                "Migrations",
                "AgentChatDbContextModelSnapshot.cs"),
        };
        var releasedTypes = new[]
        {
            "AgentConfigRow",
            "AgentChatSessionRow",
            "McpConnectionRow",
            "ChatCommandRow",
        };

        Assert.All(required, path => Assert.True(File.Exists(path), $"Missing {Relative(path)}"));
        Assert.DoesNotContain(
            "PlaceContext.Infrastructure.csproj",
            File.ReadAllText(infrastructureProject),
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "PlaceContext.Infrastructure.csproj",
            File.ReadAllText(apiProject),
            StringComparison.OrdinalIgnoreCase);
        Assert.All(
            releasedTypes,
            typeName =>
            {
                Assert.DoesNotContain(typeName, sharedContextText, StringComparison.Ordinal);
                Assert.False(File.Exists(Path.Combine(sharedPersistence, typeName + ".cs")));
            });
    }

    [Fact]
    public void Artifacts_persistence_is_owned_by_artifacts_infrastructure()
    {
        var serviceDirectory = Path.Combine(Root, "src", "PlaceContext.Artifacts");
        var infrastructureProject = Path.Combine(
            serviceDirectory,
            "PlaceContext.Artifacts.Infrastructure.csproj");
        var apiProject = Path.Combine(serviceDirectory, "PlaceContext.Artifacts.Api.csproj");
        var sharedPersistence = Path.Combine(
            Root,
            "src",
            "PlaceContext.Infrastructure",
            "Persistence");
        var sharedContextText = File.ReadAllText(Path.Combine(sharedPersistence, "AppDbContext.cs"));
        var required = new[]
        {
            Path.Combine(serviceDirectory, "Infrastructure", "Persistence", "ArtifactsDbContext.cs"),
            Path.Combine(serviceDirectory, "Infrastructure", "Persistence", "RunArtifactLinkRow.cs"),
            Path.Combine(serviceDirectory, "Infrastructure", "Persistence", "ArtifactShareTokenRow.cs"),
            Path.Combine(serviceDirectory, "Domain", "Persistence", "IArtifactsUnitOfWork.cs"),
            Path.Combine(
                serviceDirectory,
                "Infrastructure",
                "Persistence",
                "Migrations",
                "ArtifactsDbContextModelSnapshot.cs"),
        };
        var releasedTypes = new[]
        {
            "RunArtifactLinkRow",
            "ArtifactShareTokenRow",
        };

        Assert.All(required, path => Assert.True(File.Exists(path), $"Missing {Relative(path)}"));
        Assert.DoesNotContain(
            "PlaceContext.Infrastructure.csproj",
            File.ReadAllText(infrastructureProject),
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "PlaceContext.Infrastructure.csproj",
            File.ReadAllText(apiProject),
            StringComparison.OrdinalIgnoreCase);
        Assert.All(
            releasedTypes,
            typeName =>
            {
                Assert.DoesNotContain(typeName, sharedContextText, StringComparison.Ordinal);
                Assert.False(File.Exists(Path.Combine(sharedPersistence, typeName + ".cs")));
            });
    }

    [Fact]
    public void Search_persistence_is_owned_by_search_infrastructure()
    {
        var serviceDirectory = Path.Combine(Root, "src", "PlaceContext.Search");
        var infrastructureProject = Path.Combine(
            serviceDirectory,
            "PlaceContext.Search.Infrastructure.csproj");
        var apiProject = Path.Combine(serviceDirectory, "PlaceContext.Search.Api.csproj");
        var sharedPersistence = Path.Combine(
            Root,
            "src",
            "PlaceContext.Infrastructure",
            "Persistence");
        var sharedContextText = File.ReadAllText(Path.Combine(sharedPersistence, "AppDbContext.cs"));
        var required = new[]
        {
            Path.Combine(serviceDirectory, "Infrastructure", "Persistence", "SearchDbContext.cs"),
            Path.Combine(serviceDirectory, "Infrastructure", "Persistence", "OpenSearchDashboardRow.cs"),
            Path.Combine(serviceDirectory, "Domain", "Persistence", "ISearchUnitOfWork.cs"),
            Path.Combine(
                serviceDirectory,
                "Infrastructure",
                "Persistence",
                "Migrations",
                "SearchDbContextModelSnapshot.cs"),
        };

        Assert.All(required, path => Assert.True(File.Exists(path), $"Missing {Relative(path)}"));
        Assert.DoesNotContain(
            "PlaceContext.Infrastructure.csproj",
            File.ReadAllText(infrastructureProject),
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "PlaceContext.Infrastructure.csproj",
            File.ReadAllText(apiProject),
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("OpenSearchDashboardRow", sharedContextText, StringComparison.Ordinal);
        Assert.DoesNotContain("opensearch_dashboards", sharedContextText, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(sharedPersistence, "OpenSearchDashboardRow.cs")));
    }

    [Fact]
    public void Data_persistence_is_owned_by_data_infrastructure()
    {
        var serviceDirectory = Path.Combine(Root, "src", "PlaceContext.Data");
        var infrastructureProject = Path.Combine(serviceDirectory, "PlaceContext.Data.Infrastructure.csproj");
        var apiProject = Path.Combine(serviceDirectory, "PlaceContext.Data.Api.csproj");
        var sharedPersistence = Path.Combine(Root, "src", "PlaceContext.Infrastructure", "Persistence");
        var sharedContextText = File.ReadAllText(Path.Combine(sharedPersistence, "AppDbContext.cs"));
        var rowTypes = new[]
        {
            "ProjectChartRow",
            "DataMappingRow",
            "DataEntityRow",
            "EntityTagRow",
            "RecordLinkRow",
            "SavedQueryRow",
        };
        var required = new[]
        {
            Path.Combine(serviceDirectory, "Infrastructure", "Persistence", "DataDbContext.cs"),
            Path.Combine(serviceDirectory, "Domain", "Persistence", "IDataUnitOfWork.cs"),
            Path.Combine(
                serviceDirectory,
                "Infrastructure",
                "Persistence",
                "Migrations",
                "DataDbContextModelSnapshot.cs"),
        }.Concat(rowTypes.Select(typeName =>
            Path.Combine(serviceDirectory, "Infrastructure", "Persistence", typeName + ".cs")));

        Assert.All(required, path => Assert.True(File.Exists(path), $"Missing {Relative(path)}"));
        Assert.DoesNotContain(
            "PlaceContext.Infrastructure.csproj",
            File.ReadAllText(infrastructureProject),
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "PlaceContext.Infrastructure.csproj",
            File.ReadAllText(apiProject),
            StringComparison.OrdinalIgnoreCase);
        Assert.All(
            rowTypes,
            typeName =>
            {
                Assert.DoesNotContain(typeName, sharedContextText, StringComparison.Ordinal);
                Assert.False(File.Exists(Path.Combine(sharedPersistence, typeName + ".cs")));
            });
    }

    [Fact]
    public void Jobs_persistence_is_owned_by_jobs_infrastructure()
    {
        var serviceDirectory = Path.Combine(Root, "src", "PlaceContext.Jobs");
        var infrastructureProject = Path.Combine(
            serviceDirectory,
            "PlaceContext.Jobs.Infrastructure.csproj");
        var sharedPersistence = Path.Combine(Root, "src", "PlaceContext.Infrastructure", "Persistence");
        var sharedContextText = File.ReadAllText(Path.Combine(sharedPersistence, "AppDbContext.cs"));
        var rowTypes = new[]
        {
            "JobRow",
            "JobRunRow",
            "JobTestCaseRow",
            "JobTriggerRow",
            "JobChainRow",
            "ChainRunRow",
            "EventDefinitionRow",
            "EventOccurrenceRow",
            "PendingRunRow",
        };
        var required = new[]
        {
            Path.Combine(serviceDirectory, "Infrastructure", "Persistence", "JobsDbContext.cs"),
            Path.Combine(serviceDirectory, "Domain", "Persistence", "IJobsUnitOfWork.cs"),
            Path.Combine(
                serviceDirectory,
                "Infrastructure",
                "Persistence",
                "Migrations",
                "JobsDbContextModelSnapshot.cs"),
        }.Concat(rowTypes.Select(typeName =>
            Path.Combine(serviceDirectory, "Infrastructure", "Persistence", typeName + ".cs")));

        Assert.All(required, path => Assert.True(File.Exists(path), $"Missing {Relative(path)}"));
        Assert.DoesNotContain(
            "PlaceContext.Infrastructure.csproj",
            File.ReadAllText(infrastructureProject),
            StringComparison.OrdinalIgnoreCase);
        Assert.All(
            rowTypes,
            typeName =>
            {
                Assert.DoesNotContain($"DbSet<{typeName}>", sharedContextText, StringComparison.Ordinal);
                Assert.False(File.Exists(Path.Combine(sharedPersistence, typeName + ".cs")));
            });
    }

    [Fact]
    public void Crm_persistence_is_owned_by_crm_infrastructure()
    {
        var serviceDirectory = Path.Combine(Root, "src", "PlaceContext.Crm");
        var infrastructureProject = Path.Combine(
            serviceDirectory,
            "PlaceContext.Crm.Infrastructure.csproj");
        var sharedPersistence = Path.Combine(Root, "src", "PlaceContext.Infrastructure", "Persistence");
        var sharedContextText = File.ReadAllText(Path.Combine(sharedPersistence, "AppDbContext.cs"));
        var rowTypes = new[]
        {
            "CrmClientRow",
            "CrmJobRunRow",
            "CrmChainRunRow",
            "CrmCommunicationRow",
            "CrmAppointmentRow",
            "CrmCalendarRow",
            "CrmClientArtifactRow",
            "CrmClientJobChainAssignmentRow",
            "CrmAutomationRuleRow",
            "CrmAutomationQueueRow",
            "CrmIngestionSettingsRow",
        };
        var required = new[]
        {
            Path.Combine(serviceDirectory, "Infrastructure", "Persistence", "CrmDbContext.cs"),
            Path.Combine(serviceDirectory, "Domain", "Persistence", "ICrmUnitOfWork.cs"),
            Path.Combine(
                serviceDirectory,
                "Infrastructure",
                "Persistence",
                "Migrations",
                "CrmDbContextModelSnapshot.cs"),
        }.Concat(rowTypes.Select(typeName =>
            Path.Combine(serviceDirectory, "Infrastructure", "Persistence", typeName + ".cs")));

        Assert.All(required, path => Assert.True(File.Exists(path), $"Missing {Relative(path)}"));
        Assert.DoesNotContain(
            "PlaceContext.Infrastructure.csproj",
            File.ReadAllText(infrastructureProject),
            StringComparison.OrdinalIgnoreCase);
        Assert.All(
            rowTypes,
            typeName =>
            {
                Assert.DoesNotContain($"DbSet<{typeName}>", sharedContextText, StringComparison.Ordinal);
                Assert.False(File.Exists(Path.Combine(sharedPersistence, typeName + ".cs")));
            });
    }

    private static IEnumerable<string> SourceFiles()
        => Directory.EnumerateFiles(Path.Combine(Root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(file => !HasPathPart(file, "bin") && !HasPathPart(file, "obj"))
            .Where(file => !file.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<MemberDeclarationSyntax> TopLevelTypes(string file)
    {
        var root = CSharpSyntaxTree.ParseText(File.ReadAllText(file)).GetCompilationUnitRoot();
        return Members(root.Members).Where(member => member is BaseTypeDeclarationSyntax or DelegateDeclarationSyntax);
    }

    private static IEnumerable<MemberDeclarationSyntax> Members(SyntaxList<MemberDeclarationSyntax> members)
    {
        foreach (var member in members)
        {
            if (member is BaseNamespaceDeclarationSyntax namespaceDeclaration)
            {
                foreach (var nested in Members(namespaceDeclaration.Members))
                    yield return nested;
            }
            else
            {
                yield return member;
            }
        }
    }

    private static string TypeName(MemberDeclarationSyntax declaration) => declaration switch
    {
        BaseTypeDeclarationSyntax type => type.Identifier.ValueText,
        DelegateDeclarationSyntax type => type.Identifier.ValueText,
        _ => throw new ArgumentOutOfRangeException(nameof(declaration)),
    };

    private static bool HasPathPart(string path, string part)
        => path.Split(Path.DirectorySeparatorChar).Contains(part, StringComparer.OrdinalIgnoreCase);

    private static string Relative(string path) => Path.GetRelativePath(Root, path);

    private static string Root { get; } = FindRoot();

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PlaceContext.slnx")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not find the PlaceContext repository root.");
    }
}
