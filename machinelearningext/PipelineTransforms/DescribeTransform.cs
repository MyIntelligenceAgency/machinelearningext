﻿// See the LICENSE file in the project root for more information.

using System;
using System.Linq;
using System.Collections.Generic;
using Microsoft.ML.Runtime;
using Microsoft.ML.Runtime.CommandLine;
using Microsoft.ML.Runtime.Data;
using Microsoft.ML.Runtime.Model;
using MlFood.ML.PipelineHelper;

// The following files makes the object visible to maml.
// This way, it can be added to any pipeline.
using LoadableClassAttribute = Microsoft.ML.Runtime.LoadableClassAttribute;
using SignatureDataTransform = Microsoft.ML.Runtime.Data.SignatureDataTransform;
using SignatureLoadDataTransform = Microsoft.ML.Runtime.Data.SignatureLoadDataTransform;
using DescribeTransform = MlFood.ML.PipelineTransforms.DescribeTransform;

[assembly: LoadableClass(DescribeTransform.Summary, typeof(DescribeTransform),
    typeof(DescribeTransform.Arguments), typeof(SignatureDataTransform),
    "Describe Transform", DescribeTransform.LoaderSignature, "Describe")]

[assembly: LoadableClass(DescribeTransform.Summary, typeof(DescribeTransform),
    null, typeof(SignatureLoadDataTransform),
    "Describe Transform", DescribeTransform.LoaderSignature, "Describe")]


namespace MlFood.ML.PipelineTransforms
{
    /// <summary>
    /// Compute various statistics on a list of columns.
    /// </summary>
    public class DescribeTransform : IDataTransform
    {
        /// <summary>
        /// A unique signature.
        /// </summary>
        public const string LoaderSignature = "DescribeTransform";  // Not more than 24 letters.
        public const string Summary = "Compute various statistics on a list of columns.";
        public const string RegistrationName = LoaderSignature;

        /// <summary>
        /// Identify the object for dynamic instantiation.
        /// This is also used to track versionning when serializing and deserializing.
        /// </summary>
        static VersionInfo GetVersionInfo()
        {
            return new VersionInfo(
                modelSignature: "DESCTRNS",
                verWrittenCur: 0x00010001,
                verReadableCur: 0x00010001,
                verWeCanReadBack: 0x00010001,
                loaderSignature: LoaderSignature);
        }

        /// <summary>
        /// Parameters which defines the transform.
        /// </summary>
        public class Arguments
        {
            [Argument(ArgumentType.MultipleUnique, HelpText = "Columns to describe (min, max, mean, ...).", ShortName = "col")]
            public string[] columns;

            [Argument(ArgumentType.MultipleUnique, HelpText = "Compute an histogram for this column. Limited to 100 values.", ShortName = "hist")]
            public string[] hists;

            [Argument(ArgumentType.AtMostOnce, HelpText = "Saves the statistics in a file.", ShortName = "dout")]
            public string saveInFile;

            [Argument(ArgumentType.AtMostOnce, HelpText = "The output view is the input (true) or the statistics (false).", ShortName = "p")]
            public bool passThrough = true;

            [Argument(ArgumentType.AtMostOnce, HelpText = "To show the entier schema", ShortName = "sch")]
            public bool showSchema = false;

            [Argument(ArgumentType.AtMostOnce, HelpText = "To show the dimension of the problem (number of rows)." +
                "If true, the transform might fail applied after a ResampleTransform.", ShortName = "dim")]
            public bool dimension = false;

            [Argument(ArgumentType.AtMostOnce, HelpText = "Use one row per column instead of one row per statistics/column", ShortName = "one")]
            public bool oneRowPerColumn = false;

            [Argument(ArgumentType.AtMostOnce, HelpText = "Display in JSON format", ShortName = "json")]
            public bool jsonFormat = false;

            [Argument(ArgumentType.AtMostOnce, HelpText = "If not null, every display will start by <name> and end by </name>")]
            public string name = "desc";

            public void Write(ModelSaveContext ctx, IHost host)
            {
                ctx.Writer.Write(columns == null ? string.Empty : string.Join(",", columns));
                ctx.Writer.Write(hists == null ? string.Empty : string.Join(",", hists));
                ctx.Writer.Write(saveInFile == null ? string.Empty : saveInFile);
                ctx.Writer.Write(passThrough ? 1 : 0);
                ctx.Writer.Write(showSchema ? 1 : 0);
                ctx.Writer.Write(dimension ? 1 : 0);
                ctx.Writer.Write(oneRowPerColumn ? 1 : 0);
                ctx.Writer.Write(jsonFormat ? 1 : 0);
                ctx.Writer.Write(name);
            }

