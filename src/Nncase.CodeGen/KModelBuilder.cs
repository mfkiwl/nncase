// Copyright (c) Canaan Inc. All rights reserved.
// Licensed under the Apache license. See LICENSE file in the project root for full license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Nncase.IR;
using Nncase.TIR;

namespace Nncase.CodeGen;

/// <summary>
/// the kmodule Serialized result.
/// </summary>
public class KModuleSerializeResult : ISerializeResult
{
    /// <summary>
    /// the module alignment.
    /// </summary>
    public uint Alignment { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="KModuleSerializeResult"/> class.
    /// </summary>
    /// <param name="alignment"></param>
    public KModuleSerializeResult(uint alignment)
    {
        Alignment = alignment;
    }
}

/// <summary>
/// the kmodel Serialized result.
/// </summary>
public class KModelSerializeResult : ISerializeResult
{
    /// <summary>
    /// the model size.
    /// </summary>
    public int ModelSize;

    /// <summary>
    /// ctor.
    /// </summary>
    /// <param name="size"></param>
    public KModelSerializeResult(int size)
    {
        ModelSize = size;
    }
}

/// <summary>
/// the kmodel format runtime model.
/// </summary>
public class RTKModel : IRTModel
{

    /// <inheritdoc/>
    public ITarget Target { get; set; }
    /// <inheritdoc/>
    public IRModel Model { get; set; }
    /// <inheritdoc/>
    public string SourcePath { get; private set; }
    /// <inheritdoc/>
    public byte[] Source
    {
        get
        {
            if (IsSerialized)
                return File.ReadAllBytes(SourcePath);
            throw new InvalidOperationException("Must Serialized Runtime Model Can Get The Source!");
        }
    }
    /// <inheritdoc/>
    public string SourceExt { get => "kmodel"; }
    /// <inheritdoc/>
    public IRTFunction? Entry { get; set; }
    /// <inheritdoc/>
    public IReadOnlyList<IRTModule> Modules => modules;
    /// <inheritdoc/>
    public bool IsSerialized { get; private set; }


    List<IRTModule> modules;

    KModelSerializeResult serializeResult;

    /// <summary>
    /// create the kmodel.
    /// </summary>
    /// <param name="model"></param>
    /// <param name="target"></param>
    public RTKModel(IRModel model, ITarget target)
    {
        Target = target;
        Model = model;
        modules = new();
        SourcePath = CodeGenUtil.GetTempFileName(SourceExt);
        IsSerialized = false;
        serializeResult = new(0);
    }

    /// <inheritdoc/>
    public string Dump(string name, string dumpDirPath)
    {
        var dump_path = Path.Combine(dumpDirPath, $"{name}.{SourceExt}");
        if (IsSerialized)
        {
            if (File.Exists(dump_path))
                File.Delete(dump_path);
            File.Copy(SourcePath, dump_path);
            return dump_path;
        }
        throw new InvalidOperationException("Please Call Serialize First!");
    }

    /// <inheritdoc/>
    public object? Invoke(params object?[]? args)
    {
        throw new NotImplementedException();
    }

    /// <inheritdoc/>
    public ISerializeResult Serialize()
    {
        if (IsSerialized) return serializeResult;

        // step 1. create the kmodel file
        using var ostream = File.Open(SourcePath, FileMode.OpenOrCreate, FileAccess.ReadWrite);
        using var writer = new BinaryWriter(ostream);
        var begin_pos = writer.BaseStream.Position;

        // step 2. start write.
        var header = new ModelHeader()
        {
            Identifier = ModelInfo.IDENTIFIER,
            Version = ModelInfo.VERSION,
            HeaderSize = (uint)Marshal.SizeOf(typeof(ModelHeader)),
            Flags = 0,
            Alignment = 8,
            Modules = (uint)Model.Modules.Count,
        };

        var header_pos = writer.BaseStream.Position;
        writer.Seek(Marshal.SizeOf(typeof(ModelHeader)), SeekOrigin.Current);
        foreach (var module in Model.Modules)
        {
            var rtmodule = Target.CreateRTModule(Model, module);
            var res = (KModuleSerializeResult)rtmodule.Serialize();
            modules.Add(rtmodule);
            writer.Write(rtmodule.Source);
            header.Alignment = Math.Max(header.Alignment, res.Alignment);
        }

        // Entry point
        for (int i = 0; i < Model.Modules.Count; i++)
        {
            var mod = Model.Modules[i];
            for (int j = 0; j < mod.Callables.Count; j++)
            {
                if (object.ReferenceEquals(Model.Entry, mod.Callables[j]))
                {
                    header.EntryModule = (uint)i;
                    header.EntryFunction = (uint)j;
                }
            }
        }

        var end_pos = writer.BaseStream.Position;

        // write header
        writer.Seek((int)header_pos, SeekOrigin.Begin);
        writer.Write(CodeGenUtil.StructToBytes(header));
        writer.Seek((int)end_pos, SeekOrigin.Begin);
        writer.Flush();
        writer.Close();
        serializeResult.ModelSize = ((int)(end_pos - begin_pos));
        IsSerialized = true;
        return serializeResult;
    }
}