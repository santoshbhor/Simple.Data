﻿namespace Simple.Data.Ado
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel.Composition;
    using System.Data;
    using System.Linq;
    using System.Threading.Tasks;
    using Extensions;
    using Operations;
    using Schema;

    [Export("Ado", typeof (Adapter))]
    public partial class AdoAdapter : Adapter, ICloneable
    {
        private readonly ICommandExecutor _commandExecutor;
        private readonly ExecutorFactory _executorFactory = new ExecutorFactory();
        private readonly AdoAdapterFinder _finder;
        private readonly ProviderHelper _providerHelper = new ProviderHelper();
        private CommandOptimizer _commandOptimizer = new CommandOptimizer();
        private Func<IDbConnection, IDbConnection> _connectionModifier = connection => connection;

        private IConnectionProvider _connectionProvider;
        private Lazy<AdoAdapterRelatedFinder> _relatedFinder;
        private DatabaseSchema _schema;
        private IDbConnection _sharedConnection;

        public AdoAdapter()
        {
            _finder = new AdoAdapterFinder(this);
        }

        internal AdoAdapter(IConnectionProvider connectionProvider) : this()
        {
            _connectionProvider = connectionProvider;
            _schema = DatabaseSchema.Get(_connectionProvider, _providerHelper);
            _relatedFinder = new Lazy<AdoAdapterRelatedFinder>(CreateRelatedFinder);
            _commandOptimizer = ProviderHelper.GetCustomProvider<CommandOptimizer>(_connectionProvider) ??
                                new CommandOptimizer();
            _commandExecutor = ProviderHelper.GetCustomProvider<ICommandExecutor>(_connectionProvider) ?? new DefaultCommandExecutor();
        }

        private AdoAdapter(IConnectionProvider connectionProvider, AdoAdapterFinder finder, ProviderHelper providerHelper,
            Lazy<AdoAdapterRelatedFinder> relatedFinder, DatabaseSchema schema)
        {
            _connectionProvider = connectionProvider;
            _finder = finder;
            _providerHelper = providerHelper;
            _relatedFinder = relatedFinder;
            _schema = schema;
        }

        public ICommandExecutor CommandExecutor
        {
            get { return _commandExecutor; }
        }

        public AdoOptions AdoOptions
        {
            get { return Options as AdoOptions; }
        }

        public CommandOptimizer CommandOptimizer
        {
            get { return _commandOptimizer; }
        }

        public ProviderHelper ProviderHelper
        {
            get { return _providerHelper; }
        }

        public IConnectionProvider ConnectionProvider
        {
            get { return _connectionProvider; }
        }

        internal AdoAdapterFinder Finder
        {
            get { return _finder; }
        }

        public bool ProviderSupportsCompoundStatements
        {
            get { return _connectionProvider.SupportsCompoundStatements; }
        }

        public ISchemaProvider SchemaProvider
        {
            get { return _connectionProvider.GetSchemaProvider(); }
        }

        public override IEqualityComparer<string> KeyComparer
        {
            get { return HomogenizedEqualityComparer.DefaultInstance; }
        }

        public Task<OperationResult> Execute(IOperation operation, IAdapterTransaction transaction)
        {
            if (operation == null) throw new ArgumentNullException("operation");

            Func<IOperation, AdoAdapter, AdoAdapterTransaction, Task<OperationResult>> func;
            if (_executorFactory.TryGet(operation, out func))
            {
                return func(operation, this, transaction as AdoAdapterTransaction);
            }

            throw new NotSupportedException(string.Format("Operation '{0}' is not supported by the current database.", operation.GetType().Name));
        }

        public override IReadOnlyDictionary<string, object> GetKey(string tableName, IReadOnlyDictionary<string, object> record)
        {
            IDictionary<string, object> homogenizedRecord = record.ToDictionary(HomogenizedEqualityComparer.DefaultInstance);
            List<string> keyNames = GetKeyNames(tableName).Select(k => k.Homogenize()).ToList();
            return keyNames
                .Where(homogenizedRecord.ContainsKey)
                .ToDictionary(key => key, key => homogenizedRecord[key]);
        }

        protected override void OnSetup()
        {
            ICollection<string> settingsKeys = ((IDictionary<string, object>) Settings).Keys;
            if (settingsKeys.Contains("ConnectionString"))
            {
                if (settingsKeys.Contains("ProviderName"))
                {
                    _connectionProvider = ProviderHelper.GetProviderByConnectionString(Settings.ConnectionString,
                        Settings.ProviderName);
                }
                else
                {
                    _connectionProvider = ProviderHelper.GetProviderByConnectionString(Settings.ConnectionString);
                }
            }
            else if (settingsKeys.Contains("Filename"))
            {
                _connectionProvider = ProviderHelper.GetProviderByFilename(Settings.Filename);
            }
            else if (settingsKeys.Contains("ConnectionName"))
            {
                _connectionProvider = ProviderHelper.GetProviderByConnectionName(Settings.ConnectionName);
            }
            _schema = DatabaseSchema.Get(_connectionProvider, _providerHelper);
            _relatedFinder = new Lazy<AdoAdapterRelatedFinder>(CreateRelatedFinder);
            _commandOptimizer = ProviderHelper.GetCustomProvider<CommandOptimizer>(_connectionProvider) ??
                                new CommandOptimizer();
        }

        private AdoAdapterRelatedFinder CreateRelatedFinder()
        {
            return new AdoAdapterRelatedFinder(this);
        }

        public override Task<OperationResult> Execute(IOperation operation)
        {
            return Execute(operation, null);
        }

        public OperationResult Execute(FunctionOperation operation)
        {
            throw new NotImplementedException();
        }

        private IDictionary<string, object> Get(GetOperation operation)
        {
            // We don't need to implement Get because we provide a delegate for this operation...
            throw new NotImplementedException();
        }

        public override bool IsExpressionFunction(string functionName, params object[] args)
        {
            return FunctionIsLikeOrNotLike(functionName, args);
        }

        private static bool FunctionIsLikeOrNotLike(string functionName, object[] args)
        {
            return ((functionName.Equals("like", StringComparison.OrdinalIgnoreCase)
                     || functionName.Equals("notlike", StringComparison.OrdinalIgnoreCase))
                    && args.Length == 1
                    && args[0] is string);
        }

        private Task<IDictionary<string, object>> Insert(string tableName, IDictionary<string, object> data, bool resultRequired)
        {
            return new AdoAdapterInserter(this).Insert(tableName, data, resultRequired);
        }

        private int UpdateMany(string tableName, IEnumerable<IReadOnlyDictionary<string, object>> data,
            IEnumerable<string> criteriaFieldNames)
        {
            IBulkUpdater bulkUpdater = ProviderHelper.GetCustomProvider<IBulkUpdater>(ConnectionProvider) ??
                                       new BulkUpdater();
            return bulkUpdater.Update(this, tableName, data.ToList(), criteriaFieldNames, null);
        }

        private int UpdateMany(string tableName, IEnumerable<IReadOnlyDictionary<string, object>> data)
        {
            IBulkUpdater bulkUpdater = ProviderHelper.GetCustomProvider<IBulkUpdater>(ConnectionProvider) ??
                                       new BulkUpdater();
            return bulkUpdater.Update(this, tableName, data.ToList(), null);
        }

        /// <summary>
        ///   Gets the names of the fields which comprise the unique identifier for the specified table.
        /// </summary>
        /// <param name="tableName">Name of the table.</param>
        /// <returns>A list of field names; an empty list if no key is defined.</returns>
        public override IList<string> GetKeyNames(string tableName)
        {
            return _schema.FindTable(tableName).PrimaryKey.AsEnumerable().ToList();
        }

        public void SetConnectionModifier(Func<IDbConnection, IDbConnection> connectionModifer)
        {
            _connectionModifier = connectionModifer;
        }

        public void ClearConnectionModifier()
        {
            _connectionModifier = connection => connection;
        }

        public async Task<int> Execute(ICommandBuilder commandBuilder)
        {
            IDbConnection connection = CreateConnection();
            using (connection.MaybeDisposable())
            {
                using (IDbCommand command = commandBuilder.GetCommand(connection, AdoOptions))
                {
                    await _commandExecutor.OpenIfClosed(connection);
                    return await _commandExecutor.ExecuteNonQuery(command);
                }
            }
        }

        internal async Task<int> Execute(ICommandBuilder commandBuilder, IDbConnection connection)
        {
            using (connection.MaybeDisposable())
            {
                using (IDbCommand command = commandBuilder.GetCommand(connection, AdoOptions))
                {
                    await _commandExecutor.OpenIfClosed(connection);
                    return await _commandExecutor.ExecuteNonQuery(command);
                }
            }
        }

        internal Task<int> Execute(ICommandBuilder commandBuilder, IAdapterTransaction transaction)
        {
            if (transaction == null) return Execute(commandBuilder);
            IDbTransaction dbTransaction = ((AdoAdapterTransaction) transaction).DbTransaction;
            return Execute(commandBuilder, dbTransaction);
        }

        internal Task<int> Execute(ICommandBuilder commandBuilder, IDbTransaction dbTransaction)
        {
            using (IDbCommand command = commandBuilder.GetCommand(dbTransaction.Connection, AdoOptions))
            {
                command.Transaction = dbTransaction;
                return _commandExecutor.ExecuteNonQuery(command);
            }
        }

        public void UseSharedConnection(IDbConnection connection)
        {
            _sharedConnection = connection;
        }

        public void StopUsingSharedConnection()
        {
            _sharedConnection = null;
        }

        public IDbConnection CreateConnection()
        {
            if (_sharedConnection != null) return _sharedConnection;
            IDbConnection connection = _connectionModifier(_connectionProvider.CreateConnection());
            ConnectionCreatedEventArgs args = ConnectionCreated.Raise(this, () => new ConnectionCreatedEventArgs(connection));
            if (args != null && args.OverriddenConnection != null)
            {
                return args.OverriddenConnection;
            }
            return connection;
        }

        public DatabaseSchema GetSchema()
        {
            return _schema ?? (_schema = DatabaseSchema.Get(_connectionProvider, _providerHelper));
        }

        private Task<IDictionary<string, object>> Upsert(string tableName, IReadOnlyDictionary<string, object> data, SimpleExpression criteria, bool resultRequired)
        {
            return new AdoAdapterUpserter(this).Upsert(tableName, data, criteria, resultRequired);
        }

        private Task<IEnumerable<IDictionary<string, object>>> UpsertMany(string tableName, IList<IReadOnlyDictionary<string, object>> list, bool isResultRequired,
            Func<IReadOnlyDictionary<string, object>, Exception, bool> errorCallback)
        {
            var upserter = new AdoAdapterUpserter(this);
            return upserter.UpsertMany(tableName, list, isResultRequired, errorCallback);
        }

        private Task<IEnumerable<IDictionary<string, object>>> UpsertMany(string tableName, IList<IReadOnlyDictionary<string, object>> list,
            IEnumerable<string> keyFieldNames, bool isResultRequired, Func<IReadOnlyDictionary<string, object>, Exception, bool> errorCallback)
        {
            return new AdoAdapterUpserter(this).UpsertMany(tableName, list, keyFieldNames.ToArray(), isResultRequired, errorCallback);
        }

        public string GetIdentityFunction()
        {
            return _connectionProvider.GetIdentityFunction();
        }

        protected override void OnReset()
        {
            DatabaseSchema.ClearCache();
            _schema = DatabaseSchema.Get(_connectionProvider, _providerHelper);
        }

        public static event EventHandler<ConnectionCreatedEventArgs> ConnectionCreated;

        #region ICloneable Members

        public object Clone()
        {
            return new AdoAdapter(_connectionProvider) {_connectionModifier = _connectionModifier};
        }

        #endregion
    }

    public class ConnectionCreatedEventArgs : EventArgs
    {
        private readonly IDbConnection _connection;

        public ConnectionCreatedEventArgs(IDbConnection connection)
        {
            _connection = connection;
        }

        public IDbConnection Connection
        {
            get { return _connection; }
        }

        internal IDbConnection OverriddenConnection { get; private set; }

        public void OverrideConnection(IDbConnection overridingConnection)
        {
            OverriddenConnection = overridingConnection;
        }
    }
}