﻿// Copyright (c) Canaan Inc. All rights reserved.
// Licensed under the Apache license. See LICENSE file in the project root for full license information.

using Nncase.IR;
using static Nncase.IR.F.Tensors;

namespace Nncase.Utilities;

public static class ShapeExprUtility
{
    public static Expr BroadcastShape(Expr lhsShape, params Expr[] rhsShape)
    {
        var tmpTensor = new[] { ConstantOfShape(lhsShape, 0) }
            .Concat(rhsShape)
            .Aggregate((sum, shape) => ConstantOfShape(shape, 0) * sum);
        return ShapeOf(tmpTensor);
    }

    public static Expr Positive(Expr axis, Expr inShape)
    {
        var rank = ShapeOf(inShape)[0];
        return new If(axis < 0, axis + rank, axis);
    }

    public static Expr Slice(Expr shape, int begin, int end)
    {
        return IR.F.Tensors.Slice(shape, new[] { begin }, new[] { end }, 1);
    }

    public static Expr Slice(Expr shape, Expr begin, Expr end)
    {
        return IR.F.Tensors.Slice(shape, StackOne(begin), StackOne(end), 1);
    }

    public static Expr Replace(Expr shapeExpr, Expr index, Expr value)
    {
        return SliceAndMerge(shapeExpr, index, value, 1);
    }

    public static Expr Insert(Expr shapeExpr, Expr index, Expr value)
    {
        return SliceAndMerge(shapeExpr, index, value, 0);
    }

    public static Expr Remove(Expr shapeExpr, Expr index)
    {
        var front = Slice(shapeExpr, 0, index);
        var last = Slice(shapeExpr, index + 1, int.MaxValue);
        return Concat(new IR.Tuple(front, last), 0);
    }

    public static Expr ShapeOf(Expr expr) => Cast(IR.F.Tensors.ShapeOf(expr), DataTypes.Int32);

    private static Expr SliceAndMerge(Expr shapeExpr, Expr index, Expr value, Expr indexOffset)
    {
        var front = Slice(shapeExpr, 0, index);
        var last = Slice(shapeExpr, index + indexOffset, int.MaxValue);
        return Concat(new IR.Tuple(front, StackOne(value), last), 0);
    }

    private static Expr StackOne(Expr expr)
    {
        return Stack(new IR.Tuple(expr), 0);
    }
}