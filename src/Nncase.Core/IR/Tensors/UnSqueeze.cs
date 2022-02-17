// Copyright (c) Canaan Inc. All rights reserved.
// Licensed under the Apache license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Nncase.IR.TypePatternUtility;

namespace Nncase.IR.Tensors;

/// <summary>
/// Unsqueeze expression.
/// </summary>
public sealed record Unsqueeze() : Op
{
    /// <summary>
    /// Gets input.
    /// </summary>
    public static readonly ParameterInfo Input = new(typeof(Unsqueeze), 0, "input");

    /// <summary>
    /// Gets dimension.
    /// </summary>
    public static ParameterInfo Dim = new(typeof(Unsqueeze), 1, "dim", IsRank(1) & IsIntegral());
}