﻿// Copyright (c) Canaan Inc. All rights reserved.
// Licensed under the Apache license. See LICENSE file in the project root for full license information.

using System.Reactive;
using System.Runtime.CompilerServices;
using NetFabric.Hyperlinq;
using Nncase.Diagnostics;
using Nncase.IR;
using Nncase.IR.Buffers;
using Nncase.IR.CPU;
using Nncase.IR.F;
using Nncase.IR.Math;
using Nncase.Passes.Mutators;
using Nncase.PatternMatch;
using Nncase.Schedule;
using Nncase.Targets;
using Nncase.TIR;
using Nncase.TIR.Builders;
using Buffer = Nncase.TIR.Buffer;
using MathF = Nncase.IR.F.Math;
using Range = Nncase.TIR.Range;
using Tuple = Nncase.IR.Tuple;

namespace Nncase.Passes.Tile;

/// <summary>
/// convert the fusion to prim func.
/// </summary>
internal sealed class SingleCPUFusionConverter
{
    public TIR.PrimFunction Visit(Fusion fusion)
    {
        var body = new List<Expr>();
        var visitor = new ConvertVisitor(body);
        visitor.Visit(fusion);
        return T.PrimFunc(fusion.Name, fusion.ModuleKind, visitor.InputBuffers.Concat(visitor.OutputBuffers).ToArray()).Body(body.ToArray()).Build();
    }

    private sealed class ConvertVisitor : ExprVisitor<Unit, Unit>
    {
        private readonly Dictionary<Expr, TIR.Buffer> _buffersMap = new(ReferenceEqualityComparer.Instance);
        private readonly List<Expr> _mainBody;

        public ConvertVisitor(List<Expr> mainBody)
        {
            _mainBody = mainBody;
        }

        public Fusion VisitRootFusion => (Fusion)VisitRoot!;

        public IEnumerable<TIR.PhysicalBuffer> OutputBuffers => _buffersMap.Values.OfType<TIR.PhysicalBuffer>().Where(b => b.MemLocation == MemoryLocation.Output);

        public IEnumerable<TIR.PhysicalBuffer> InputBuffers => _buffersMap.Values.OfType<TIR.PhysicalBuffer>().Where(b => b.MemLocation == MemoryLocation.Input);

        protected override Unit DefaultVisitLeaf(Expr expr)
        {
            return default;
        }

        protected override Unit VisitLeafCall(Call expr)
        {
            var arguments = expr.Arguments.AsValueEnumerable().Select(TryAllocateBuffer).ToArray();
            var ret = TryAllocateBuffer(expr);
            var op = ((CPUKernelOp)expr.Target).Target;

            switch (op)
            {
                case Unary unary:
                    GenerateUnary(unary, arguments, ret);
                    break;
                default:
                    throw new NotSupportedException();
            }

            return default;
        }

        private void GenerateUnary(Unary unary, ReadOnlySpan<Buffer> arguments, Buffer ret)
        {
            var input = arguments[Unary.Input.Index];
            var loops = Enumerable.Range(0, input.Rank).Select(i => (T.ForLoop(out var loopVar, (0, input.Dimensions[i]), LoopMode.Serial, $"loop_{i}"), loopVar)).ToArray();
            var input_index = Enumerable.Range(0, input.Rank).Aggregate((Expr)0, (acc, i) => acc + (input.Strides[i] * loops[i].loopVar));
            var output_index = Enumerable.Range(0, input.Rank).Aggregate((Expr)0, (acc, i) => acc + (ret.Strides[i] * loops[i].loopVar));
            Expr stmt = T.Store(ret, output_index, IR.F.Math.Unary(unary.UnaryOp, T.Load(input, output_index)));
            var final = loops.Reverse().Aggregate(stmt, (acc, p) => p.Item1.Body(acc).Build());
            _mainBody.Add(T.Block(nameof(Unary)).Body(final).Build());
        }

        private TIR.Buffer TryAllocateBuffer(Expr expr)
        {
            var name = $"buffer_{_buffersMap.Keys.Count}";
            if (!_buffersMap.TryGetValue(expr, out var buffer))
            {
                switch (expr)
                {
                    case Call c:
                        if (ReferenceEquals(c, VisitRootFusion.Body))
                        {
                            buffer = T.PhysicalBuffer(c.CheckedDataType, MemoryLocation.Output, c.CheckedShape.ToValueArray(), out _, name);
                        }
                        else
                        {
                            buffer = T.Buffer(c.CheckedDataType, MemoryLocation.Data, c.CheckedShape.ToValueArray().Select(i => (Expr)i).ToArray(), out _, name);
                        }

                        break;
                    case Var v:
                        buffer = T.PhysicalBuffer(v.CheckedDataType, MemoryLocation.Input, v.CheckedShape.ToValueArray(), out _, name);
                        break;
                    case TensorConst c:
                        buffer = T.PhysicalBuffer(c.Value.ElementType, MemoryLocation.Rdata, c.Value.Dimensions, out _, name);
                        break;
                    default:
                        throw new NotSupportedException();
                }

                _buffersMap.Add(expr, buffer);
            }

            return buffer;
        }
    }
}