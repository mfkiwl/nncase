﻿// Copyright (c) Canaan Inc. All rights reserved.
// Licensed under the Apache license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Nncase.IR;

namespace Nncase.PatternMatch;

/// <summary>
/// Pattern.
/// </summary>
public abstract partial record Pattern : IPattern
{
    /// <summary>
    /// Gets or sets hashcode cache, for speedup get hashcode.
    /// </summary>
    protected int? HashCode { get; set; }

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode ??=
      System.HashCode.Combine(EqualityComparer<Type>.Default.GetHashCode(EqualityContract));

    /// <inheritdoc/>
    public abstract bool MatchLeaf(object input);

    /// <summary>
    /// Print members.
    /// </summary>
    /// <param name="builder">String builder.</param>
    /// <returns>Break print.</returns>
    protected virtual bool PrintMembers(StringBuilder builder)
    {
        builder.Append(this.DumpAsIL());
        return true;
    }
}

/// <summary>
/// Pattern.
/// </summary>
/// <typeparam name="TExpr">Expression type.</typeparam>
public record Pattern<TExpr> : Pattern, IPattern<TExpr>
    where TExpr : Expr
{
    /// <summary>
    /// Gets pattern for CheckedType, defulat match IR Type.
    /// </summary>
    public TypePattern TypePattern { get; init; } = TypePatternUtility.IsIRType();

    /// <inheritdoc/>
    public bool MatchLeaf(TExpr expr)
    {
        return MatchCheckedType(expr) && MatchLeafCore(expr);
    }

    /// <inheritdoc/>
    public sealed override bool MatchLeaf(object input) => input is TExpr expr && MatchLeaf(expr);

    /// <summary>
    /// Match leaf impl.
    /// </summary>
    /// <param name="expr">Input expression.</param>
    /// <returns>Match result.</returns>
    protected virtual bool MatchLeafCore(TExpr expr) => true;

    /// <summary>
    /// Match The Expr Type.
    /// </summary>
    /// <param name="expr">Expression.</param>
    /// <returns>Is Matched.</returns>
    private bool MatchCheckedType(Expr expr) => expr.CheckedType switch
    {
        IRType type => TypePattern.MatchLeaf(type),
        _ => throw new InvalidOperationException("Infer type before pattern match."),
    };
}