            public void Read(ModelLoadContext ctx, IHost host)
            {
                string sr = ctx.Reader.ReadString();
                host.CheckValue(sr, "columns");
                columns = sr.Split(',').Where(c => !string.IsNullOrEmpty(c)).ToArray();
                if (columns.Length == 0)
                    columns = null;

                sr = ctx.Reader.ReadString();
                host.CheckValue(sr, "hists");
                hists = sr.Split(',').Where(c => !string.IsNullOrEmpty(c)).ToArray();
                if (hists.Length == 0)
                    hists = null;

                saveInFile = ctx.Reader.ReadString();
                int nb = ctx.Reader.ReadInt32();
                host.Check(nb == 0 || nb == 1, "passThrough");
                passThrough = nb == 1;
                showSchema = ctx.Reader.ReadInt32() == 1;
                dimension = ctx.Reader.ReadInt32() == 1;
                oneRowPerColumn = ctx.Reader.ReadInt32() == 1;
                jsonFormat = ctx.Reader.ReadInt32() == 1;
                name = ctx.Reader.ReadString();
            }

            public void PostProcess()
            {
                if (hists != null && hists.Length == 1 && hists[0].Contains(","))
                    hists = hists[0].Split(',');
                if (columns != null && columns.Length == 1 && columns[0].Contains(","))
                    columns = columns[0].Split(',');
            }
        }

        IDataView _input;
        IDataView _statistics;
        Arguments _args;
        IHost _host;
        object _lock;

        public IDataView Source { get { return _input; } }

        public DescribeTransform(IHostEnvironment env, Arguments args, IDataView input)
        {
            Contracts.CheckValue(env, "env");
            _host = env.Register(LoaderSignature);
            _host.CheckValue(args, "args");
            args.PostProcess();
            _host.CheckValue(args.columns, "columns");

            if (!args.passThrough)
                throw _host.ExceptNotImpl("passThrough=+ not yet implemented.");

            _input = input;
            _args = args;
            _lock = new object();
            _statistics = null;
        }

        public static DescribeTransform Create(IHostEnvironment env, ModelLoadContext ctx, IDataView input)
        {
            Contracts.CheckValue(env, "env");
            var h = env.Register(LoaderSignature);
            h.CheckValue(ctx, "ctx");
            h.CheckValue(input, "input");
            ctx.CheckAtModel(GetVersionInfo());
            return h.Apply("Loading Model", ch => new DescribeTransform(h, ctx, input));
        }

        public void Save(ModelSaveContext ctx)
        {
            _host.CheckValue(ctx, "ctx");
            ctx.CheckAtModel();
            ctx.SetVersionInfo(GetVersionInfo());
            _args.Write(ctx, _host);
        }

        private DescribeTransform(IHost host, ModelLoadContext ctx, IDataView input)
        {
            Contracts.CheckValue(host, "host");
            Contracts.CheckValue(input, "input");
            _host = host;
            _input = input;
            _host.CheckValue(input, "input");
            _host.CheckValue(ctx, "ctx");
            _args = new Arguments();
            _args.Read(ctx, _host);
            _statistics = null;
            _lock = new object();
        }

        public ISchema Schema { get { return _input.Schema; } }

        public bool CanShuffle { get { return _input.CanShuffle; } }

        /// <summary>
        /// Same as the input data view.
        /// </summary>
        public long? GetRowCount(bool lazy = true)
        {
            _host.AssertValue(Source, "_input");
            return Source.GetRowCount(lazy);
        }

        public IRowCursor GetRowCursor(Func<int, bool> predicate, IRandom rand = null)
        {
            ComputeStatistics();
            _host.AssertValue(_input, "_input");
            return _input.GetRowCursor(predicate, rand);
        }

        public IRowCursor[] GetRowCursorSet(out IRowCursorConsolidator consolidator, Func<int, bool> predicate, int n, IRandom rand = null)
        {
            ComputeStatistics();
            _host.AssertValue(_input, "_input");
            return _input.GetRowCursorSet(out consolidator, predicate, n, rand);
        }

