﻿// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.ML.Runtime;
using Microsoft.ML.Runtime.Api;
using Microsoft.ML.Runtime.Data;
using Scikit.ML.PipelineHelper;


namespace Scikit.ML.ProductionPrediction
{
    #region replace 1 column

    /// <summary>
    /// Creates a view on one row/column.
    /// </summary>
    public class TemporaryViewCursorColumn<TRepValue> : IDataView
    {
        readonly int _column;
        readonly TRepValue _value;
        readonly Schema _schema;
        readonly IRowCursor _otherValues;
        readonly bool _ignoreOtherColumn;

        public TRepValue Constant => _value;
        public int ConstantCol => _column;

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="value">value which will replace the value</param>
        /// <param name="column">column to be replaced</param>
        /// <param name="otherValues">cursor which contains the others values</param>
        /// <param name="schema">schema to replace if otherValues is null</param>
        public TemporaryViewCursorColumn(TRepValue value, int column, Schema schema = null, IRowCursor otherValues = null, bool ignoreOtherColumn = false)
        {
            _column = column;
            _otherValues = otherValues;
            _schema = otherValues == null ? schema : otherValues.Schema;
            _value = value;
            _ignoreOtherColumn = ignoreOtherColumn;
            Contracts.AssertValue(_schema);
        }

        public bool CanShuffle { get { return false; } }
        public long? GetRowCount() { return null; }
        public Schema Schema { get { return _schema; } }

        public IRowCursor GetRowCursor(Func<int, bool> needCol, IRandom rand = null)
        {
            return new CursorType(this, needCol, _otherValues);
        }

        public IRowCursor[] GetRowCursorSet(out IRowCursorConsolidator consolidator, Func<int, bool> needCol, int n, IRandom rand = null)
        {
            var cur = GetRowCursor(needCol, rand);
            consolidator = new Consolidator();
            if (n >= 2)
            {
                // This trick avoids the cursor to be split into multiple later.
                var res = new IRowCursor[n];
                var empty = new EmptyCursor(this,
                                    col => col == _column || needCol(col) ||
                                                  (_otherValues != null && _otherValues.IsColumnActive(col)));
                for (int i = 0; i < n; ++i)
                    res[i] = i == 0 ? cur : empty;
                return res;
            }
            else
                return new IRowCursor[] { cur };
        }

        class Consolidator : IRowCursorConsolidator
        {
            public IRowCursor CreateCursor(IChannelProvider provider, IRowCursor[] inputs)
            {
                return inputs[0];
            }
        }

        class CursorType : IRowCursor
        {
            Func<int, bool> _needCol;
            TemporaryViewCursorColumn<TRepValue> _view;
            CursorState _state;
            IRowCursor _otherValues;
            bool _ignoreOtherColumn;

            public CursorType(TemporaryViewCursorColumn<TRepValue> view, Func<int, bool> needCol, IRowCursor otherValues)
            {
                _needCol = needCol;
                _view = view;
                _state = CursorState.NotStarted;
                _otherValues = otherValues;
                _ignoreOtherColumn = view._ignoreOtherColumn;
            }

            public CursorState State { get { return _state; } }
            public ICursor GetRootCursor() { return this; }
            public long Batch { get { return 1; } }
            public long Position { get { return 0; } }
            public Schema Schema { get { return _view.Schema; } }
            public ValueGetter<UInt128> GetIdGetter() { return (ref UInt128 uid) => { uid = new UInt128(0, 1); }; }

            void IDisposable.Dispose()
            {
                if (_otherValues != null)
                {
                    // Do not dispose the cursor. The current one does not call MoveNext,
                    // it does not own any cursor and should free any of them.
                    // _otherValues.Dispose();
                    _otherValues = null;
                }
                GC.SuppressFinalize(this);
            }

            public bool MoveMany(long count)
            {
                throw Contracts.ExceptNotSupp();
            }

            public bool MoveNext()
            {
                if (State == CursorState.NotStarted)
                {
                    _state = CursorState.Good;
                    return true;
                }
                else if (State == CursorState.Good)
                {
                    _state = CursorState.Done;
                    return false;
                }
                return false;
            }

            public bool IsColumnActive(int col)
            {
                return col == _view._column || _needCol(col) || (_otherValues != null && _otherValues.IsColumnActive(col));
            }

