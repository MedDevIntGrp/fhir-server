﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Net;
using System.Transactions;
using EnsureThat;
using Microsoft.Health.Abstractions.Features.Transactions;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage.Registry
{
    internal class SqlServerStatusRegistryInitializer : IStartable
    {
        private readonly ISearchParameterRegistry _filebasedRegistry;
        private readonly SqlServerStatusRegistry _sqlServerStatusRegistry;
        private readonly ITransactionHandler _transactionHandler;

        public SqlServerStatusRegistryInitializer(
            FilebasedSearchParameterRegistry.Resolver filebasedRegistry,
            SqlServerStatusRegistry sqlServerStatusRegistry,
            ITransactionHandler transactionHandler)
        {
            EnsureArg.IsNotNull(filebasedRegistry, nameof(filebasedRegistry));
            EnsureArg.IsNotNull(sqlServerStatusRegistry, nameof(sqlServerStatusRegistry));
            EnsureArg.IsNotNull(transactionHandler, nameof(transactionHandler));

            _filebasedRegistry = filebasedRegistry.Invoke();
            _sqlServerStatusRegistry = sqlServerStatusRegistry;
            _transactionHandler = transactionHandler;
        }

        public async void Start()
        {
            try
            {
                // Wrap the SQL calls in a transaction to ensure the read and insert operations are atomic.
                using (var transaction = _transactionHandler.BeginTransaction())
                {
                    if (await _sqlServerStatusRegistry.GetIsSearchParameterRegistryEmpty())
                    {
                        // The registry has yet to be initialized, so get the search parameter statuses from file and add them to the registry.
                        IReadOnlyCollection<ResourceSearchParameterStatus> readonlyStatuses = await _filebasedRegistry.GetSearchParameterStatuses();
                        var statuses = new List<ResourceSearchParameterStatus>(readonlyStatuses);

                        await _sqlServerStatusRegistry.BulkInsert(statuses);
                    }

                    transaction.Complete();
                }
            }
            catch (TransactionAbortedException)
            {
                throw new FhirTransactionFailedException(Resources.SearchParameterInitializationTransactionFailedError, HttpStatusCode.BadRequest);
            }
        }
    }
}