        private void ComputeStatistics()
        {
            lock (_lock)
            {
                if (_statistics == null)
                {
                    var stats = new Dictionary<string, List<ColumnStatObs>>();

                    using (var ch = _host.Start("Computing statistics"))
                    {
                        if (!_args.jsonFormat)
                            ch.Info("Begin DescribeTransform {0}", _args.name);

                        if (_args.showSchema)
                        {
                            if (_args.jsonFormat)
                                ch.Info("    <{0}>{{\"Schema\":\"{1}\"}},</{0}>", _args.name, SchemaHelper.ToString(_input.Schema));
                            else
                                ch.Info("    <{0}>Schema: {1}</{0}>", _args.name, SchemaHelper.ToString(_input.Schema));
                        }

                        if (_args.dimension)
                        {
                            var nbRows = DataViewHelper.ComputeRowCount(_input);
                            if (_args.jsonFormat)
                                ch.Info("    <{0}>{{\"NbRows\":\"{1}\"}},</{0}>", _args.name, nbRows);
                            else
                                ch.Info("    <{0}>NbRows: {1}</{0}>", _args.name, nbRows);
                        }

                        var sch = _input.Schema;
                        var indexesCol = new List<int>();
                        var textCols = new List<string>();
                        if (_args.columns != null)
                            textCols.AddRange(_args.columns);
                        if (_args.hists != null)
                            textCols.AddRange(_args.hists);

                        for (int i = 0; i < textCols.Count; ++i)
                        {
                            int index;
                            if (!sch.TryGetColumnIndex(textCols[i], out index))
                                throw ch.Except("Unable to find column '{0}' in '{1}'", textCols[i], SchemaHelper.ToString(sch));
                            var ty = sch.GetColumnType(index);
                            if (!(ty == NumberType.R4 || ty == NumberType.U4 ||
                                ty == NumberType.I4 || ty == TextType.Instance ||
                                ty == BoolType.Instance || ty == NumberType.I8 ||
                                (ty.IsKey && ty.AsKey.RawKind == DataKind.U4) ||
                                (ty.IsVector && ty.AsVector.ItemType == NumberType.R4)))
                                throw ch.Except("Unsupported type {0} (schema={1}).", _args.columns[i], SchemaHelper.ToString(sch));
                            indexesCol.Add(index);
                        }

                        // Computation
                        var required = new HashSet<int>(indexesCol);
                        var requiredIndexes = required.OrderBy(c => c).ToArray();
                        using (var cur = _input.GetRowCursor(i => required.Contains(i)))
                        {
                            bool[] isText = requiredIndexes.Select(c => sch.GetColumnType(c) == TextType.Instance).ToArray();
                            bool[] isBool = requiredIndexes.Select(c => sch.GetColumnType(c) == BoolType.Instance).ToArray();
                            bool[] isFloat = requiredIndexes.Select(c => sch.GetColumnType(c) == NumberType.R4).ToArray();
                            bool[] isUint = requiredIndexes.Select(c => sch.GetColumnType(c) == NumberType.U4 || sch.GetColumnType(c).RawKind == DataKind.U4).ToArray();
                            bool[] isInt = requiredIndexes.Select(c => sch.GetColumnType(c) == NumberType.I4 || sch.GetColumnType(c).RawKind == DataKind.I4).ToArray();
                            bool[] isInt8 = requiredIndexes.Select(c => sch.GetColumnType(c) == NumberType.I8 || sch.GetColumnType(c).RawKind == DataKind.I8).ToArray();

                            ValueGetter<DvBool>[] boolGetters = requiredIndexes.Select(i => sch.GetColumnType(i) == BoolType.Instance || sch.GetColumnType(i).RawKind == DataKind.BL ? cur.GetGetter<DvBool>(i) : null).ToArray();
                            ValueGetter<uint>[] uintGetters = requiredIndexes.Select(i => sch.GetColumnType(i) == NumberType.U4 || sch.GetColumnType(i).RawKind == DataKind.U4 ? cur.GetGetter<uint>(i) : null).ToArray();
                            ValueGetter<DvText>[] textGetters = requiredIndexes.Select(i => sch.GetColumnType(i) == TextType.Instance ? cur.GetGetter<DvText>(i) : null).ToArray();
                            ValueGetter<float>[] floatGetters = requiredIndexes.Select(i => sch.GetColumnType(i) == NumberType.R4 ? cur.GetGetter<float>(i) : null).ToArray();
                            ValueGetter<VBuffer<float>>[] vectorGetters = requiredIndexes.Select(i => sch.GetColumnType(i).IsVector ? cur.GetGetter<VBuffer<float>>(i) : null).ToArray();
                            ValueGetter<DvInt4>[] intGetters = requiredIndexes.Select(i => sch.GetColumnType(i) == NumberType.I4 || sch.GetColumnType(i).RawKind == DataKind.I4 ? cur.GetGetter<DvInt4>(i) : null).ToArray();
                            ValueGetter<DvInt8>[] int8Getters = requiredIndexes.Select(i => sch.GetColumnType(i) == NumberType.I8 || sch.GetColumnType(i).RawKind == DataKind.I8 ? cur.GetGetter<DvInt8>(i) : null).ToArray();

                            var cols = _args.columns == null ? null : new HashSet<string>(_args.columns);
                            var hists = _args.hists == null ? null : new HashSet<string>(_args.hists);

                            for (int i = 0; i < _input.Schema.ColumnCount; ++i)
                            {
                                string name = _input.Schema.GetColumnName(i);
                                if (!required.Contains(i))
                                    continue;
                                stats[name] = new List<ColumnStatObs>();
                                var t = stats[name];
                                if (cols != null && cols.Contains(name))
                                {
                                    t.Add(new ColumnStatObs(ColumnStatObs.StatKind.min));
                                    t.Add(new ColumnStatObs(ColumnStatObs.StatKind.max));
                                    t.Add(new ColumnStatObs(ColumnStatObs.StatKind.sum));
                                    t.Add(new ColumnStatObs(ColumnStatObs.StatKind.sum2));
                                    t.Add(new ColumnStatObs(ColumnStatObs.StatKind.nb));
                                }
                                if (hists != null && hists.Contains(name))
                                    t.Add(new ColumnStatObs(ColumnStatObs.StatKind.hist));
                            }

                            float value = 0;
                            var tvalue = new DvText();
                            var vector = new VBuffer<float>();
                            uint uvalue = 0;
                            var bvalue = DvBool.True;
                            var int4 = new DvInt4();
                            var int8 = new DvInt8();

                            while (cur.MoveNext())
                            {
                                for (int i = 0; i < requiredIndexes.Length; ++i)
                                {
                                    string name = cur.Schema.GetColumnName(requiredIndexes[i]);
                                    if (!stats.ContainsKey(name))
                                        continue;
                                    if (isFloat[i])
                                    {
                                        floatGetters[i](ref value);
                                        foreach (var t in stats[name])
                                            t.Update(value);
                                    }
                                    else if (isBool[i])
                                    {
                                        boolGetters[i](ref bvalue);
                                        foreach (var t in stats[name])
                                            t.Update(bvalue);
                                    }
                                    else if (isText[i])
                                    {
                                        textGetters[i](ref tvalue);
                                        foreach (var t in stats[name])
                                            t.Update(tvalue.ToString());
                                    }
                                    else if (isUint[i])
                                    {
                                        uintGetters[i](ref uvalue);
                                        foreach (var t in stats[name])
                                            t.Update(uvalue);
                                    }
                                    else if (isInt[i])
                                    {
                                        intGetters[i](ref int4);
                                        foreach (var t in stats[name])
                                            t.Update((double)int4);
                                    }
                                    else if (isInt8[i])
                                    {
                                        int8Getters[i](ref int8);
                                        foreach (var t in stats[name])
                                            t.Update((double)int8);
                                    }
                                    else
                                    {
                                        vectorGetters[i](ref vector);
                                        foreach (var t in stats[name])
                                            t.Update(vector);
                                    }
                                }
                            }
                        }

                        if (_args.oneRowPerColumn || _args.jsonFormat)
                        {
                            var rows = new List<string>();
                            rows.Add(string.Format("<{0}>{1}", _args.name, _args.jsonFormat ? "[" : ""));
                            foreach (var col in stats.OrderBy(c => c.Key))
                                if (_args.jsonFormat)
                                    rows.Add(string.Format("    {{\"Column\": \"{0}\", \"stat\": {1}}},", col.Key,
                                        string.Join(", ", col.Value.Select(c => c.ToString(true)))));
                                else
                                    rows.Add(string.Format("    <{2}>Column '{0}': {1}</{2}>", col.Key,
                                        string.Join(", ", col.Value.Select(c => c.ToString(false))), _args.name));
                            rows.Add(string.Format("{1}</{0}>", _args.name, _args.jsonFormat ? "]" : ""));
                            ch.Info(string.Join("\n", rows));
                        }
                        else
                        {
                            var rows = new List<string>();
                            foreach (var col in stats.OrderBy(c => c.Key))
                            {
                                rows.Add(string.Format("     [{1}] Column '{0}'", col.Key, _args.name));
                                foreach (var st in col.Value)
                                    rows.Add(string.Format("        {0}", st.ToString(false)));
                            }
                            ch.Info(string.Join("\n", rows));
                        }

                        // Save
                        if (!string.IsNullOrEmpty(_args.saveInFile))
                            throw ch.ExceptNotImpl("Unable to save into \"{0}\"", _args.saveInFile);

                        if (!_args.jsonFormat)
                            ch.Info("End DescribeTransform {0}", _args.name);

                        ch.Done();
                    }
                    _statistics = _input;
                }
            }
        }
    }
}
