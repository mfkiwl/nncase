// Copyright (c) Canaan Inc. All rights reserved.
// Licensed under the Apache license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Nncase.IR;
using Google.Protobuf;
using Google.Protobuf.Collections;
using LanguageExt;
using Nncase.IR.Tensors;
using Onnx;
using static Nncase.IR.F.Tensors;
using Tuple = Nncase.IR.Tuple;

namespace Nncase.Importer
{
    public sealed partial class OnnxImporter
    {
        private static readonly Dictionary<TensorProto.Types.DataType, DataType> _typeMap = new()
        {
            { TensorProto.Types.DataType.Bool, DataTypes.Boolean },
            { TensorProto.Types.DataType.Float16, DataTypes.Float16 },
            { TensorProto.Types.DataType.Float, DataTypes.Float32 },
            { TensorProto.Types.DataType.Double, DataTypes.Float64 },
            { TensorProto.Types.DataType.Int16, DataTypes.Int16 },
            { TensorProto.Types.DataType.Int32, DataTypes.Int32 },
            { TensorProto.Types.DataType.Int64, DataTypes.Int64 },
            { TensorProto.Types.DataType.Int8, DataTypes.Int8 },
            { TensorProto.Types.DataType.String, DataTypes.Utf8Char },
            { TensorProto.Types.DataType.Uint32, DataTypes.UInt32 },
            { TensorProto.Types.DataType.Uint64, DataTypes.UInt64 },
            { TensorProto.Types.DataType.Uint8, DataTypes.UInt8 },
        };

        private readonly ModelProto _model;
        private readonly GraphProto _graph;
        private readonly Dictionary<string, long> _opSetMap;
        private Dictionary<string, Expr> _outputTensors;
        private Dictionary<string, TensorProto> _constTensors;

        public OnnxImporter(byte[] onnxModel)
        {
            _opSetMap = new Dictionary<string, long>();
            var m = new MessageParser<ModelProto>(
                () => new ModelProto());

            // todo:how to check valid?
            _model = m.ParseFrom(onnxModel);
            foreach (var opSet in _model.OpsetImport)
            {
                _opSetMap.Add(opSet.Domain, opSet.Version);
            }

            _graph = _model.Graph;
        }

        private bool EmptyTensor(TensorProto tensor)
        {
            return tensor.Dims.Count == 1 && tensor.Dims[0] == 0;
        }
        
        private Tensor GetTensor(TensorProto tensor)
        {
            var shape = GetShape(tensor).ToValueArray();
            var type = GetDataType(tensor);
            var dt = (TensorProto.Types.DataType)tensor.DataType;

            // should not use tensor.DataLocation to distinguish whether it is RawData
            if (tensor.RawData.ToByteArray().Length() != 0)
            {
                return Tensor.FromBytes(type, tensor.RawData.ToByteArray(), shape);
            }
            else
            {
                return dt switch
                {
                    // todo:not directly supported type should convert
                    //TensorProto.Types.DataType.Bool => Tensor.FromSpan(),
                    //TensorProto.Types.DataType.Float16 => Tensor.FromSpan(),
                    TensorProto.Types.DataType.Float => Tensor.FromSpan<float>(tensor.FloatData.ToArray(), shape),
                    TensorProto.Types.DataType.Double => Tensor.FromSpan<double>(tensor.DoubleData.ToArray(), shape),

                    //TensorProto.Types.DataType.Int16 => Tensor.FromSpan(),
                    TensorProto.Types.DataType.Int32 => Tensor.FromSpan<int>(tensor.Int32Data.ToArray(), shape),
                    TensorProto.Types.DataType.Int64 => Tensor.FromSpan<long>(tensor.Int64Data.ToArray(), shape),

                    //TensorProto.Types.DataType.Int8 => Tensor.FromSpan(),
                    //TensorProto.Types.DataType.String => Tensor.FromSpan(),
                    //TensorProto.Types.DataType.Uint32 => Tensor.FromSpan(),
                    //TensorProto.Types.DataType.Uint64 => Tensor.FromSpan<ulong>(tensor.Uint64Data.ToArray(), shape),
                    //TensorProto.Types.DataType.Uint8 => Tensor.FromSpan(),
                    _ => throw new NotSupportedException($"Not supported onnx constant data type{dt}"),
                };
            }
        }