            public ValueGetter<TValue> GetGetter<TValue>(int col)
            {
                if (col == _view.ConstantCol)
                    return GetGetterPrivate(col) as ValueGetter<TValue>;
                else if (_otherValues != null)
                    return _otherValues.GetGetter<TValue>(col);
                else if (_ignoreOtherColumn)
                    return (ref TValue value) =>
                    {
                        value = default(TValue);
                    };
                else
                    throw Contracts.Except("otherValues is null, unable to access other columns.");
            }

            private ValueGetter<TRepValue> GetGetterPrivate(int col)
            {
                if (col == _view.ConstantCol)
                {
                    var type = _view.Schema.GetColumnType(col);
                    if (type.IsVector())
                    {
                        switch (type.AsVector().ItemType().RawKind())
                        {
                            case DataKind.R4:
                                return GetGetterPrivateVector<float>(col) as ValueGetter<TRepValue>;
                            default:
                                throw Contracts.ExceptNotSupp("Unable to get a getter for type {0}", type.ToString());
                        }
                    }
                    else
                    {
                        return (ref TRepValue value) =>
                        {
                            value = _view.Constant;
                        };
                    }
                }
                else
                    throw Contracts.ExceptNotSupp();
            }

            private ValueGetter<VBuffer<TRepValueItem>> GetGetterPrivateVector<TRepValueItem>(int col)
            {
                if (col == _view.ConstantCol)
                {
                    VBuffer<TRepValueItem> cast = (VBuffer<TRepValueItem>)(object)_view.Constant;
                    return (ref VBuffer<TRepValueItem> value) =>
                    {
                        cast.CopyTo(ref value);
                    };
                }
                else
                    throw Contracts.ExceptNotSupp("Unable to create a vector getter.");
            }
        }
    }

    #endregion

    #region multiple columns

