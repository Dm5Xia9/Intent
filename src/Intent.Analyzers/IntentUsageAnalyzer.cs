using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Intents.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class IntentUsageAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(
            IntentDiagnostics.DiscardWithoutBackground,
            IntentDiagnostics.ConfigureAfterAwait,
            IntentDiagnostics.IntentNotAwaited);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterOperationAction(AnalyzeExpressionStatement, OperationKind.ExpressionStatement);
        context.RegisterOperationAction(AnalyzeSimpleAssignment, OperationKind.SimpleAssignment);
        context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
    }

    private static void AnalyzeSimpleAssignment(OperationAnalysisContext context)
    {
        if (context.Operation is not ISimpleAssignmentOperation assignment)
            return;

        if (assignment.Target is not IDiscardOperation)
            return;

        if (!IsIntentType(assignment.Value.Type))
            return;

        if (IsFireAndForgetInvocation(assignment.Value))
            return;

        context.ReportDiagnostic(Diagnostic.Create(
            IntentDiagnostics.DiscardWithoutBackground,
            assignment.Syntax.GetLocation()));
    }

    private static void AnalyzeExpressionStatement(OperationAnalysisContext context)
    {
        if (context.Operation is not IExpressionStatementOperation exprStmt)
            return;

        var value = exprStmt.Operation;
        // Assignment/await statements are not "unused Intent" (Type of assignment is the RHS type).
        if (value is IAwaitOperation or IAssignmentOperation)
            return;

        if (IsFireAndForgetInvocation(value))
            return;

        if (IsIntentType(value.Type))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                IntentDiagnostics.IntentNotAwaited,
                value.Syntax.GetLocation()));
        }
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context)
    {
        if (context.Operation is not IInvocationOperation invocation)
            return;

        if (!IsConfigureLike(invocation))
            return;

        var receiver = invocation.Instance ?? GetExtensionReceiver(invocation);
        if (receiver is not ILocalReferenceOperation localRef)
            return;

        if (WasAwaitedEarlier(invocation, localRef.Local))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                IntentDiagnostics.ConfigureAfterAwait,
                invocation.Syntax.GetLocation(),
                localRef.Local.Name));
        }
    }

    private static IOperation? GetExtensionReceiver(IInvocationOperation invocation)
    {
        if (invocation.TargetMethod.IsExtensionMethod && invocation.Arguments.Length > 0)
            return invocation.Arguments[0].Value;
        return null;
    }

    private static bool IsConfigureLike(IInvocationOperation invocation)
    {
        var name = invocation.TargetMethod.Name;
        if (name is "Configure" or "WithPolicies")
            return IsIntentConfigureTarget(invocation);

        if (!name.StartsWith("With", StringComparison.Ordinal))
            return false;

        return IsIntentConfigureTarget(invocation);
    }

    private static bool IsIntentConfigureTarget(IInvocationOperation invocation)
    {
        if (IsIntentType(invocation.Instance?.Type))
            return true;

        if (invocation.TargetMethod.IsExtensionMethod &&
            invocation.Arguments.Length > 0 &&
            IsIntentType(invocation.Arguments[0].Value.Type))
            return true;

        var containing = invocation.TargetMethod.ContainingType?.Name;
        return containing is "IntentWithExtensions" or "IntentPollyWithExtensions";
    }

    private static bool WasAwaitedEarlier(IInvocationOperation configureCall, ILocalSymbol local)
    {
        var block = configureCall.Syntax.Ancestors().OfType<BlockSyntax>().FirstOrDefault();
        if (block is null)
            return false;

        var configureSpan = configureCall.Syntax.SpanStart;
        foreach (var awaitExpr in block.DescendantNodes().OfType<AwaitExpressionSyntax>())
        {
            if (awaitExpr.SpanStart >= configureSpan)
                continue;

            if (awaitExpr.Expression is IdentifierNameSyntax id &&
                id.Identifier.ValueText == local.Name)
                return true;
        }

        return false;
    }

    private static bool IsFireAndForgetInvocation(IOperation operation)
    {
        if (operation is not IInvocationOperation inv)
            return false;

        var name = inv.TargetMethod.Name;
        if (name is not ("Background" or "Into" or "FromEach"))
            return false;

        return inv.TargetMethod.ContainingType?.Name is "Intent" or "IntentExtensions";
    }

    private static bool IsIntentType(ITypeSymbol? type)
    {
        if (type is null)
            return false;

        if (type.ContainingNamespace?.ToDisplayString() != "Intents")
            return false;

        if (type.Name == "Intent" && type is INamedTypeSymbol { IsGenericType: false })
            return true;

        return type is INamedTypeSymbol { IsGenericType: true, Name: "Intent", TypeArguments.Length: 1 };
    }
}
