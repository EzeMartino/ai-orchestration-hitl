using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orchestration.Application.Agents.Planner.ToolCalling;
using Orchestration.Infrastructure.Agents.Planner.ToolCalling;

namespace Orchestration.Tests.Agents.Planner.ToolCalling;

public class ToolPlanProposalRegistrationTests
{
    [Fact]
    public void Program_ScopedToolCallingRegistrations_UseOptionsSnapshot()
    {
        var source = File.ReadAllText(FindApiProgramPath());

        source.Should().NotContain("IOptions<ToolCallingOptions>");
        source.Split(
                "IOptionsSnapshot<ToolCallingOptions>",
                StringSplitOptions.None)
            .Should().HaveCount(3);
    }

    [Fact]
    public void AddToolPlanProposal_Should_use_deterministic_service_when_tool_calling_is_disabled()
    {
        var services = new ServiceCollection();

        services.AddToolPlanProposal(CreateConfiguration(
            toolCallingEnabled: false,
            llmEnabled: true
        ));

        using var provider = services.BuildServiceProvider();
        var service = provider.GetRequiredService<IToolPlanProposalService>();

        service.Should().BeOfType<DeterministicToolPlanProposalService>();
    }

    [Fact]
    public void AddToolPlanProposal_Should_use_deterministic_service_when_llm_is_disabled()
    {
        var services = new ServiceCollection();

        services.AddToolPlanProposal(CreateConfiguration(
            toolCallingEnabled: true,
            llmEnabled: false
        ));

        using var provider = services.BuildServiceProvider();
        var service = provider.GetRequiredService<IToolPlanProposalService>();

        service.Should().BeOfType<DeterministicToolPlanProposalService>();
    }

    [Fact]
    public void AddToolPlanProposal_Should_use_semantic_kernel_service_when_tool_calling_and_llm_are_enabled()
    {
        var services = new ServiceCollection();

        services.AddToolPlanProposal(CreateConfiguration(
            toolCallingEnabled: true,
            llmEnabled: true
        ));

        using var provider = services.BuildServiceProvider();
        var service = provider.GetRequiredService<IToolPlanProposalService>();

        service.Should().BeOfType<SemanticKernelToolPlanProposalService>();
    }

    [Fact]
    public void AddToolPlanProposal_PlanDrivenLegalToolWithDisabledMcp_Throws()
    {
        var services = new ServiceCollection();
        var configuration = CreateConfiguration(
            toolCallingEnabled: true,
            llmEnabled: true,
            executionMode: ToolCallingExecutionMode.PlanDriven,
            cnvRegulationEnabled: false,
            allowedTools: ["LEGAL.SEARCH_CNV_REGULATION"]);

        Action act = () => services.AddToolPlanProposal(configuration);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage(
                "Plan-driven legal tool execution requires Mcp:CnvRegulation:Enabled=true at application startup; restart is required after enabling it.");
    }

    [Fact]
    public void AddToolPlanProposal_PlanDrivenMissingAllowlistWithDisabledMcp_Throws()
    {
        var services = new ServiceCollection();
        var configuration = CreateConfiguration(
            toolCallingEnabled: true,
            llmEnabled: true,
            executionMode: ToolCallingExecutionMode.PlanDriven,
            cnvRegulationEnabled: false);

        Action act = () => services.AddToolPlanProposal(configuration);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage(
                "Plan-driven legal tool execution requires Mcp:CnvRegulation:Enabled=true at application startup; restart is required after enabling it.");
    }

    [Fact]
    public void AddToolPlanProposal_ExplicitDataOnlyAllowlist_IsEffectiveAtRuntime()
    {
        var services = new ServiceCollection();
        var configuration = CreateConfiguration(
            toolCallingEnabled: true,
            llmEnabled: true,
            executionMode: ToolCallingExecutionMode.PlanDriven,
            cnvRegulationEnabled: false,
            allowedTools: [PlannerToolCatalog.AnalyzeTransactionsName]);

        services.AddToolPlanProposal(configuration);

        using var provider = services.BuildServiceProvider();
        var options = provider
            .GetRequiredService<IOptions<ToolCallingOptions>>()
            .Value;
        options.AllowedTools.Should().Equal(
            PlannerToolCatalog.AnalyzeTransactionsName);
        PlannerToolCatalog.GetAllowed(options).Should().ContainSingle()
            .Which.Name.Should().Be(PlannerToolCatalog.AnalyzeTransactionsName);
    }

