﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.LanguageServices;
using Microsoft.CodeAnalysis.Shared.Extensions;

namespace Microsoft.CodeAnalysis.FindSymbols.Finders
{
    internal abstract class AbstractMethodOrPropertyOrEventSymbolReferenceFinder<TSymbol> : AbstractReferenceFinder<TSymbol>
        where TSymbol : ISymbol
    {
        protected AbstractMethodOrPropertyOrEventSymbolReferenceFinder()
        {
        }

        protected override async Task<ImmutableArray<SymbolAndProjectId>> DetermineCascadedSymbolsAsync(
            SymbolAndProjectId<TSymbol> symbolAndProjectId,
            Solution solution,
            IImmutableSet<Project> projects,
            FindReferencesSearchOptions options,
            CancellationToken cancellationToken)
        {
            // Static methods can't cascade.
            var symbol = symbolAndProjectId.Symbol;
            if (!symbol.IsStatic)
            {
                if (symbol.ContainingType.TypeKind == TypeKind.Interface)
                {
                    // We have an interface method.  Find all implementations of that method and
                    // cascade to them.
                    return await SymbolFinder.FindImplementationsAsync(symbolAndProjectId, solution, projects, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    // We have a normal method.  Find any interface methods that it implicitly or
                    // explicitly implements and cascade down to those.
                    var interfaceMembersImplemented = await SymbolFinder.FindImplementedInterfaceMembersAsync(
                        symbolAndProjectId, solution, projects, cancellationToken).ConfigureAwait(false);

                    // Finally, methods can cascade through virtual/override inheritance.  NOTE(cyrusn):
                    // We only need to go up or down one level.  Then, when we're finding references on
                    // those members, we'll end up traversing the entire hierarchy.
                    var overrides = await SymbolFinder.FindOverridesAsync(
                        symbolAndProjectId, solution, projects, cancellationToken).ConfigureAwait(false);

                    var overriddenMember = symbolAndProjectId.WithSymbol(symbol.OverriddenMember());
                    if (overriddenMember.Symbol == null)
                    {
                        return interfaceMembersImplemented.Concat(overrides);
                    }

                    return interfaceMembersImplemented.Concat(overrides).Concat(overriddenMember);
                }
            }

            return ImmutableArray<SymbolAndProjectId>.Empty;
        }

        protected ImmutableArray<IMethodSymbol> GetReferencedAccessorSymbols(
            ISemanticFactsService semanticFacts, SemanticModel model,
            IPropertySymbol property, SyntaxNode node, CancellationToken cancellationToken)
        {
            if (semanticFacts.IsWrittenTo(model, node, cancellationToken))
            {
                // if it was only written to, then only the setter was referenced.
                // if it was written *and* read, then both accessors were referenced.
                return semanticFacts.IsOnlyWrittenTo(model, node, cancellationToken)
                    ? ImmutableArray.Create(property.SetMethod)
                    : ImmutableArray.Create(property.GetMethod, property.SetMethod);
            }
            else
            {
                // Wasn't written. This could be a normal read, or used through something
                // like nameof().
                return semanticFacts.IsNameOfContext(model, node.SpanStart, cancellationToken)
                    ? ImmutableArray<IMethodSymbol>.Empty
                    : ImmutableArray.Create(property.GetMethod);
            }
        }
    }
}
