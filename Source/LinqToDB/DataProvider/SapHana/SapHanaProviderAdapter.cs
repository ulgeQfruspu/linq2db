﻿using System;
using System.Data;
using System.Data.Common;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace LinqToDB.DataProvider.SapHana
{
	using Expressions;

	public class SapHanaProviderAdapter : IDynamicProviderAdapter
	{
		private static readonly object _syncRoot = new ();
		private static SapHanaProviderAdapter? _instance;

#if NETFRAMEWORK
		public const string AssemblyName        = "Sap.Data.Hana.v4.5";
#else
		public const string AssemblyName        = "Sap.Data.Hana.Core.v2.1";
#endif

		public const string ClientNamespace     = "Sap.Data.Hana";
		public const string ProviderFactoryName = "Sap.Data.Hana";

		private SapHanaProviderAdapter(
			Type connectionType,
			Type dataReaderType,
			Type parameterType,
			Type commandType,
			Type transactionType,

			Action<DbParameter, HanaDbType> dbTypeSetter,

			Func<DbConnection, HanaBulkCopyOptions, DbTransaction?, HanaBulkCopy> bulkCopyCreator,
			Func<int, string, HanaBulkCopyColumnMapping>                          bulkCopyColumnMappingCreator)
		{
			ConnectionType  = connectionType;
			DataReaderType  = dataReaderType;
			ParameterType   = parameterType;
			CommandType     = commandType;
			TransactionType = transactionType;

			SetDbType = dbTypeSetter;

			CreateBulkCopy              = bulkCopyCreator;
			CreateBulkCopyColumnMapping = bulkCopyColumnMappingCreator;
		}

		public Type ConnectionType  { get; }
		public Type DataReaderType  { get; }
		public Type ParameterType   { get; }
		public Type CommandType     { get; }
		public Type TransactionType { get; }

		public Action<DbParameter, HanaDbType> SetDbType { get; }

		public Func<DbConnection, HanaBulkCopyOptions, DbTransaction?, HanaBulkCopy> CreateBulkCopy              { get; }
		public Func<int, string, HanaBulkCopyColumnMapping>                          CreateBulkCopyColumnMapping { get; }

		internal static SapHanaProviderAdapter GetInstance()
		{
			if (_instance == null)
				lock (_syncRoot)
					if (_instance == null)
					{
						var assembly = Common.Tools.TryLoadAssembly(AssemblyName, ProviderFactoryName);
						if (assembly == null)
							throw new InvalidOperationException($"Cannot load assembly {AssemblyName}");

						var connectionType  = assembly.GetType($"{ClientNamespace}.HanaConnection" , true)!;
						var dataReaderType  = assembly.GetType($"{ClientNamespace}.HanaDataReader" , true)!;
						var parameterType   = assembly.GetType($"{ClientNamespace}.HanaParameter"  , true)!;
						var commandType     = assembly.GetType($"{ClientNamespace}.HanaCommand"    , true)!;
						var transactionType = assembly.GetType($"{ClientNamespace}.HanaTransaction", true)!;
						var dbType          = assembly.GetType($"{ClientNamespace}.HanaDbType"     , true)!;

						var bulkCopyType                    = assembly.GetType($"{ClientNamespace}.HanaBulkCopy"                       , true)!;
						var bulkCopyOptionsType             = assembly.GetType($"{ClientNamespace}.HanaBulkCopyOptions"                , true)!;
						var bulkCopyColumnMappingType       = assembly.GetType($"{ClientNamespace}.HanaBulkCopyColumnMapping"          , true)!;
						var rowsCopiedEventHandlerType      = assembly.GetType($"{ClientNamespace}.HanaRowsCopiedEventHandler"         , true)!;
						var rowsCopiedEventArgs             = assembly.GetType($"{ClientNamespace}.HanaRowsCopiedEventArgs"            , true)!;
						var bulkCopyColumnMappingCollection = assembly.GetType($"{ClientNamespace}.HanaBulkCopyColumnMappingCollection", true)!;

						var typeMapper = new TypeMapper();

						typeMapper.RegisterTypeWrapper<HanaConnection>(connectionType);
						typeMapper.RegisterTypeWrapper<HanaTransaction>(transactionType);
						typeMapper.RegisterTypeWrapper<HanaParameter>(parameterType);
						typeMapper.RegisterTypeWrapper<HanaDbType>(dbType);

						// bulk copy types
						typeMapper.RegisterTypeWrapper<HanaBulkCopy>(bulkCopyType);
						typeMapper.RegisterTypeWrapper<HanaRowsCopiedEventArgs>(rowsCopiedEventArgs);
						typeMapper.RegisterTypeWrapper<HanaRowsCopiedEventHandler>(rowsCopiedEventHandlerType);
						typeMapper.RegisterTypeWrapper<HanaBulkCopyColumnMappingCollection>(bulkCopyColumnMappingCollection);
						typeMapper.RegisterTypeWrapper<HanaBulkCopyOptions>(bulkCopyOptionsType);
						typeMapper.RegisterTypeWrapper<HanaBulkCopyColumnMapping>(bulkCopyColumnMappingType);

						typeMapper.FinalizeMappings();

						var typeSetter = typeMapper.Type<HanaParameter>().Member(p => p.HanaDbType).BuildSetter<DbParameter>();

						_instance = new SapHanaProviderAdapter(
							connectionType,
							dataReaderType,
							parameterType,
							commandType,
							transactionType,
							typeSetter,
							typeMapper.BuildWrappedFactory((DbConnection connection, HanaBulkCopyOptions options, DbTransaction? transaction) => new HanaBulkCopy((HanaConnection)(object)connection, options, (HanaTransaction?)(object?)transaction)),
							typeMapper.BuildWrappedFactory((int source, string destination) => new HanaBulkCopyColumnMapping(source, destination)));
					}

			return _instance;
		}

		[Wrapper]
		public class HanaTransaction
		{
		}

		[Wrapper]
		public class HanaConnection
		{
		}

		[Wrapper]
		private sealed class HanaParameter
		{
			public HanaDbType HanaDbType { get; set; }
		}

		[Wrapper]
		public enum HanaDbType
		{
			AlphaNum     = 1,
			BigInt       = 2,
			Blob         = 3,
			Boolean      = 4,
			Clob         = 5,
			Date         = 6,
			Decimal      = 7,
			Double       = 8,
			Integer      = 9,
			NClob        = 10,
			NVarChar     = 11,
			Real         = 12,
			SecondDate   = 13,
			ShortText    = 14,
			SmallDecimal = 15,
			SmallInt     = 16,
			TableType    = 23,
			Text         = 17,
			Time         = 18,
			TimeStamp    = 19,
			TinyInt      = 20,
			VarBinary    = 21,
			VarChar      = 22
		}

		#region BulkCopy
		[Wrapper]
		public class HanaBulkCopy : TypeWrapper, IDisposable
		{
			private static object[] Wrappers { get; }
				= new object[]
			{
				// [0]: Dispose
				(Expression<Action<HanaBulkCopy>>                                   )((HanaBulkCopy this_                    ) => ((IDisposable)this_).Dispose()),
				// [1]: WriteToServer
				(Expression<Action<HanaBulkCopy, IDataReader>>                      )((HanaBulkCopy this_, IDataReader reader) => this_.WriteToServer(reader)),
				// [2]: get NotifyAfter
				(Expression<Func<HanaBulkCopy, int>>                                )((HanaBulkCopy this_                    ) => this_.NotifyAfter),
				// [3]: get BatchSize
				(Expression<Func<HanaBulkCopy, int>>                                )((HanaBulkCopy this_                    ) => this_.BatchSize),
				// [4]: get BulkCopyTimeout
				(Expression<Func<HanaBulkCopy, int>>                                )((HanaBulkCopy this_                    ) => this_.BulkCopyTimeout),
				// [5]: get DestinationTableName
				(Expression<Func<HanaBulkCopy, string?>>                            )((HanaBulkCopy this_                    ) => this_.DestinationTableName),
				// [6]: get ColumnMappings
				(Expression<Func<HanaBulkCopy, HanaBulkCopyColumnMappingCollection>>)((HanaBulkCopy this_                    ) => this_.ColumnMappings),
				// [7]: set NotifyAfter
				PropertySetter((HanaBulkCopy this_) => this_.NotifyAfter),
				// [8]: set BatchSize
				PropertySetter((HanaBulkCopy this_) => this_.BatchSize),
				// [9]: set BulkCopyTimeout
				PropertySetter((HanaBulkCopy this_) => this_.BulkCopyTimeout),
				// [10]: set DestinationTableName
				PropertySetter((HanaBulkCopy this_) => this_.DestinationTableName),
				// [11]: WriteToServerAsync
				new Tuple<LambdaExpression, bool>
				((Expression<Func<HanaBulkCopy, IDataReader, CancellationToken, Task>>)((HanaBulkCopy this_, IDataReader reader, CancellationToken cancellationToken) => this_.WriteToServerAsync(reader, cancellationToken)), true),
			};

			private static string[] Events { get; }
				= new[]
			{
				nameof(HanaRowsCopied)
			};

			public HanaBulkCopy(object instance, Delegate[] wrappers) : base(instance, wrappers)
			{
			}

			public HanaBulkCopy(HanaConnection connection, HanaBulkCopyOptions options, HanaTransaction? transaction) => throw new NotImplementedException();

			public void Dispose      ()                       => ((Action<HanaBulkCopy>)CompiledWrappers[0])(this);
#pragma warning disable RS0030 // API mapping must preserve type
			public void WriteToServer(IDataReader dataReader) => ((Action<HanaBulkCopy, IDataReader>)CompiledWrappers[1])(this, dataReader);
#pragma warning restore RS0030 //  API mapping must preserve type

			public bool CanWriteToServerAsync => CompiledWrappers[11] != null;
#pragma warning disable RS0030 // API mapping must preserve type
			public Task WriteToServerAsync(IDataReader dataReader, CancellationToken cancellationToken)
				=> ((Func<HanaBulkCopy, IDataReader, CancellationToken, Task>)CompiledWrappers[11])(this, dataReader, cancellationToken);
#pragma warning restore RS0030 //  API mapping must preserve type

			public int NotifyAfter
			{
				get => ((Func  <HanaBulkCopy, int>)CompiledWrappers[2])(this);
				set => ((Action<HanaBulkCopy, int>)CompiledWrappers[7])(this, value);
			}

			public int BatchSize
			{
				get => ((Func  <HanaBulkCopy, int>)CompiledWrappers[3])(this);
				set => ((Action<HanaBulkCopy, int>)CompiledWrappers[8])(this, value);
			}

			public int BulkCopyTimeout
			{
				get => ((Func  <HanaBulkCopy, int>)CompiledWrappers[4])(this);
				set => ((Action<HanaBulkCopy, int>)CompiledWrappers[9])(this, value);
			}

			public string? DestinationTableName
			{
				get => ((Func  <HanaBulkCopy, string?>)CompiledWrappers[5] )(this);
				set => ((Action<HanaBulkCopy, string?>)CompiledWrappers[10])(this, value);
			}

			public HanaBulkCopyColumnMappingCollection ColumnMappings => ((Func<HanaBulkCopy, HanaBulkCopyColumnMappingCollection>)CompiledWrappers[6])(this);

			private      HanaRowsCopiedEventHandler? _HanaRowsCopied;
			public event HanaRowsCopiedEventHandler?  HanaRowsCopied
			{
				add    => _HanaRowsCopied = (HanaRowsCopiedEventHandler?)Delegate.Combine(_HanaRowsCopied, value);
				remove => _HanaRowsCopied = (HanaRowsCopiedEventHandler?)Delegate.Remove (_HanaRowsCopied, value);
			}
		}

		[Wrapper]
		public class HanaRowsCopiedEventArgs : TypeWrapper
		{
			private static LambdaExpression[] Wrappers { get; }
				= new LambdaExpression[]
			{
				// [0]: get RowsCopied
				(Expression<Func<HanaRowsCopiedEventArgs, long>>)((HanaRowsCopiedEventArgs this_) => this_.RowsCopied),
				// [1]: get Abort
				(Expression<Func<HanaRowsCopiedEventArgs, bool>>)((HanaRowsCopiedEventArgs this_) => this_.Abort),
				// [2]: set Abort
				PropertySetter((HanaRowsCopiedEventArgs this_) => this_.Abort),
			};

			public HanaRowsCopiedEventArgs(object instance, Delegate[] wrappers) : base(instance, wrappers)
			{
			}

			public long RowsCopied => ((Func<HanaRowsCopiedEventArgs, long>)CompiledWrappers[0])(this);

			public bool Abort
			{
				get => ((Func  <HanaRowsCopiedEventArgs, bool>)CompiledWrappers[1])(this);
				set => ((Action<HanaRowsCopiedEventArgs, bool>)CompiledWrappers[2])(this, value);
			}
		}

		[Wrapper]
		public delegate void HanaRowsCopiedEventHandler(object sender, HanaRowsCopiedEventArgs e);

		[Wrapper]
		public class HanaBulkCopyColumnMappingCollection : TypeWrapper
		{
			private static LambdaExpression[] Wrappers { get; }
				= new LambdaExpression[]
			{
				// [0]: Add
				(Expression<Func<HanaBulkCopyColumnMappingCollection, HanaBulkCopyColumnMapping, HanaBulkCopyColumnMapping>>)((HanaBulkCopyColumnMappingCollection this_, HanaBulkCopyColumnMapping column) => this_.Add(column)),
			};

			public HanaBulkCopyColumnMappingCollection(object instance, Delegate[] wrappers) : base(instance, wrappers)
			{
			}

			public HanaBulkCopyColumnMapping Add(HanaBulkCopyColumnMapping bulkCopyColumnMapping) => ((Func<HanaBulkCopyColumnMappingCollection, HanaBulkCopyColumnMapping, HanaBulkCopyColumnMapping>)CompiledWrappers[0])(this, bulkCopyColumnMapping);
		}

		[Wrapper, Flags]
		public enum HanaBulkCopyOptions
		{
			Default                = 0,
			KeepIdentity           = 1,
			TableLock              = 2,
			UseInternalTransaction = 4
		}

		[Wrapper]
		public class HanaBulkCopyColumnMapping : TypeWrapper
		{
			public HanaBulkCopyColumnMapping(object instance) : base(instance, null)
			{
			}

			public HanaBulkCopyColumnMapping(int source, string destination) => throw new NotImplementedException();
		}

		#endregion
	}
}