    [Fact]
    public void AddToolPlanProposal_HigherPrecedenceInMemoryDataOnly_ReplacesBaseJsonAllowlist()
    {
        var services = new ServiceCollection();
        var configuration = CreateLayeredJsonConfiguration(
            cnvRegulationEnabled: false,
            inMemoryOverlay: new Dictionary<string, string?>
            {
                ["ToolCalling:AllowedTools:0"] =
                    PlannerToolCatalog.AnalyzeTransactionsName
            });

        services.AddToolPlanProposal(configuration);

        using var provider = services.BuildServiceProvider();
        AssertRuntimeAllowedTools(
            provider,
            PlannerToolCatalog.AnalyzeTransactionsName);
    }

    [Fact]
    public void AddToolPlanProposal_HigherPrecedenceJsonEmpty_ReplacesBaseJsonAllowlist()
    {
        var services = new ServiceCollection();
        var configuration = CreateLayeredJsonConfiguration(
            cnvRegulationEnabled: false,
            jsonOverlay: """
                {
                  "ToolCalling": {
                    "AllowedTools": []
                  }
                }
                """);

        services.AddToolPlanProposal(configuration);

        using var provider = services.BuildServiceProvider();
        AssertRuntimeAllowedTools(provider);
    }

    [Fact]
    public void AddToolPlanProposal_HigherPrecedenceProvider_OrdersNumericIndicesAndDropsBlankValues()
    {
        var services = new ServiceCollection();
        var configuration = CreateLayeredJsonConfiguration(
            cnvRegulationEnabled: true,
            inMemoryOverlay: new Dictionary<string, string?>
            {
                ["ToolCalling:AllowedTools:10"] =
                    PlannerToolCatalog.AnalyzeTransactionsName,
                ["ToolCalling:AllowedTools:2"] =
                    PlannerToolCatalog.SearchCnvRegulationName,
                ["ToolCalling:AllowedTools:1"] = " "
            });

        services.AddToolPlanProposal(configuration);

        using var provider = services.BuildServiceProvider();
        var expected = new[]
        {
            PlannerToolCatalog.SearchCnvRegulationName,
            PlannerToolCatalog.AnalyzeTransactionsName
        };
        provider.GetRequiredService<IOptions<ToolCallingOptions>>()
            .Value.AllowedTools.Should().Equal(expected);
        provider.GetRequiredService<IOptionsMonitor<ToolCallingOptions>>()
            .CurrentValue.AllowedTools.Should().Equal(expected);
        using var scope = provider.CreateScope();
        scope.ServiceProvider
            .GetRequiredService<IOptionsSnapshot<ToolCallingOptions>>()
            .Value.AllowedTools.Should().Equal(expected);
    }

    [Fact]
    public void AddToolPlanProposal_BaseJsonWithoutOverlay_PreservesAllowlist()
    {
        var services = new ServiceCollection();
        var configuration = CreateLayeredJsonConfiguration(
            cnvRegulationEnabled: true);

        services.AddToolPlanProposal(configuration);

        using var provider = services.BuildServiceProvider();
        AssertRuntimeAllowedTools(
            provider,
            PlannerToolCatalog.AnalyzeTransactionsName,
            PlannerToolCatalog.SearchCnvRegulationName);
    }

    [Fact]
    public void AddToolPlanProposal_BaseJsonLegalWithDisabledMcp_Throws()
    {
        var services = new ServiceCollection();
        var configuration = CreateLayeredJsonConfiguration(
            cnvRegulationEnabled: false);

        Action act = () => services.AddToolPlanProposal(configuration);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage(
                "Plan-driven legal tool execution requires Mcp:CnvRegulation:Enabled=true at application startup; restart is required after enabling it.");
    }

