﻿// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using Microsoft.ML.Runtime.Data;


namespace Microsoft.ML.Ext.DataManipulation
{
    /// <summary>
    /// Interface for dataframes and dataframe views.
    /// </summary>
    public interface IDataFrameView
    {
        /// <summary>
        /// Returns a copy of the view.
        /// </summary>
        DataFrame Copy();
    }

    /// <summary>
    /// Interface for a data container held by a dataframe.
    /// </summary>
    public interface IDataContainer
    {
        IDataColumn GetColumn(int col);
    }

    /// <summary>
    /// Interface for a column container.
    /// </summary>
    public interface IDataColumn
    {
        /// <summary>
        /// Length of the column
        /// </summary>
        int Length { get; }

        /// <summary>
        /// type of the column 
        /// </summary>
        DataKind Kind { get; }

        /// <summary>
        /// Returns a copy.
        /// </summary>
        IDataColumn Copy();

        /// <summary>
        /// Returns the element at position row
        /// </summary>
        object Get(int row);

        /// <summary>
        /// Updates value at position row
        /// </summary>
        void Set(int row, object value);

        /// <summary>
        /// Updates all values.
        /// </summary>
        void Set(object value);

        /// <summary>
        /// Updates values based on a condition.
        /// </summary>
        void Set(IEnumerable<bool> rows, object value);

        /// <summary>
        /// Updates values based on a condition.
        /// </summary>
        void Set(IEnumerable<int> rows, object value);

        /// <summary>
        /// Updates values based on a condition.
        /// </summary>
        void Set(IEnumerable<bool> rows, IEnumerable<object> values);

        /// <summary>
        /// Updates values based on a condition.
        /// </summary>
        void Set(IEnumerable<int> rows, IEnumerable<object> values);

        /// <summary>
        /// The returned getter returns the element
        /// at position <pre>cursor.Position</pre>
        /// </summary>
        ValueGetter<DType> GetGetter<DType>(IRowCursor cursor);

        /// <summary>
        /// exact comparison
        /// </summary>
        bool Equals(IDataColumn col);

        /// <summary>
        /// Returns an enumerator on every row telling if each of them
        /// verfies the condition.
        /// </summary>
        IEnumerable<bool> Filter<TSource>(Func<TSource, bool> predicate);
    }
}
