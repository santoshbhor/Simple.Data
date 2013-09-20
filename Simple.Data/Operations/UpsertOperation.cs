﻿namespace Simple.Data.Operations
{
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Linq;

    public class UpsertOperation
    {
        private readonly IEnumerable<IReadOnlyDictionary<string, object>> _data;
        private readonly bool _resultRequired;
        private readonly string _tableName;
        private readonly string[] _byFieldNames;
        private readonly ErrorCallback _errorCallback;

        public UpsertOperation(string tableName, IDictionary<string, object> data, bool resultRequired, string[] byFieldNames = null, ErrorCallback errorCallback = null)
            : this(tableName, resultRequired, byFieldNames, errorCallback)
        {
            _data = EnumerableEx.Once(new ReadOnlyDictionary<string, object>(data)); 
        }

        public UpsertOperation(string tableName, IReadOnlyDictionary<string, object> data, bool resultRequired, string[] byFieldNames = null, ErrorCallback errorCallback = null)
            : this(tableName, resultRequired, byFieldNames, errorCallback)
        {
            _data = EnumerableEx.Once(data);
        }

        public UpsertOperation(string tableName, IEnumerable<IDictionary<string, object>> data, bool resultRequired, string[] byFieldNames = null, ErrorCallback errorCallback = null)
            : this(tableName, resultRequired, byFieldNames, errorCallback)
        {
            _data = data.Select(d => new ReadOnlyDictionary<string, object>(d));
        }

        public UpsertOperation(string tableName, bool resultRequired, string[] byFieldNames = null, ErrorCallback errorCallback = null)
        {
            _tableName = tableName;
            _resultRequired = resultRequired;
            _byFieldNames = byFieldNames;
            _errorCallback = errorCallback ?? ((item, exception) => true) ;
        }

        public UpsertOperation(string tableName, IEnumerable<IReadOnlyDictionary<string, object>> data, bool resultRequired, string[] byFieldNames = null, ErrorCallback errorCallback = null)
            : this(tableName, resultRequired, byFieldNames, errorCallback)
        {
            _data = data;
        }

        public bool ResultRequired
        {
            get { return _resultRequired; }
        }

        public string TableName
        {
            get { return _tableName; }
        }

        public IEnumerable<IReadOnlyDictionary<string, object>> Data
        {
            get { return _data; }
        }

        public ErrorCallback ErrorCallback
        {
            get { return _errorCallback; }
        }

        public string[] ByFieldNames
        {
            get { return _byFieldNames; }
        }
    }

    public class UpdateOperation
    {
        private readonly string _tableName;
        private readonly IReadOnlyDictionary<string, object> _data;

        public UpdateOperation(string tableName, IReadOnlyDictionary<string, object> data)
        {
            _tableName = tableName;
            _data = data;
        }

        public string TableName
        {
            get { return _tableName; }
        }

        public IReadOnlyDictionary<string, object> Data
        {
            get { return _data; }
        }
    }
}