    [Fact]
    public async Task AddToolPlanProposal_ReloadToDataOnly_RefreshesScopedConsumers()
    {
        var services = new ServiceCollection();
        var configuration = (ConfigurationManager)CreateConfiguration(
            toolCallingEnabled: true,
            llmEnabled: false,
            executionMode: ToolCallingExecutionMode.PlanDriven,
            cnvRegulationEnabled: true,
            allowedTools:
            [
                PlannerToolCatalog.AnalyzeTransactionsName,
                PlannerToolCatalog.SearchCnvRegulationName
            ]);
        services.AddToolPlanProposal(configuration);
        AddPlannerScopedOptionConsumers(services);

        using var provider = services.BuildServiceProvider();
        var monitor = provider
            .GetRequiredService<IOptionsMonitor<ToolCallingOptions>>();
        using var scope1 = provider.CreateScope();
        var scope1Snapshot = scope1.ServiceProvider
            .GetRequiredService<IOptionsSnapshot<ToolCallingOptions>>()
            .Value;
        var scope1Plan = await scope1.ServiceProvider
            .GetRequiredService<IToolPlanProposalService>()
            .ProposeAsync(CreateProposalInput(), CancellationToken.None);
        scope1Snapshot.AllowedTools.Should().Equal(
            PlannerToolCatalog.AnalyzeTransactionsName,
            PlannerToolCatalog.SearchCnvRegulationName);
        scope1Plan.ProposedCalls.Select(call => call.ToolName).Should().Equal(
            PlannerToolCatalog.AnalyzeTransactionsName,
            PlannerToolCatalog.SearchCnvRegulationName);

        ReloadConfiguration(
            configuration,
            ("ToolCalling:AllowedTools:1", null),
            ("Mcp:CnvRegulation:Enabled", false.ToString()));

        scope1Snapshot.AllowedTools.Should().HaveCount(2);
        monitor.CurrentValue.AllowedTools.Should().Equal(
            PlannerToolCatalog.AnalyzeTransactionsName);
        using var scope2 = provider.CreateScope();
        var scope2Options = scope2.ServiceProvider
            .GetRequiredService<ToolCallingOptions>();
        scope2Options.AllowedTools.Should().Equal(
            PlannerToolCatalog.AnalyzeTransactionsName);
        PlannerToolCatalog.GetAllowed(scope2Options).Should().ContainSingle()
            .Which.Name.Should().Be(PlannerToolCatalog.AnalyzeTransactionsName);

        var scope2Plan = await scope2.ServiceProvider
            .GetRequiredService<IToolPlanProposalService>()
            .ProposeAsync(CreateProposalInput(), CancellationToken.None);
        scope2Plan.ProposedCalls.Should().ContainSingle()
            .Which.ToolName.Should().Be(
                PlannerToolCatalog.AnalyzeTransactionsName);
        var validation = scope2.ServiceProvider
            .GetRequiredService<IToolPlanValidator>()
            .Validate(CreateLegalOnlyPlan());
        validation.IsValid.Should().BeFalse();
    }

    [Fact]
    public void AddToolPlanProposal_ReloadAddingLegalWithDisabledMcp_FailsValidation()
    {
        var services = new ServiceCollection();
        var configuration = (ConfigurationManager)CreateConfiguration(
            toolCallingEnabled: true,
            llmEnabled: false,
            executionMode: ToolCallingExecutionMode.PlanDriven,
            cnvRegulationEnabled: false,
            allowedTools: [PlannerToolCatalog.AnalyzeTransactionsName]);
        services.AddToolPlanProposal(configuration);

        using var provider = services.BuildServiceProvider();
        ReloadConfiguration(
            configuration,
            ("ToolCalling:AllowedTools:1",
                PlannerToolCatalog.SearchCnvRegulationName));

        Action monitorAct = () => _ = provider
            .GetRequiredService<IOptionsMonitor<ToolCallingOptions>>()
            .CurrentValue;
        var monitorException = monitorAct.Should()
            .Throw<OptionsValidationException>().Which;
        monitorException.Failures.Should().Contain(
            "Plan-driven legal tool execution requires Mcp:CnvRegulation:Enabled=true at application startup; restart is required after enabling it.");

        using var scope2 = provider.CreateScope();
        Action snapshotAct = () => _ = scope2.ServiceProvider
            .GetRequiredService<IOptionsSnapshot<ToolCallingOptions>>()
            .Value;
        snapshotAct.Should().Throw<OptionsValidationException>()
            .Which.Failures.Should().Contain(
                "Plan-driven legal tool execution requires Mcp:CnvRegulation:Enabled=true at application startup; restart is required after enabling it.");
    }