        public IRModule Import()
        {
            _constTensors = _graph.Initializer
                .ToDictionary(tensor => tensor.Name, tensor => tensor);

            var createdInputs = _graph.Input
                .Where(n => !_constTensors.ContainsKey(n.Name))
                .Select(n => new Var(n.Name, GetIRType(n)));

            _outputTensors = createdInputs.ToDictionary(n => n.Name, n => (Expr) n);
            _graph.Node.ToList().ForEach(Visit);

            var outputs = _graph.Output.Select(o => _outputTensors[o.Name]).ToArray();

            return MakeMainModule(outputs, createdInputs.ToArray());
        }

        private IRModule MakeMainModule(Expr[] body, IRArray<Var> parameter)
        {
            var outputTuple = new IR.Tuple(ImmutableArray.Create(body));
            var mainFunc = new Function("main", outputTuple, parameter);
            var module = new IRModule();
            module.Add(mainFunc);
            module.Entry = mainFunc;
            return module;
        }

        public Shape GetShape(ValueInfoProto v)
        {
            var shape = v.Type.TensorType.Shape.Dim.Select(x => x.DimValue);
            return new Shape(shape);
        }

        public Shape GetShape(TensorProto tensor)
        {
            return new Shape(tensor.Dims);
        }

        public TensorType GetIRType(ValueInfoProto v)
        {
            return new TensorType(GetDataType(v), GetShape(v));
        }

        public TensorType GetIRType(TensorProto v)
        {
            return new TensorType(GetDataType(v), GetShape(v));
        }

        private Expr GetInputExpr(NodeProto n, int index)
        {
            // todo:is null?
            var id = n.Input[index];
            if (_outputTensors.TryGetValue(id, out var expr))
            {
                return expr;
            }

            return _graph.Initializer
                    .Find(x => x.Name == id)
                    .Match(
                        GetTensor,
                        () => throw new InvalidDataException($"Cannot load tensor data (tensor:{id})."));
        }

        private DataType GetInputDataType(NodeProto n, int index)
        {
            var id = n.Input[index];
            return _graph.Input.Concat(_graph.ValueInfo)
                .Find(x => x.Name == id)
                .Match(GetDataType,
                    () => throw new InvalidDataException($"Cannot load tensor data (tensor:{id})."));
        }

        private Expr GetSingleInputExpr(NodeProto n)
        {
            return GetInputExpr(n, 0);
        }

        private (Expr, Expr) GetInputExprs(NodeProto n, int index0, int index1)
        {
            return (GetInputExpr(n, index0), GetInputExpr(n, index1));
        }

        private Option<Expr> GetOptionInputExpr(NodeProto n, int index)
        {
            if (n.Input.Count <= index)
            {
                return Option<Expr>.None;
            }
            
            var id = n.Input[index];
            if (id == "")
            {
                return Option<Expr>.None;
            }
            
            if (_outputTensors.TryGetValue(id, out var expr))
            {
                return expr;
            }
            
            return _graph.Initializer
                .Find(x => x.Name == id)
                .Match(
                    t => EmptyTensor(t) ? Option<Expr>.None : Option<Expr>.Some(GetTensor(t)),
                    () => throw new InvalidDataException($"Cannot load tensor data (tensor:{id})."));
        }

        private Expr GetOptionInputExpr(NodeProto n, int index, Expr defaultExpr)
        {
            return GetOptionInputExpr(n, index).Or(defaultExpr);
        }

