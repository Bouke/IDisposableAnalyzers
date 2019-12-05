﻿namespace IDisposableAnalyzers
{
    using System.Diagnostics.CodeAnalysis;
    using System.Threading;
    using Gu.Roslyn.AnalyzerExtensions;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;
    using Microsoft.CodeAnalysis.CSharp.Syntax;

    internal static partial class Disposable
    {
        internal static bool DisposedByReturnValue(ArgumentSyntax candidate, SemanticModel semanticModel, CancellationToken cancellationToken, [NotNullWhen(true)] out ExpressionSyntax? creation)
        {
            if (candidate.TryFirstAncestor(out TypeDeclarationSyntax? containingTypeDeclaration) &&
                semanticModel.TryGetNamedType(containingTypeDeclaration, cancellationToken, out var containingType))
            {
                using var recursion = Recursion.Borrow(containingType, semanticModel, cancellationToken);
                if (recursion.Target(candidate) is { } target)
                {
                    return DisposedByReturnValue(target, recursion, out creation);
                }
            }

            creation = null;
            return false;
        }

        private static bool DisposedByReturnValue(ExpressionSyntax candidate, Recursion recursion, [NotNullWhen(true)] out ExpressionSyntax? creation)
        {
            switch (candidate)
            {
                case { Parent: ArgumentSyntax argument }
                    when recursion.Target(argument) is { } target:
                    return DisposedByReturnValue(target, recursion, out creation);
                case { Parent: InitializerExpressionSyntax { Parent: ObjectCreationExpressionSyntax objectCreation } }
                    when recursion.SemanticModel.TryGetType(objectCreation, recursion.CancellationToken, out var type) &&
                         type == KnownSymbol.CompositeDisposable:
                    creation = objectCreation;
                    return true;
                case { }
                    when Identity(candidate, recursion) is { } id:
                    return DisposedByReturnValue(id, recursion, out creation);
                case { } expression
                     when recursion.Target(expression) is { } target &&
                          DisposedByReturnValue(target, recursion):
                    creation = expression;
                    return true;
                default:
                    creation = null;
                    return false;
            }
        }

        private static bool DisposedByReturnValue(Target<ArgumentSyntax, IParameterSymbol, BaseMethodDeclarationSyntax> target, Recursion recursion, [NotNullWhen(true)] out ExpressionSyntax? creation)
        {
            switch (target)
            {
                case { Symbol: { ContainingSymbol: IMethodSymbol constructor } parameter, Source: { Parent: ArgumentListSyntax { Parent: ObjectCreationExpressionSyntax { Type: { } type } objectCreation } } }:
                    if (type == KnownSymbol.SingleAssignmentDisposable ||
                        type == KnownSymbol.RxDisposable ||
                        type == KnownSymbol.CompositeDisposable)
                    {
                        creation = objectCreation;
                        return true;
                    }

                    if (Disposable.IsAssignableFrom(target.Symbol.ContainingType, recursion.SemanticModel.Compilation))
                    {
                        if (constructor.ContainingType == KnownSymbol.BinaryReader ||
                            constructor.ContainingType == KnownSymbol.BinaryWriter ||
                            constructor.ContainingType == KnownSymbol.StreamReader ||
                            constructor.ContainingType == KnownSymbol.StreamWriter ||
                            constructor.ContainingType == KnownSymbol.CryptoStream ||
                            constructor.ContainingType == KnownSymbol.DeflateStream ||
                            constructor.ContainingType == KnownSymbol.GZipStream ||
                            constructor.ContainingType == KnownSymbol.StreamMemoryBlockProvider)
                        {
                            if (constructor.TryFindParameter("leaveOpen", out var leaveOpenParameter) &&
                                objectCreation.TryFindArgument(leaveOpenParameter, out var leaveOpenArgument) &&
                                leaveOpenArgument.Expression is LiteralExpressionSyntax literal &&
                                literal.IsKind(SyntaxKind.TrueLiteralExpression))
                            {
                                creation = null;
                                return false;
                            }

                            creation = objectCreation;
                            return true;
                        }

                        if (parameter.Type.IsAssignableTo(KnownSymbol.HttpMessageHandler, recursion.SemanticModel.Compilation) &&
                            constructor.ContainingType.IsAssignableTo(KnownSymbol.HttpClient, recursion.SemanticModel.Compilation))
                        {
                            if (constructor.TryFindParameter("disposeHandler", out var leaveOpenParameter) &&
                                objectCreation.TryFindArgument(leaveOpenParameter, out var leaveOpenArgument) &&
                                leaveOpenArgument.Expression is LiteralExpressionSyntax literal &&
                                literal.IsKind(SyntaxKind.FalseLiteralExpression))
                            {
                                creation = null;
                                return false;
                            }

                            creation = objectCreation;
                            return true;
                        }

                        if (DisposedByReturnValue(target, recursion))
                        {
                            creation = objectCreation;
                            return true;
                        }
                    }

                    break;
                case { Symbol: { ContainingSymbol: IMethodSymbol method }, Source: { Parent: ArgumentListSyntax { Parent: InvocationExpressionSyntax invocation } } }:
                    if (method == KnownSymbol.Task.FromResult)
                    {
                        creation = invocation;
                        return true;
                    }

                    if (Disposable.IsAssignableFrom(method.ReturnType, recursion.SemanticModel.Compilation) &&
                        DisposedByReturnValue(target, recursion))
                    {
                        creation = invocation;
                        return true;
                    }

                    break;
            }

            creation = null;
            return false;
        }

        private static bool DisposedByReturnValue<TSource, TSymbol, TNode>(Target<TSource, TSymbol, TNode> target, Recursion recursion)
            where TSource : SyntaxNode
            where TSymbol : ISymbol
            where TNode : SyntaxNode
        {
            switch (target.Symbol)
            {
                case IMethodSymbol { ReturnsVoid: true }:
                case IMethodSymbol { ReturnType: { MetadataName: "Task" } }:
                    return false;
                case IMethodSymbol { ReturnType: { } returnType, DeclaringSyntaxReferences: { Length: 0 } }:
                    // we assume here, not sure it is the best assumption.
                    return Disposable.IsAssignableFrom(returnType, recursion.SemanticModel.Compilation);
                case IMethodSymbol { IsExtensionMethod: true, ReducedFrom: { } reducedFrom }
                     when reducedFrom.Parameters.TryFirst(out var parameter):
                    return DisposedByReturnValue(Target.Create(target.Source, parameter, target.Declaration), recursion);
                case IFieldSymbol _:
                case IPropertySymbol _:
                    return false;
            }

            if (target.Declaration is { })
            {
                using var walker = UsagesWalker.Borrow(target.Symbol, target.Declaration, recursion.SemanticModel, recursion.CancellationToken);
                foreach (var usage in walker.Usages)
                {
                    if (Assigns(usage, recursion, out var fieldOrProperty) &&
                        DisposableMember.IsDisposed(fieldOrProperty, target.Symbol.ContainingType, recursion.SemanticModel, recursion.CancellationToken).IsEither(Result.Yes, Result.AssumeYes))
                    {
                        return true;
                    }

                    if (usage.Parent is ArgumentSyntax argument &&
                        recursion.Target(argument) is { } argumentTarget &&
                        DisposedByReturnValue(argumentTarget, recursion, out var invocationOrObjectCreation) &&
                        Returns(invocationOrObjectCreation, recursion))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