    [Fact]
    public void AddToolPlanProposal_ReloadAddingLegalAndEnablingMcp_RequiresRestart()
    {
        var services = new ServiceCollection();
        var configuration = (ConfigurationManager)CreateConfiguration(
            toolCallingEnabled: true,
            llmEnabled: false,
            executionMode: ToolCallingExecutionMode.PlanDriven,
            cnvRegulationEnabled: false,
            allowedTools: [PlannerToolCatalog.AnalyzeTransactionsName]);
        services.AddToolPlanProposal(configuration);

        using var provider = services.BuildServiceProvider();
        ReloadConfiguration(
            configuration,
            ("ToolCalling:AllowedTools:1",
                PlannerToolCatalog.SearchCnvRegulationName),
            ("Mcp:CnvRegulation:Enabled", true.ToString()));

        using var scope2 = provider.CreateScope();
        Action snapshotAct = () => _ = scope2.ServiceProvider
            .GetRequiredService<IOptionsSnapshot<ToolCallingOptions>>()
            .Value;
        snapshotAct.Should().Throw<OptionsValidationException>()
            .Which.Failures.Should().Contain(
                "Plan-driven legal tool execution requires Mcp:CnvRegulation:Enabled=true at application startup; restart is required after enabling it.");
    }

    [Fact]
    public async Task AddToolPlanProposal_ReloadAddingLegal_WhenMcpEnabledAtStartup_RefreshesScopedProposal()
    {
        var services = new ServiceCollection();
        var configuration = (ConfigurationManager)CreateConfiguration(
            toolCallingEnabled: true,
            llmEnabled: false,
            executionMode: ToolCallingExecutionMode.PlanDriven,
            cnvRegulationEnabled: true,
            allowedTools: [PlannerToolCatalog.AnalyzeTransactionsName]);
        services.AddToolPlanProposal(configuration);

        using var provider = services.BuildServiceProvider();
        ReloadConfiguration(
            configuration,
            ("ToolCalling:AllowedTools:1",
                PlannerToolCatalog.SearchCnvRegulationName));

        using var scope2 = provider.CreateScope();
        var scope2Snapshot = scope2.ServiceProvider
            .GetRequiredService<IOptionsSnapshot<ToolCallingOptions>>()
            .Value;
        scope2Snapshot.AllowedTools.Should().Equal(
            PlannerToolCatalog.AnalyzeTransactionsName,
            PlannerToolCatalog.SearchCnvRegulationName);
        var plan = await scope2.ServiceProvider
            .GetRequiredService<IToolPlanProposalService>()
            .ProposeAsync(CreateProposalInput(), CancellationToken.None);
        plan.ProposedCalls.Select(call => call.ToolName).Should().Equal(
            PlannerToolCatalog.AnalyzeTransactionsName,
            PlannerToolCatalog.SearchCnvRegulationName);
    }

    [Fact]
    public async Task AddToolPlanProposal_ReloadDisablingMcp_WhenEnabledAtStartup_KeepsRegisteredCapability()
    {
        var services = new ServiceCollection();
        var configuration = (ConfigurationManager)CreateConfiguration(
            toolCallingEnabled: true,
            llmEnabled: false,
            executionMode: ToolCallingExecutionMode.PlanDriven,
            cnvRegulationEnabled: true,
            allowedTools: [PlannerToolCatalog.AnalyzeTransactionsName]);
        services.AddToolPlanProposal(configuration);

        using var provider = services.BuildServiceProvider();
        ReloadConfiguration(
            configuration,
            ("ToolCalling:AllowedTools:1",
                PlannerToolCatalog.SearchCnvRegulationName),
            ("Mcp:CnvRegulation:Enabled", false.ToString()));

        using var scope2 = provider.CreateScope();
        var snapshot = scope2.ServiceProvider
            .GetRequiredService<IOptionsSnapshot<ToolCallingOptions>>()
            .Value;
        snapshot.AllowedTools.Should().Contain(
            PlannerToolCatalog.SearchCnvRegulationName);
        var plan = await scope2.ServiceProvider
            .GetRequiredService<IToolPlanProposalService>()
            .ProposeAsync(CreateProposalInput(), CancellationToken.None);
        plan.ProposedCalls.Select(call => call.ToolName).Should().Contain(
            PlannerToolCatalog.SearchCnvRegulationName);
    }

    [Fact]
    public void AddToolPlanProposal_ExplicitEmptyInMemoryAllowlist_IsEffectiveAtRuntime()
    {
        var services = new ServiceCollection();
        var configuration = CreateConfiguration(
            toolCallingEnabled: true,
            llmEnabled: false,
            executionMode: ToolCallingExecutionMode.PlanDriven,
            cnvRegulationEnabled: false,
            allowedTools: []);

        services.AddToolPlanProposal(configuration);

        using var provider = services.BuildServiceProvider();
        var options = provider
            .GetRequiredService<IOptions<ToolCallingOptions>>()
            .Value;
        options.AllowedTools.Should().BeEmpty();
        PlannerToolCatalog.GetAllowed(options).Should().BeEmpty();
    }

