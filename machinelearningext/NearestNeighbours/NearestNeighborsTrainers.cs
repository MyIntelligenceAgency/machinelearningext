﻿// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using Microsoft.ML.Runtime;
using Microsoft.ML.Runtime.Data;
using Microsoft.ML.Runtime.Training;

// The following files makes the object visible to maml.
// This way, it can be added to any pipeline.
using LoadableClassAttribute = Microsoft.ML.Runtime.LoadableClassAttribute;
using NearestNeighborsTrainer = Microsoft.ML.Ext.NearestNeighbours.NearestNeighborsTrainer;
using NearestNeighborsBinaryClassificationTrainer = Microsoft.ML.Ext.NearestNeighbours.NearestNeighborsBinaryClassificationTrainer;
using NearestNeighborsMultiClassClassificationTrainer = Microsoft.ML.Ext.NearestNeighbours.NearestNeighborsMultiClassClassificationTrainer;

[assembly: LoadableClass(NearestNeighborsBinaryClassificationTrainer.Summary,
    typeof(NearestNeighborsBinaryClassificationTrainer),
    typeof(NearestNeighborsTrainer.Arguments),
    new[] { typeof(SignatureBinaryClassifierTrainer), typeof(SignatureTrainer) },
    NearestNeighborsBinaryClassificationTrainer.LoaderSignature,
    NearestNeighborsBinaryClassificationTrainer.LongName,
    NearestNeighborsBinaryClassificationTrainer.ShortName)]

[assembly: LoadableClass(NearestNeighborsMultiClassClassificationTrainer.Summary,
    typeof(NearestNeighborsMultiClassClassificationTrainer),
    typeof(NearestNeighborsTrainer.Arguments),
    new[] { typeof(SignatureMultiClassClassifierTrainer), typeof(SignatureTrainer) },
    NearestNeighborsMultiClassClassificationTrainer.LoaderSignature,
    NearestNeighborsMultiClassClassificationTrainer.LongName,
    NearestNeighborsMultiClassClassificationTrainer.ShortName)]


namespace Microsoft.ML.Ext.NearestNeighbours
{
    public class NearestNeighborsBinaryClassificationTrainer : NearestNeighborsTrainer
    {
        public const string LoaderSignature = "NearestNeighborsBC";  // Not more than 24 letters.
        public const string Summary = "k-Nearest Neighbors trainer for Binary Classification";
        public const string RegistrationName = LoaderSignature;
        public const string ShortName = "kNN";
        public const string LongName = "kNNbc";

        public NearestNeighborsBinaryClassificationTrainer(IHostEnvironment env, Arguments args) : base(env, args, LoaderSignature)
        {
        }

        public override void Train(RoleMappedData data)
        {
            data.CheckBinaryLabel();
            base.Train(data);
        }

        protected override INearestNeighborsPredictor CreateTrainedPredictor<TLabel>(KdTree[] kdtrees,
            Dictionary<long, Tuple<TLabel, float>> labelsWeights)
        {
            return NearestNeighborsBinaryClassifierPredictor.Create<TLabel>(Host, kdtrees, labelsWeights,
                                _args.k, _args.algo, _args.weight);
        }
    }

    public class NearestNeighborsMultiClassClassificationTrainer : NearestNeighborsTrainer
    {
        public const string LoaderSignature = "NearestNeighborsMCC";  // Not more than 24 letters.
        public const string Summary = "k-Nearest Neighbors trainer for Multi-Class Classification";
        public const string RegistrationName = LoaderSignature;
        public const string ShortName = "kNNmc";
        public const string LongName = "kNNmcl";

        public NearestNeighborsMultiClassClassificationTrainer(IHostEnvironment env, Arguments args) : base(env, args, LoaderSignature)
        {
        }

        public override void Train(RoleMappedData data)
        {
            int count;
            data.CheckMultiClassLabel(out count);
            base.Train(data);
        }

        protected override INearestNeighborsPredictor CreateTrainedPredictor<TLabel>(KdTree[] kdtrees,
            Dictionary<long, Tuple<TLabel, float>> labelsWeights)
        {
            return NearestNeighborsMultiClassClassifierPredictor.Create<TLabel>(Host, kdtrees, labelsWeights,
                                _args.k, _args.algo, _args.weight);
        }
    }
}