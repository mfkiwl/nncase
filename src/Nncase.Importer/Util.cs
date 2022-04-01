// Copyright (c) Canaan Inc. All rights reserved.
// Licensed under the Apache license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using Nncase.IR;
using Nncase.IR.Tensors;
using F = Nncase.IR.F;
using Tuple = Nncase.IR.Tuple;
using static Nncase.IR.F.Tensors;

namespace Nncase
{
    public static class Util
    {
        public static Expr ShapeIndex(in Expr input, int index)
        {
            return GetItem(F.Tensors.ShapeOf(input), index);
        }

        public static Expr GetItem(in Expr input, Expr index)
        {
            return F.Tensors.Squeeze(F.Tensors.Slice(input, index, index + 1, 1), 0L);
        }

        public static (Expr, Expr) GetHW(in Expr input)
        {
            return (ShapeIndex(input, 2), ShapeIndex(input, 3));
        }

        /// <summary>
        /// onnx format pads to nncase format(same as tf)
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        public static Expr PadTranslate(Expr pads)
        {
            return Transpose(Reshape(pads, new[] {-1, 2}), new[] {1, 0});
        }
        
        public static TensorConst ZeroTensor()
        {
            return new TensorConst(Tensor.FromSpan<int>(new[] {0}));
        }
        
        /// <summary>
        /// 
        /// </summary>
        /// <param name="padH"> [before, after] </param>
        /// <param name="padW"> [before, after] </param>
        /// <returns></returns>
        public static Expr ConcatPadding(Expr[] padH, Expr[] padW)
        {
            // return [[padh_before, padh_after],
            //         [padw_before, padw_after]]
            return Stack(new Tuple(
                Stack(new Tuple(padH), 0),
                Stack(new Tuple(padW), 0)), 0);
        }

        private static Expr GetWindowedOutputSize(Expr size, Expr filter, Expr stride, Expr dilation, bool same, bool ceilMode)
        {
            var effectiveFilterSize = ((filter - 1) * dilation) + 1;
            var falseBranch = !ceilMode
                ? ((size - effectiveFilterSize + stride) / stride)
                : F.Tensors.Cast(F.Math.Ceil(
                        F.Tensors.Cast((size - effectiveFilterSize + stride), DataTypes.Float32) /
                        F.Tensors.Cast(stride, DataTypes.Float32)),
                    DataTypes.Int32);
            var trueBranch = (size + stride - 1) / stride;
            return same ? trueBranch : falseBranch;
        }

        private static Expr[] GetWindowedPaddingValue(Expr inputSize, Expr outputSize, Expr filter, Expr stride, Expr dilation, bool lower)
        {
            var effectiveFilterSize = ((filter - 1) * dilation) + 1;
            var padding = F.Math.Max(0, ((outputSize - 1) * stride) + effectiveFilterSize - inputSize);
            var before = F.Tensors.Cast(padding / 2, DataTypes.Int32);
            var after = F.Tensors.Cast(padding - (padding / 2), DataTypes.Int32);
            if (lower)
            {
                return new[] { F.Math.Max(before, after), F.Math.Min(before, after) };
            }

            return new[] { before, after };
        }

        // todo:refactor and set private this
        public static Expr[] GetWindowedPadding(Expr inputSize, Expr filter, Expr stride, Expr dilation, bool same, bool lower = false)
        {
            var outputSize = GetWindowedOutputSize(inputSize, filter, stride, dilation, same, false);
            return GetWindowedPaddingValue(inputSize, outputSize, filter, stride, dilation, lower);
        }

        // lower used for onnx when auto_pad attr is SAME_LOWER
        public static Expr GetPaddings(Expr input, Expr weights, long[] stride, long[] dilation, bool same, bool lower = false)
        {
            var (inH, inW) = GetHW(input);
            var (fH, fW) = GetHW(weights);
            var padH = GetWindowedPadding(inH, fH, (int)stride[0], (int)dilation[0], same, lower);
            var padW = GetWindowedPadding(inW, fW, (int)stride[1], (int)dilation[1], same, lower);
            return ConcatPadding(padH, padW);
        }
    }
}