    [Fact]
    public void AddToolPlanProposal_ExplicitEmptyJsonAllowlist_IsEffectiveAtRuntime()
    {
        var services = new ServiceCollection();
        var configuration = CreateJsonConfigurationWithEmptyAllowlist();

        services.AddToolPlanProposal(configuration);

        using var provider = services.BuildServiceProvider();
        var options = provider
            .GetRequiredService<IOptions<ToolCallingOptions>>()
            .Value;
        options.AllowedTools.Should().BeEmpty();
        PlannerToolCatalog.GetAllowed(options).Should().BeEmpty();
    }

    [Fact]
    public void AddToolPlanProposal_MissingAllowlist_PreservesRuntimeDefaults()
    {
        var services = new ServiceCollection();
        var configuration = CreateConfiguration(
            toolCallingEnabled: true,
            llmEnabled: true,
            executionMode: ToolCallingExecutionMode.PlanDriven,
            cnvRegulationEnabled: true);

        services.AddToolPlanProposal(configuration);

        using var provider = services.BuildServiceProvider();
        var options = provider
            .GetRequiredService<IOptions<ToolCallingOptions>>()
            .Value;
        options.AllowedTools.Should().Equal(
            PlannerToolCatalog.AnalyzeTransactionsName,
            PlannerToolCatalog.SearchCnvRegulationName);
        PlannerToolCatalog.GetAllowed(options).Select(tool => tool.Name)
            .Should().Equal(
                PlannerToolCatalog.AnalyzeTransactionsName,
                PlannerToolCatalog.SearchCnvRegulationName);
    }

    [Theory]
    [InlineData(
        false,
        ToolCallingExecutionMode.PlanDriven,
        false,
        "legal.search_cnv_regulation")]
    [InlineData(
        true,
        ToolCallingExecutionMode.Shadow,
        false,
        "legal.search_cnv_regulation")]
    [InlineData(
        true,
        ToolCallingExecutionMode.PlanDriven,
        false,
        "data.analyze_transactions")]
    [InlineData(
        true,
        ToolCallingExecutionMode.PlanDriven,
        true,
        "legal.search_cnv_regulation")]
    public void AddToolPlanProposal_SupportedExecutionConfiguration_Registers(
        bool toolCallingEnabled,
        ToolCallingExecutionMode executionMode,
        bool cnvRegulationEnabled,
        string allowedTool)
    {
        var services = new ServiceCollection();
        var configuration = CreateConfiguration(
            toolCallingEnabled,
            llmEnabled: true,
            executionMode,
            cnvRegulationEnabled,
            [allowedTool]);

        services.AddToolPlanProposal(configuration);

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IToolPlanProposalService>()
            .Should().NotBeNull();
    }

    private static IConfiguration CreateConfiguration(
        bool toolCallingEnabled,
        bool llmEnabled,
        ToolCallingExecutionMode executionMode = ToolCallingExecutionMode.Shadow,
        bool cnvRegulationEnabled = false,
        string[]? allowedTools = null)
    {
        var configuration = new ConfigurationManager
        {
            ["ToolCalling:Enabled"] = toolCallingEnabled.ToString(),
            ["ToolCalling:ExecutionMode"] = executionMode.ToString(),
            ["Mcp:CnvRegulation:Enabled"] = cnvRegulationEnabled.ToString(),
            ["Llm:Enabled"] = llmEnabled.ToString(),
            ["Llm:Provider"] = "OpenAI",
            ["Llm:Model"] = "test-model",
            ["Llm:ApiKey"] = "test-api-key",
            ["Llm:ServiceId"] = "planner-reasoning"
        };

        if (allowedTools is not null)
        {
            if (allowedTools.Length == 0)
            {
                configuration["ToolCalling:AllowedTools"] = string.Empty;
            }

            for (var index = 0; index < allowedTools.Length; index++)
            {
                configuration[$"ToolCalling:AllowedTools:{index}"] =
                    allowedTools[index];
            }
        }

        return configuration;
    }