    /// <summary>
    /// Creates a view on one row/column.
    /// </summary>
    public class TemporaryViewCursorRow<TRowValue> : IDataView
        where TRowValue : class
    {
        readonly int[] _columns;
        TRowValue _value;
        readonly Schema _schema;
        readonly IRowCursor _otherValues;
        readonly SchemaDefinition _columnsSchema;
        readonly Dictionary<string, Delegate> _overwriteRowGetter;
        public TRowValue Constant => _value;
        public int[] ConstantCol => _columns;

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="value">value which will replace the value</param>
        /// <param name="column">column to be replaced</param>
        /// <param name="otherValues">cursor which contains the others values</param>
        /// <param name="schema">schema to replace if otherValues is null</param>
        public TemporaryViewCursorRow(TRowValue value, int[] columns = null, Schema schema = null, IRowCursor otherValues = null,
                                         Dictionary<string, Delegate> overwriteRowGetter = null)
        {
            var columnsSchema = SchemaDefinition.Create(typeof(TRowValue), SchemaDefinition.Direction.Read);
            if (columns == null)
                columns = columnsSchema.Select((c, i) => i).ToArray();
            if (columns.Length != columnsSchema.Count)
                throw Contracts.Except($"Dimension mismatch expected columns is {columns.Length}, number of fields for {typeof(TRowValue)} is {columnsSchema.Count}.");
            _columns = columns;
            _otherValues = otherValues;
            _schema = otherValues == null ? schema : otherValues.Schema;
            _value = value;
            _columnsSchema = columnsSchema;
            _overwriteRowGetter = overwriteRowGetter;
            Contracts.AssertValue(_schema);
        }

        public bool CanShuffle { get { return false; } }
        public long? GetRowCount() { return null; }
        public Schema Schema { get { return _schema; } }

        public IRowCursor GetRowCursor(Func<int, bool> needCol, IRandom rand = null)
        {
            return new CursorType(this, needCol, _otherValues);
        }

        public IRowCursor[] GetRowCursorSet(out IRowCursorConsolidator consolidator, Func<int, bool> needCol, int n, IRandom rand = null)
        {
            var cur = GetRowCursor(needCol, rand);
            consolidator = new Consolidator();
            if (n >= 2)
            {
                // This trick avoids the cursor to be split into multiple later.
                var setColumns = new HashSet<int>(_columns);
                var res = new IRowCursor[n];
                var empty = new EmptyCursor(this,
                                    col => setColumns.Contains(col) || needCol(col) || (_otherValues != null && _otherValues.IsColumnActive(col)));
                for (int i = 0; i < n; ++i)
                    res[i] = i == 0 ? cur : empty;
                return res;
            }
            else
                return new IRowCursor[] { cur };
        }

        class Consolidator : IRowCursorConsolidator
        {
            public IRowCursor CreateCursor(IChannelProvider provider, IRowCursor[] inputs)
            {
                return inputs[0];
            }
        }

        class CursorType : IRowCursor
        {
            Func<int, bool> _needCol;
            TemporaryViewCursorRow<TRowValue> _view;
            SchemaDefinition _columnsSchema;
            CursorState _state;
            IRowCursor _otherValues;
            Dictionary<int, int> _columns;
            Dictionary<string, Delegate> _overwriteRowGetter;

            public CursorType(TemporaryViewCursorRow<TRowValue> view, Func<int, bool> needCol, IRowCursor otherValues)
            {
                _needCol = needCol;
                _view = view;
                _state = CursorState.NotStarted;
                _otherValues = otherValues;
                _columnsSchema = _view._columnsSchema;
                _overwriteRowGetter = _view._overwriteRowGetter;
                _columns = new Dictionary<int, int>();
                for (int i = 0; i < view.ConstantCol.Length; ++i)
                    _columns[view.ConstantCol[i]] = i;
            }

            public CursorState State { get { return _state; } }
            public ICursor GetRootCursor() { return this; }
            public long Batch { get { return 1; } }
            public long Position { get { return 0; } }
            public Schema Schema { get { return _view.Schema; } }
            public ValueGetter<UInt128> GetIdGetter() { return (ref UInt128 uid) => { uid = new UInt128(0, 1); }; }

            void IDisposable.Dispose()
            {
                if (_otherValues != null)
                {
                    // Do not dispose the cursor. The current one does not call MoveNext,
                    // it does not own any cursor and should free any of them.
                    // _otherValues.Dispose();
                    _otherValues = null;
                }
                GC.SuppressFinalize(this);
            }

            public bool MoveMany(long count)
            {
                throw Contracts.ExceptNotSupp();
            }

            public bool MoveNext()
            {
                if (State == CursorState.NotStarted)
                {
                    _state = CursorState.Good;
                    return true;
                }
                else if (State == CursorState.Good)
                {
                    _state = CursorState.Done;
                    return false;
                }
                return false;
            }

            public bool IsColumnActive(int col)
            {
                return _columns.ContainsKey(col) || _needCol(col) || (_otherValues != null && _otherValues.IsColumnActive(col));
            }

            /// <summary>
            /// Switches between the replaced column or the values coming
            /// from a view which has a column to be replaced,
            /// or the entire row (ConstantCol == -1, _otherValues).
            /// </summary>
            /// <typeparam name="TValue">column type</typeparam>
            /// <param name="col">column inde</param>
            /// <returns>ValueGetter</returns>
            public ValueGetter<TValue> GetGetter<TValue>(int col)
            {
                if (_columns.ContainsKey(col))
                    return GetGetterPrivate<TValue>(col) as ValueGetter<TValue>;
                else if (_otherValues != null)
                    return _otherValues.GetGetter<TValue>(col);
                else
                    throw Contracts.Except("otherValues is null, unable to access other columns." +
                        "If you are using PrePostTransformPredictor, it means the preprossing transform cannot be " +
                        "converted into a IValueMapper because it relies on more than one columns.");
            }

            public ValueGetter<TValue> GetGetterPrivate<TValue>(int col)
            {
                int rowColumns = _columns[col];
                var name = _columnsSchema[rowColumns].ColumnName;
                if (_overwriteRowGetter.ContainsKey(name))
                {
                    var getter = _overwriteRowGetter[name] as ValueGetterInstance<TRowValue, TValue>;
                    if (getter == null)
                        throw Contracts.Except($"Irreconcilable types {_overwriteRowGetter[name].GetType()} != {typeof(ValueGetterInstance<TRowValue, TValue>)}.");
                    return (ref TValue value) =>
                    {
                        getter(ref _view._value, ref value);
                    };
                }
                else
                {
                    var prop = typeof(TRowValue).GetProperty(name);
                    var getMethod = prop.GetGetMethod();
                    if (getMethod == null)
                        throw Contracts.Except($"GetMethod returns null for type {typeof(TRowValue)} and member '{name}'");
                    throw Contracts.ExceptNotSupp($"Getter must be specified.");
                }
            }
        }
    }

    #endregion
}
