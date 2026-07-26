using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace Intents.Analyzers.Tests;

public class IntentUsageAnalyzerTests
{
    private static MetadataReference IntentReference { get; } =
        MetadataReference.CreateFromFile(typeof(Intent).Assembly.Location);

    [Fact]
    public async Task Discard_without_Background_warns()
    {
        const string source = """
            using Intents;
            class C {
              void M() {
                {|#0:_ = Intent.From(() => { })|};
              }
            }
            """;

        await new CSharpAnalyzerTest<IntentUsageAnalyzer, DefaultVerifier>
        {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestState =
            {
                Sources = { source },
                AdditionalReferences = { IntentReference },
                ExpectedDiagnostics =
                {
                    DiagnosticResult.CompilerWarning("INT0001").WithLocation(0)
                }
            }
        }.RunAsync();
    }

    [Fact]
    public async Task Unawaited_expression_warns()
    {
        const string source = """
            using Intents;
            class C {
              void M() {
                {|#0:Intent.From(() => { })|};
              }
            }
            """;

        await new CSharpAnalyzerTest<IntentUsageAnalyzer, DefaultVerifier>
        {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestState =
            {
                Sources = { source },
                AdditionalReferences = { IntentReference },
                ExpectedDiagnostics =
                {
                    DiagnosticResult.CompilerWarning("INT0003").WithLocation(0)
                }
            }
        }.RunAsync();
    }

    [Fact]
    public async Task Configure_after_await_warns()
    {
        const string source = """
            using System.Threading.Tasks;
            using Intents;
            class C {
              async Task M() {
                var plan = Intent.From(() => { });
                await plan;
                {|#0:plan.WithNamed("x")|};
              }
            }
            """;

        await new CSharpAnalyzerTest<IntentUsageAnalyzer, DefaultVerifier>
        {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestState =
            {
                Sources = { source },
                AdditionalReferences = { IntentReference },
                ExpectedDiagnostics =
                {
                    DiagnosticResult.CompilerWarning("INT0002").WithLocation(0).WithArguments("plan"),
                    DiagnosticResult.CompilerWarning("INT0003").WithLocation(0)
                }
            }
        }.RunAsync();
    }

    [Fact]
    public async Task Background_does_not_warn()
    {
        const string source = """
            using Intents;
            class C {
              void M() {
                Intent.From(() => { }).Background();
              }
            }
            """;

        await new CSharpAnalyzerTest<IntentUsageAnalyzer, DefaultVerifier>
        {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestState =
            {
                Sources = { source },
                AdditionalReferences = { IntentReference }
            }
        }.RunAsync();
    }

    [Fact]
    public async Task Into_does_not_warn()
    {
        const string source = """
            using System.Threading.Channels;
            using Intents;
            class C {
              void M(ChannelWriter<int> w) {
                Intent.From(() => 1).Into(w);
              }
            }
            """;

        await new CSharpAnalyzerTest<IntentUsageAnalyzer, DefaultVerifier>
        {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestState =
            {
                Sources = { source },
                AdditionalReferences = { IntentReference }
            }
        }.RunAsync();
    }
}