    private static IConfiguration CreateJsonConfigurationWithEmptyAllowlist()
    {
        const string json = """
            {
              "ToolCalling": {
                "Enabled": true,
                "ExecutionMode": "PlanDriven",
                "AllowedTools": []
              },
              "Mcp": {
                "CnvRegulation": {
                  "Enabled": false
                }
              },
              "Llm": {
                "Enabled": false
              }
            }
            """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        return new ConfigurationBuilder()
            .AddJsonStream(stream)
            .Build();
    }

    private static IConfiguration CreateLayeredJsonConfiguration(
        bool cnvRegulationEnabled,
        IReadOnlyDictionary<string, string?>? inMemoryOverlay = null,
        string? jsonOverlay = null)
    {
        var enabled = cnvRegulationEnabled.ToString().ToLowerInvariant();
        var baseJson = $$"""
            {
              "ToolCalling": {
                "Enabled": true,
                "ExecutionMode": "PlanDriven",
                "AllowedTools": [
                  "data.analyze_transactions",
                  "legal.search_cnv_regulation"
                ]
              },
              "Mcp": {
                "CnvRegulation": {
                  "Enabled": {{enabled}}
                }
              },
              "Llm": {
                "Enabled": false
              }
            }
            """;
        using var baseStream = new MemoryStream(
            Encoding.UTF8.GetBytes(baseJson));
        var builder = new ConfigurationBuilder()
            .AddJsonStream(baseStream);

        if (inMemoryOverlay is not null)
        {
            builder.AddInMemoryCollection(inMemoryOverlay);
        }

        if (jsonOverlay is not null)
        {
            using var overlayStream = new MemoryStream(
                Encoding.UTF8.GetBytes(jsonOverlay));
            builder.AddJsonStream(overlayStream);
            return builder.Build();
        }

        return builder.Build();
    }

    private static void AssertRuntimeAllowedTools(
        ServiceProvider provider,
        params string[] expected)
    {
        var options = provider
            .GetRequiredService<IOptions<ToolCallingOptions>>()
            .Value;
        var monitor = provider
            .GetRequiredService<IOptionsMonitor<ToolCallingOptions>>()
            .CurrentValue;
        using var scope = provider.CreateScope();
        var snapshot = scope.ServiceProvider
            .GetRequiredService<IOptionsSnapshot<ToolCallingOptions>>()
            .Value;

        options.AllowedTools.Should().Equal(expected);
        snapshot.AllowedTools.Should().Equal(expected);
        monitor.AllowedTools.Should().Equal(expected);
        PlannerToolCatalog.GetAllowed(options).Select(tool => tool.Name)
            .Should().Equal(expected);
    }

    private static void AddPlannerScopedOptionConsumers(
        IServiceCollection services)
    {
        services.AddScoped<IToolPlanValidator>(provider =>
            new ToolPlanValidator(
                provider.GetRequiredService<
                    IOptionsSnapshot<ToolCallingOptions>>().Value));
        services.AddScoped(provider =>
            provider.GetRequiredService<
                IOptionsSnapshot<ToolCallingOptions>>().Value);
    }

    private static void ReloadConfiguration(
        ConfigurationManager configuration,
        params (string Key, string? Value)[] changes)
    {
        var root = (IConfigurationRoot)configuration;
        var mutableProvider = root.Providers.Last();
        foreach (var change in changes)
        {
            mutableProvider.Set(change.Key, change.Value);
        }

        root.Reload();
    }

    private static ToolPlanProposalInput CreateProposalInput() => new(
        Guid.NewGuid(),
        "reload-test-report",
        1000m,
        10,
        DateTimeOffset.UtcNow,
        "Planner summary.",
        [],
        []);

    private static ToolPlan CreateLegalOnlyPlan() => new(
    [
        new ProposedToolCall(
            PlannerToolCatalog.SearchCnvRegulationName,
            new Dictionary<string, string>(),
            "Legal review.")
    ]);

    private static string FindApiProgramPath()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var backendCandidate = Path.Combine(
                directory.FullName,
                "Orchestration.Api",
                "Program.cs");
            if (File.Exists(backendCandidate))
            {
                return backendCandidate;
            }

            var repositoryCandidate = Path.Combine(
                directory.FullName,
                "backend",
                "Orchestration.Api",
                "Program.cs");
            if (File.Exists(repositoryCandidate))
            {
                return repositoryCandidate;
            }
        }

        throw new FileNotFoundException("Could not locate Orchestration.Api/Program.cs.");
    }
}
