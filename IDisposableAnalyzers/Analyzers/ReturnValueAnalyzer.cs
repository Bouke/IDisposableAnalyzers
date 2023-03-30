﻿namespace IDisposableAnalyzers
{
    using System.Collections.Generic;
    using System.Collections.Immutable;
    using System.Linq;
    using System.Threading;

    using Gu.Roslyn.AnalyzerExtensions;
    using Gu.Roslyn.CodeFixExtensions;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;
    using Microsoft.CodeAnalysis.CSharp.Syntax;
    using Microsoft.CodeAnalysis.Diagnostics;

    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    internal class ReturnValueAnalyzer : DiagnosticAnalyzer
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(
            Descriptors.IDISP005ReturnTypeShouldBeIDisposable,
            Descriptors.IDISP011DontReturnDisposed,
            Descriptors.IDISP012PropertyShouldNotReturnCreated,
            Descriptors.IDISP013AwaitInUsing);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSyntaxNodeAction(c => HandleReturnValue(c), SyntaxKind.ReturnStatement);
            context.RegisterSyntaxNodeAction(c => HandleArrow(c), SyntaxKind.ArrowExpressionClause);
            context.RegisterSyntaxNodeAction(c => HandleLambda(c), SyntaxKind.ParenthesizedLambdaExpression);
            context.RegisterSyntaxNodeAction(c => HandleLambda(c), SyntaxKind.SimpleLambdaExpression);
        }

        private static void HandleReturnValue(SyntaxNodeAnalysisContext context)
        {
            if (!context.IsExcludedFromAnalysis() &&
                context.ContainingSymbol is { } &&
                !IsIgnored(context.ContainingSymbol) &&
                context.Node is ReturnStatementSyntax { Expression: { } expression })
            {
                HandleReturnValue(context, expression);
            }
        }

        private static void HandleArrow(SyntaxNodeAnalysisContext context)
        {
            if (!context.IsExcludedFromAnalysis() &&
                context.ContainingSymbol is { } &&
                !IsIgnored(context.ContainingSymbol) &&
                context.Node is ArrowExpressionClauseSyntax { Expression: { } expression })
            {
                HandleReturnValue(context, expression);
            }
        }

        private static void HandleLambda(SyntaxNodeAnalysisContext context)
        {
            if (!context.IsExcludedFromAnalysis() &&
                context.ContainingSymbol is { } &&
                !IsIgnored(context.ContainingSymbol) &&
                context.Node is LambdaExpressionSyntax { Body: ExpressionSyntax expression } lambda &&
                ShouldHandle())
            {
                HandleReturnValue(context, expression);
            }

            bool ShouldHandle()
            {
                return lambda switch
                {
                    { Parent: ArgumentSyntax } => Disposable.Ignores(lambda, context.SemanticModel, context.CancellationToken),
                    _ => true,
                };
            }
        }

        private static void HandleReturnValue(SyntaxNodeAnalysisContext context, ExpressionSyntax returnValue)
        {
            if (Disposable.IsCreation(returnValue, context.SemanticModel, context.CancellationToken) &&
                context.SemanticModel.TryGetSymbol(returnValue, context.CancellationToken, out var returnedSymbol))
            {
                if (IsInUsing(returnedSymbol, context.CancellationToken) ||
                    Disposable.IsDisposedBefore(returnedSymbol, returnValue, context.SemanticModel, context.CancellationToken))
                {
                    context.ReportDiagnostic(Diagnostic.Create(Descriptors.IDISP011DontReturnDisposed, returnValue.GetLocation()));
                }
                else
                {
                    if (returnValue.FirstAncestor<AccessorDeclarationSyntax>() is { } accessor &&
                        accessor.IsKind(SyntaxKind.GetAccessorDeclaration))
                    {
                        context.ReportDiagnostic(Diagnostic.Create(Descriptors.IDISP012PropertyShouldNotReturnCreated, returnValue.GetLocation()));
                    }

                    if (returnValue.FirstAncestor<ArrowExpressionClauseSyntax>() is { Parent: PropertyDeclarationSyntax _ })
                    {
                        context.ReportDiagnostic(Diagnostic.Create(Descriptors.IDISP012PropertyShouldNotReturnCreated, returnValue.GetLocation()));
                    }

                    if (!IsDisposableReturnTypeOrIgnored(ReturnType(context), context.Compilation))
                    {
                        context.ReportDiagnostic(Diagnostic.Create(Descriptors.IDISP005ReturnTypeShouldBeIDisposable, returnValue.GetLocation()));
                    }
                }
            }
            else if (returnValue is InvocationExpressionSyntax { ArgumentList: { Arguments: { } arguments } } invocation &&
                     context.ContainingSymbol is { ContainingType: { } containingType })
            {
                foreach (var argument in arguments)
                {
                    if (argument is { Expression: { } expression } &&
                        Disposable.IsCreation(expression, context.SemanticModel, context.CancellationToken) &&
                        context.SemanticModel.TryGetSymbol(expression, context.CancellationToken, out var argumentSymbol))
                    {
                        if (IsInUsing(argumentSymbol, context.CancellationToken) ||
                            Disposable.IsDisposedBefore(argumentSymbol, expression, context.SemanticModel, context.CancellationToken))
                        {
                            if (IsLazyEnumerable(invocation, containingType, context.SemanticModel, context.CancellationToken))
                            {
                                context.ReportDiagnostic(Diagnostic.Create(Descriptors.IDISP011DontReturnDisposed, argument.GetLocation()));
                            }
                        }
                    }
                }
            }

            if (ReturnType(context)?.IsAwaitable() == true &&
                IsInUsingScope(returnValue) &&
                !returnValue.TryFirstAncestorOrSelf<AwaitExpressionSyntax>(out _) &&
                context.SemanticModel.TryGetType(returnValue, context.CancellationToken, out var returnValueType2) &&
                returnValueType2.IsAwaitable() &&
                ShouldAwait(context, returnValue))
            {
                context.ReportDiagnostic(Diagnostic.Create(Descriptors.IDISP013AwaitInUsing, returnValue.GetLocation()));
            }
        }

        private static bool IsInUsingScope(SyntaxNode node)
        {
            return IsInUsingStatement(node) || HasPrecedingUsingDeclaration(node);
        }

        private static bool IsInUsingStatement(SyntaxNode node)
        {
            return node.TryFirstAncestor<UsingStatementSyntax>(out var usingStatement) &&
                   usingStatement.Statement.Contains(node);
        }

        private static bool HasPrecedingUsingDeclaration(SyntaxNode node)
        {
            if (node.TryFirstAncestor<UsingStatementSyntax>(out var usingStatement) &&
                usingStatement.Statement.Contains(node))
            {
                return true;
            }

            if (node.Parent?.ChildNodes().TakeWhile(x => x != node).OfType<LocalDeclarationStatementSyntax>().Any(x => x.UsingKeyword.Text == "using") ?? false)
            {
                return true;
            }

            if (node.Parent is { } parent)
            {
                return HasPrecedingUsingDeclaration(parent);
            }

            return false;
        }

        private static bool IsInUsing(ISymbol symbol, CancellationToken cancellationToken)
        {
            return symbol.TrySingleDeclaration<SyntaxNode>(cancellationToken, out var declaration) &&
                   declaration.Parent?.Parent is UsingStatementSyntax;
        }

        private static bool ShouldAwait(SyntaxNodeAnalysisContext context, ExpressionSyntax returnValue)
        {
            if (returnValue.TryFirstAncestor(out InvocationExpressionSyntax? ancestor) &&
                ancestor.TryGetMethodName(out var ancestorName) &&
                ancestorName == "ThrowsAsync")
            {
                return false;
            }

            return returnValue switch
            {
                InvocationExpressionSyntax invocation
                => !invocation.IsSymbol(KnownSymbols.Task.FromResult, context.SemanticModel, context.CancellationToken),
                MemberAccessExpressionSyntax { Name: { Identifier: { ValueText: "CompletedTask" } } } memberAccess
                => !memberAccess.IsSymbol(KnownSymbols.Task.CompletedTask, context.SemanticModel, context.CancellationToken),
                _ => true,
            };
        }

        private static bool IsLazyEnumerable(InvocationExpressionSyntax invocation, INamedTypeSymbol containingType, SemanticModel semanticModel, CancellationToken cancellationToken)
        {
            using var recursion = Recursion.Borrow(containingType, semanticModel, cancellationToken);
            return IsLazyEnumerable(invocation, recursion);
        }

        private static bool IsLazyEnumerable(InvocationExpressionSyntax invocation, Recursion recursion)
        {
            if (recursion.Target(invocation) is { Symbol: IMethodSymbol method, Declaration: { } declaration } &&
                method.ReturnType.IsAssignableTo(KnownSymbols.IEnumerable, recursion.SemanticModel.Compilation))
            {
                using var yieldWalker = YieldStatementWalker.Borrow(declaration);
                if (yieldWalker.YieldStatements.Count > 0)
                {
                    return true;
                }

                using var walker = ReturnValueWalker.Borrow(declaration, ReturnValueSearch.Member, recursion.SemanticModel, recursion.CancellationToken);
                foreach (var returnValue in walker.Values)
                {
                    if (returnValue is InvocationExpressionSyntax nestedInvocation)
                    {
                        if (IsLazyEnumerable(nestedInvocation, recursion))
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private static bool IsDisposableReturnTypeOrIgnored(ITypeSymbol? type, Compilation compilation)
        {
            if (type is null ||
                type == KnownSymbols.Void)
            {
                return true;
            }

            if (Disposable.IsAssignableFrom(type, compilation))
            {
                return true;
            }

            if (type == KnownSymbols.IAsyncDisposable)
            {
                return true;
            }

            if (type == KnownSymbols.IEnumerator)
            {
                return true;
            }

            if (type == KnownSymbols.Task)
            {
                return type is INamedTypeSymbol { IsGenericType: true } namedType &&
                       Disposable.IsAssignableFrom(namedType.TypeArguments[0], compilation);
            }

            if (type == KnownSymbols.ValueTaskOfT)
            {
                return type is INamedTypeSymbol { IsGenericType: true } namedType &&
                       Disposable.IsAssignableFrom(namedType.TypeArguments[0], compilation);
            }

            if (type == KnownSymbols.Func)
            {
                return type is INamedTypeSymbol { IsGenericType: true } namedType &&
                       Disposable.IsAssignableFrom(namedType.TypeArguments[namedType.TypeArguments.Length - 1], compilation);
            }

            return false;
        }

        private static bool IsIgnored(ISymbol symbol)
        {
            if (symbol is IMethodSymbol method)
            {
                return method == KnownSymbols.IEnumerable.GetEnumerator;
            }

            return false;
        }

        private static ITypeSymbol? ReturnType(SyntaxNodeAnalysisContext context)
        {
            if (context.Node.FirstAncestorOrSelf<AnonymousFunctionExpressionSyntax>() is { } lambda)
            {
                var method = context.SemanticModel.GetSymbolSafe(lambda, context.CancellationToken) as IMethodSymbol;
                return method?.ReturnType;
            }

            if (context.Node.TryFirstAncestor(out LocalFunctionStatementSyntax? local))
            {
                var method = context.SemanticModel.GetDeclaredSymbol(local, context.CancellationToken) as IMethodSymbol;
                return method?.ReturnType;
            }

            return context switch
            {
                { ContainingSymbol: IFieldSymbol field } => field.Type,
                { ContainingSymbol: IPropertySymbol property } => property.Type,
                { ContainingSymbol: IMethodSymbol method } => method.ReturnType,
                _ => null,
            };
        }
    }
}