        private (Option<Expr>, Option<Expr>) GetOptionInputExprs(NodeProto n, int index0, int index1)
        {
            return (GetOptionInputExpr(n, index0), GetOptionInputExpr(n, index1));
        }

        // about op set: https://github.com/onnx/onnx/issues/3678
        private long GetOpSet(string domain)
        {
            return _opSetMap[domain];
        }

        private long GetOpSet(NodeProto node)
        {
            return _opSetMap[node.Domain];
        }

        private void Visit(NodeProto op)
        {
            var output = op.OpType switch
            {
                "Abs" => VisitUnary(op, UnaryOp.Abs),
                "Acos" => VisitUnary(op, UnaryOp.Acos),
                "Acosh" => VisitUnary(op, UnaryOp.Acosh),
                "And" => VisitBinary(op, BinaryOp.LogicalAnd),
                "ArgMax" => VisitReduceArg(op, ReduceArgOp.ArgMax),
                "ArgMin" => VisitReduceArg(op, ReduceArgOp.ArgMin),
                "Asin" => VisitUnary(op, UnaryOp.Asin),
                "Asinh" => VisitUnary(op, UnaryOp.Asinh),
                "Add" => VisitBinary(op, BinaryOp.Add),
                "AveragePool" => VisitReduceWindow2D(op, ReduceOp.Mean, 0f),
                "BatchNormalization" => VisitBatchNormalization(op),
                "Cast" => VisitCast(op),
                "Ceil" => VisitUnary(op, UnaryOp.Ceil),
                "Celu" => VisitCelu(op),
                "Clip" => VisitClip(op),
                "Concat" => VisitConcat(op),
                "Constant" => VisitConstant(op),
                "ConstantOfShape" => VisitConstantOfShape(op),
                "Conv" => VisitConv2D(op),
                "ConvTranspose" => VisitConv2DTranspose(op),
                "Cos" => VisitUnary(op, UnaryOp.Cos),
                "Cosh" => VisitUnary(op, UnaryOp.Cosh),
                "CumSum" => VisitCumSum(op),
                "DepthToSpace" => VisitDepthToSpace(op),

                // "DequantizeLinear" => VisitDequantizeLinear(op),
                "Div" => VisitBinary(op, BinaryOp.Div),
                "Dropout" => VisitDropout(op),
                "Elu" => VisitElu(op),
                "Exp" => VisitUnary(op, UnaryOp.Exp),
                "Expand" => VisitExpand(op),
                "Flatten" => VisitFlatten(op),
                "Floor" => VisitUnary(op, UnaryOp.Floor),
                "Gather" => VisitGather(op),
                "GatherND" => VisitGatherND(op),
                "Gemm" => VisitGemm(op),
                "GlobalAveragePool" => VisitReduceWindow2D(op, ReduceOp.Mean, float.MinValue, true),
                "GlobalMaxPool" => VisitReduceWindow2D(op, ReduceOp.Max, float.MinValue, true),
                "Hardmax" => VisitHardmax(op),
                "HardSigmoid" => VisitHardSigmoid(op),
                "HardSwish" => VisitHardSwish(op),
                "Identity" => VisitIdentity(op),
                "InstanceNormalization" => VisitInstanceNormalization(op),
                "LpNormalization" => VisitLpNormalization(op),
                "LeakyRelu" => VisitLeakyRelu(op),
                "Log" => VisitUnary(op, UnaryOp.Log),
                "LogSoftmax" => VisitLogSoftmax(op),
                "LRN" => VisitLRN(op),
                "LSTM" => VisitLSTM(op),
                "MatMul" => VisitMatMul(op),
                "MaxPool" => VisitReduceWindow2D(op, ReduceOp.Max, float.MinValue),
                "Max" => VisitBinary(op, BinaryOp.Max),
                "Min" => VisitBinary(op, BinaryOp.Min),
                "Mul" => VisitBinary(op, BinaryOp.Mul),
                "Neg" => VisitUnary(op, UnaryOp.Neg),
                "OneHot" => VisitOneHot(op),
                "Pad" => VisitPad(op),
                "Pow" => VisitBinary(op, BinaryOp.Pow),
                "PRelu" => VisitPRelu(op),

                // "QuantizeLinear" => VisitQuantizeLinear(op),
                "RandomNormal" => VisitRandomNormal(op),
                "RandomNormalLike" => VisitRandomNormalLike(op),
                "RandomUniform" => VisitRandomUniform(op),
                "RandomUniformLike" => VisitRandomUniformLike(op),
                "ReduceL1" => VisitReduceL1(op),
                "ReduceL2" => VisitReduceL2(op),
                "ReduceLogSum" => VisitReduceLogSum(op),
                "ReduceLogSumExp" => VisitReduceLogSumExp(op),
                "ReduceMax" => VisitReduce(op, ReduceOp.Max, float.MinValue),
                "ReduceMean" => VisitReduce(op, ReduceOp.Mean, 0f),
                "ReduceMin" => VisitReduce(op, ReduceOp.Min, float.MaxValue),
                "ReduceProd" => VisitReduce(op, ReduceOp.Prod, 1f),
                "ReduceSum" => VisitReduce(op, ReduceOp.Sum, 0f),
                "ReduceSumSquare" => VisitReduceSumSquare(op),
                "Relu" => VisitRelu(op),
                "Reshape" => VisitReshape(op),

                "Resize" => VisitResize(op),
                "ReverseSequence" => VisitReverseSequence(op),
                "Round" => VisitUnary(op, UnaryOp.Round),
                "Selu" => VisitSelu(op),
                "Shape" => VisitShape(op),
                "Sin" => VisitUnary(op, UnaryOp.Sin),
                "Sinh" => VisitUnary(op, UnaryOp.Sinh),
                "Sigmoid" => VisitSigmoid(op),
                "Sign" => VisitUnary(op, UnaryOp.Sign),
                "Size" => VisitSize(op),
                "Slice" => VisitSlice(op),
                "Softmax" => VisitSoftmax(op),
                "Softplus" => VisitSoftplus(op),
                "Softsign" => VisitSoftsign(op),
                "SpaceToDepth" => VisitSpaceToDepth(op),
                "Split" => VisitSplit(op),
                "Sqrt" => VisitUnary(op, UnaryOp.Sqrt),
                "Squeeze" => VisitSqueeze(op),
                "Sub" => VisitBinary(op, BinaryOp.Sub),
                "Sum" => VisitSum(op),
                "Tanh" => VisitUnary(op, UnaryOp.Tanh),
                "Tile" => VisitTile(op),
                "Transpose" => VisitTranspose(op),

                // "Upsample" => VisitUpsample(op),
                "Unsqueeze" => VisitUnsqueeze(op),
                "Where" => VisitWhere(op),
                _ => throw new NotSupportedException($"Not Supported onnx op {op.OpType}"),
            };

            if (output is Expr expr)
            {
                if (op.Output.Count == 1)
                {
                    _outputTensors.Add(op.Output[0], expr);
                }
                else
                {
                    for (int i = 0; i < op.Output.Count; i++)
                    {
                        _outputTensors.Add(op.Output[i], IR.F.Tensors.GetItem(expr, i));
                    }
                }
            }
            else if (output is IReadOnlyList<Expr> exprList)
            {
                Debug.Assert(op.Output.Count == exprList.Count, $"Op outputs length should be {op.Output.Count}.");
                for (int i = 0; i < op.Output.Count; i++)
                {
                    _outputTensors.Add(op.Output[i], exprList[i]);
                }
            }
            else
            {
                throw new InvalidOperationException("Visit result is not expression(s).");
            }
        }

        private Expr ToNncasePadFormat(Expr pads)
        {
            return Transpose(Reshape(pads, new[] {-1, 2}), new[] {1, 0});
        }
    }
}