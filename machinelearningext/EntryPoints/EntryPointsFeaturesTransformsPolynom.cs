﻿// See the LICENSE file in the project root for more information.

#pragma warning disable
using System;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json;
using Microsoft.ML;
using Microsoft.ML.Runtime;
using Microsoft.ML.Runtime.Data;
using Microsoft.ML.Runtime.EntryPoints;
using Microsoft.ML.Runtime.CommandLine;

using PolynomialTransform = Scikit.ML.FeaturesTransforms.PolynomialTransform;
using EntryPointPolynomial = Scikit.ML.EntryPoints.EntryPointPolynomial;
using EP_Polynomial = Scikit.ML.EntryPoints.Polynomial;
using Legacy = Microsoft.ML.Legacy;

[assembly: LoadableClass(typeof(void), typeof(EntryPointPolynomial), null,
    typeof(SignatureEntryPointModule), EP_Polynomial.Name)]


namespace Scikit.ML.EntryPoints
{
    #region Definition

    [TlcModule.EntryPointKind(typeof(CommonInputs.ITransformInput))]
    public class PolynomialTransform_ArgumentsEntryPoint : PolynomialTransform.Arguments
    {
        [Argument(ArgumentType.Required, HelpText = "Input dataset",
                  Visibility = ArgumentAttribute.VisibilityType.EntryPointsOnly)]
        public IDataView Data;
    }

    public static class EntryPointPolynomial
    {
        [TlcModule.EntryPoint(Name = EP_Polynomial.Name,
                              Desc = PolynomialTransform.Summary,
                              UserName = EP_Polynomial.Name)]
        public static CommonOutputs.TransformOutput Polynomial(IHostEnvironment env, PolynomialTransform_ArgumentsEntryPoint input)
        {
            Contracts.CheckValue(env, nameof(env));
            env.CheckValue(input, nameof(input));

            var h = EntryPointUtils.CheckArgsAndCreateHost(env, EP_Polynomial.Name, input);
            var view = new PolynomialTransform(h, input, input.Data);
            return new CommonOutputs.TransformOutput()
            {
                Model = new TransformModel(h, view, input.Data),
                OutputData = view
            };
        }
    }
    #endregion

    #region Polynomial

    public static class EntryPointsPolynomialTransformHelper
    {
        public static Polynomial.Output Add(this Microsoft.ML.Runtime.Experiment exp, Polynomial input)
        {
            var output = new Polynomial.Output();
            exp.Add(input, output);
            return output;
        }

        public static void Add(this Microsoft.ML.Runtime.Experiment exp, Polynomial input, Polynomial.Output output)
        {
            exp.AddEntryPoint(EP_Polynomial.Name, input, output);
        }
    }

    #endregion

    #region Entry Point

    /// <summary>
    /// Multiplies features, build polynomial features x1, x1^2, x1x2, x2, x2^2... The output should be cached otherwise the transform will recompute the features each time it is needed. Use CacheTransform.
    /// </summary>
    public sealed partial class Polynomial : Microsoft.ML.Runtime.EntryPoints.CommonInputs.ITransformInput, Legacy.ILearningPipelineItem
    {
        public const string Name = EntryPointsConstants.EntryPointPrefix + nameof(Polynomial);

        public Polynomial()
        {
        }

        public Polynomial(params string[] inputColumnss)
        {
            if (inputColumnss != null)
            {
                foreach (string input in inputColumnss)
                {
                    AddColumns(input);
                }
            }
        }

        public Polynomial(params (string inputColumn, string outputColumn)[] inputOutputColumnss)
        {
            if (inputOutputColumnss != null)
            {
                foreach (var inputOutput in inputOutputColumnss)
                {
                    AddColumns(inputOutput.outputColumn, inputOutput.inputColumn);
                }
            }
        }

        public void AddColumns(string inputColumn)
        {
            var list = Columns == null ? new List<Column1x1>() : new List<Column1x1>(Columns);
            list.Add(OneToOneColumn<Column1x1>.Create(inputColumn));
            Columns = list.ToArray();
        }

        public void AddColumns(string outputColumn, string inputColumn)
        {
            var list = Columns == null ? new List<Column1x1>() : new List<Column1x1>(Columns);
            list.Add(OneToOneColumn<Column1x1>.Create(outputColumn, inputColumn));
            Columns = list.ToArray();
        }

        /// <summary>
        /// Features columns (a vector)
        /// </summary>
        [JsonProperty("columns")]
        public Column1x1[] Columns { get; set; }

        /// <summary>
        /// Highest degree of the polynomial features
        /// </summary>
        [JsonProperty("degree")]
        public int Degree { get; set; } = 2;

        /// <summary>
        /// Number of threads used to estimate allowed by the transform.
        /// </summary>
        [JsonProperty("numThreads")]
        public int? NumThreads { get; set; }

        /// <summary>
        /// Input dataset
        /// </summary>
        public Var<Microsoft.ML.Runtime.Data.IDataView> Data { get; set; } = new Var<Microsoft.ML.Runtime.Data.IDataView>();


        public sealed class Output : Microsoft.ML.Runtime.EntryPoints.CommonOutputs.ITransformOutput
        {
            /// <summary>
            /// Transformed dataset
            /// </summary>
            public Var<Microsoft.ML.Runtime.Data.IDataView> OutputData { get; set; } = new Var<Microsoft.ML.Runtime.Data.IDataView>();

            /// <summary>
            /// Transform model
            /// </summary>
            public Var<Microsoft.ML.Runtime.EntryPoints.ITransformModel> Model { get; set; } = new Var<Microsoft.ML.Runtime.EntryPoints.ITransformModel>();

        }
        public Var<IDataView> GetInputData() => Data;

        public Legacy.ILearningPipelineStep ApplyStep(Legacy.ILearningPipelineStep previousStep, Experiment experiment)
        {
            if (previousStep != null)
            {
                if (!(previousStep is Legacy.ILearningPipelineDataStep dataStep))
                {
                    throw new InvalidOperationException($"{ nameof(Polynomial)} only supports an { nameof(Legacy.ILearningPipelineDataStep)} as an input.");
                }

                Data = dataStep.Data;
            }
            Output output = EntryPointsPolynomialTransformHelper.Add(experiment, this);
            return new PolynomialPipelineStep(output);
        }

        private class PolynomialPipelineStep : Legacy.ILearningPipelineDataStep
        {
            public PolynomialPipelineStep(Output output)
            {
                Data = output.OutputData;
                Model = output.Model;
            }

            public Var<IDataView> Data { get; }
            public Var<ITransformModel> Model { get; }
        }
    }

    #endregion
}

