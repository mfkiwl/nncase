// Copyright (c) Canaan Inc. All rights reserved.
// Licensed under the Apache license. See LICENSE file in the project root for full license information.

using System;
using System.Linq;
using NetFabric.Hyperlinq;
using Nncase.IR;
using Nncase.IR.Math;
using Nncase.IR.Tensors;
using OrtKISharp;

namespace Nncase.Evaluator.Tensors;

/// <summary>
/// Evaluator for <see cref="Concat"/>.
/// </summary>
public class ConcatEvaluator : IEvaluator<Concat>, ITypeInferencer<Concat>
{
    /// <inheritdoc/>
    public IValue Visit(IEvaluateContext context, Concat cat)
    {
        var inputs = context.GetArgumentValueAsTensors(cat, Concat.Input);
        var axis = context.GetArgumentValueAsScalar<int>(cat, Concat.Axis);
        return OrtKI.Concat(inputs.Select(t => t.ToOrtTensor()).ToArray(), axis).ToValue();
    }

    /// <inheritdoc/>
    public IRType Visit(ITypeInferenceContext context, Concat target)
    {
        var inputs = context.CheckArgumentType<TupleType>(target, Concat.Input);
        var axis = context.CheckArgumentType<TensorType>(target, Concat.Axis);
        return Visit(context, target, inputs, axis);
    }

    // internal static torch.Tensor ExpandDim(torch.Tensor tensor)
    // {
    //     if (!tensor.shape.Any())
    //     {
    //         return tensor.view(new long[] { 1 });
    //     }
    //
    //     return tensor;
    // }

    private IRType Visit(ITypeInferenceContext context, Concat target, TupleType inputs, TensorType axis)
    {
        bool? allScalar = null;
        DataType? allDType = null;
        foreach (var (i, input) in Enumerable.Range(0, inputs.Count).Select(i => (i, inputs[i])))
        {
            var type = input as TensorType;
            if (type is null)
            {
                if (input is InvalidType)
                {
                    return input;
                }
                else
                {
                    return new InvalidType($"The ConCat Item[{i}] Must Be TensorType But Get {input.GetType().Name}");
                }
            }

            allScalar = (allScalar ?? type.IsScalar) & type.IsScalar;
            allDType ??= type.DType;
            if (allDType != type.DType)
            {
                return new InvalidType($"The ConCat Item[{i}] Must Be {allDType} But Get {type.DType.GetDisplayName()}");
            }
        }

        var input0 = (TensorType)inputs[0];
        if (allScalar == true && allDType is not null)
        {
            return new TensorType(allDType, new[] { inputs.Count });
        }

        InvalidType? invalidType = null;
        var axisValue = ((TensorConst)context.GetArgument(target, Concat.Axis)).Value.ToScalar<int>();
        var shapeValue = Enumerable.Range(0, input0.Shape.Rank).Select(i =>
        {
            if (i == axisValue)
            {
                return AxisDim(inputs, axisValue);
            }

            // if all input shape[dim] is not same, return invalid
            else
            {
                var allAxisDimIsSame = inputs.Fields.Aggregate(
                    true,
                    (prod, next) => prod && ((TensorType)next).Shape[i].IsFixed);
                if (allAxisDimIsSame)
                {
                    return ((TensorType)inputs[0]).Shape[i];
                }
                else
                {
                    invalidType = new InvalidType("Concat dims that except the shape of axis dim are different");
                    return Dimension.Unknown;
                }
            }
        });
        var shape = new Shape(shapeValue);
        return (invalidType as IRType) ?? new TensorType(input0.DType, shape);
    }

    // axis: if one of inputs shape[axis] is unknown
    // then dims axis is known
    // else get sum of dims
    private Dimension AxisDim(TupleType inputs, int axisValue)
    {
        var allAxisDimIsFixed = inputs.Fields.Aggregate(
            true,
            (prod, next) => prod && ((TensorType)next).Shape[axisValue].IsFixed);
        if (allAxisDimIsFixed)
        {
            return inputs.Fields.Aggregate(
                0,
                (prod, next) => prod + ((TensorType)next).Shape[axisValue].FixedValue);
        }
        else
        {
            return Dimension.Unknown;
        }
    }
}