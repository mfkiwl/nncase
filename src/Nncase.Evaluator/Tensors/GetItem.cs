// Copyright (c) Canaan Inc. All rights reserved.
// Licensed under the Apache license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Formatters.Binary;
using NetFabric.Hyperlinq;
using Nncase.IR;
using Nncase.IR.Tensors;

namespace Nncase.Evaluator.Tensors;

/// <summary>
/// Evaluator for <see cref="GetItem"/>.
/// </summary>
[EvaluatorGenerator]
[TypeInferGenerator]
public partial class GetItemEvaluator : IEvaluator<GetItem>, ITypeInferencer<GetItem>
{
    private Tensor Visit(IValue Input, IValue Index)
    {
        if (Input.Type is TensorType ttype)
        {
            var tensor = Input.AsTensor();
            var elementSize = tensor.ElementType.SizeInBytes;
            var indices = new int[tensor.Rank];
            var indexTensor = Index.AsTensor().Cast<int>();
            indexTensor.Buffer.CopyTo(indices);
            var linearIndex = TensorUtilities.GetIndex(tensor.Strides, indices);
            var returnDims = tensor.Dimensions.AsValueEnumerable().Skip(indexTensor.Length).ToArray();
            var elementsCount = (int)TensorUtilities.GetProduct(returnDims);

            var src = tensor.BytesBuffer.Slice(elementSize * linearIndex, elementSize * elementsCount);
            return Tensor.FromBytes(new TensorType(ttype.DType, returnDims), src);
        }

        return Input.AsTensors()[Index.AsTensor().ToScalar<int>()];
    }

    private IRType Visit(ITypeInferenceContext context, GetItem target, IRType Input, TensorType Index)
    {
        IRType ret = new InvalidType("Need Be Reset!");
        switch (Input)
        {
            case TensorType tensorType:
                ret = new TensorType(tensorType.DType, new Shape(tensorType.Shape.Skip(System.Math.Max(Index.Shape.Rank, 1))));
                break;
            case TupleType tupleType:
                if (context.GetArgument(target, GetItem.Index) is TensorConst @const)
                {
                    var index = @const.Value.ToScalar<int>();
                    if (index < tupleType.Count)
                    {
                        ret = tupleType[index];
                    }
                    else
                    {
                        ret = new InvalidType($"The Input Tuple Count = {tupleType.Count}, But Index = {index}");
                    }
                }
                else
                {
                    ret = AnyType.Default;
                }

                break;
            default:
                break;
        }

        return ret;
    }
}