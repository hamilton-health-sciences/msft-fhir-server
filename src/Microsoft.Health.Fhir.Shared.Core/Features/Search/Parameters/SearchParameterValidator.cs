﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using EnsureThat;
using FluentValidation.Results;
using Hl7.Fhir.Model;
using Microsoft.Health.Core.Features.Security.Authorization;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Features.Validation;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Shared.Core.Features.Search.Parameters
{
    public class SearchParameterValidator : ISearchParameterValidator
    {
        private readonly Func<IScoped<IFhirOperationDataStore>> _fhirOperationDataStoreFactory;
        private readonly IAuthorizationService<DataActions> _authorizationService;
        private readonly ISearchParameterDefinitionManager _searchParameterDefinitionManager;

        private const string HttpPostName = "POST";
        private const string HttpPutName = "PUT";
        private const string HttpDeleteName = "DELETE";

        public SearchParameterValidator(
            Func<IScoped<IFhirOperationDataStore>> fhirOperationDataStoreFactory,
            IAuthorizationService<DataActions> authorizationService,
            ISearchParameterDefinitionManager searchParameterDefinitionManager)
        {
            EnsureArg.IsNotNull(fhirOperationDataStoreFactory, nameof(fhirOperationDataStoreFactory));
            EnsureArg.IsNotNull(authorizationService, nameof(authorizationService));
            EnsureArg.IsNotNull(searchParameterDefinitionManager, nameof(searchParameterDefinitionManager));

            _fhirOperationDataStoreFactory = fhirOperationDataStoreFactory;
            _authorizationService = authorizationService;
            _searchParameterDefinitionManager = searchParameterDefinitionManager;
        }

        public async Task ValidateSearchParamterInput(SearchParameter searchParam, string method, CancellationToken cancellationToken)
        {
            if (await _authorizationService.CheckAccess(DataActions.Reindex, cancellationToken) != DataActions.Reindex)
            {
                throw new UnauthorizedFhirActionException();
            }

            // check if reindex job is running
            using (IScoped<IFhirOperationDataStore> fhirOperationDataStore = _fhirOperationDataStoreFactory())
            {
                (var activeReindexJobs, var reindexJobId) = await fhirOperationDataStore.Value.CheckActiveReindexJobsAsync(cancellationToken);
                if (activeReindexJobs)
                {
                    throw new JobConflictException(string.Format(Resources.ChangesToSearchParametersNotAllowedWhileReindexing, reindexJobId));
                }
            }

            var validationFailures = new List<ValidationFailure>();

            if (string.IsNullOrEmpty(searchParam.Url))
            {
                validationFailures.Add(
                    new ValidationFailure(nameof(Base.TypeName), Resources.SearchParameterDefinitionInvalidMissingUri));
            }
            else
            {
                try
                {
                    _searchParameterDefinitionManager.GetSearchParameter(new Uri(searchParam.Url));

                    // If a post, then it is a creation of a new search parameter
                    // only allow this if no other parameters exist with the same Uri
                    if (method.Equals(HttpPostName, StringComparison.OrdinalIgnoreCase))
                    {
                        // if no exception is thrown, then the search parameter with the same Uri was found
                        // and this is a conflict
                        validationFailures.Add(
                            new ValidationFailure(
                                nameof(searchParam.Url),
                                string.Format(Resources.SearchParameterDefinitionDuplicatedEntry, searchParam.Url)));
                    }
                }
                catch (FormatException)
                {
                    validationFailures.Add(
                          new ValidationFailure(
                              nameof(searchParam.Url),
                              string.Format(Resources.SearchParameterDefinitionInvalidDefinitionUri, searchParam.Url)));
                }
                catch (SearchParameterNotSupportedException)
                {
                    // if thrown, then the search parameter is not found
                    // if a PUT, then we should be updating an exsting paramter, but it was not found
                    if (method.Equals(HttpPutName, StringComparison.OrdinalIgnoreCase) ||
                        method.Equals(HttpDeleteName, StringComparison.OrdinalIgnoreCase))
                    {
                        // if an exception above was thrown, then the search parameter with the same Uri was not found
                        // and DELETE or PUT can only run on existing parameter
                        validationFailures.Add(
                            new ValidationFailure(
                                nameof(searchParam.Url),
                                string.Format(Resources.SearchParameterDefinitionNotFound, searchParam.Url)));
                    }
                }
            }

            // validate that the url does not correspond to a search param from the spec
            // TODO: still need a method to determine spec defined search params

            // validation of the fhir path
            // TODO: separate user story for this validation

            if (validationFailures.Any())
            {
                throw new ResourceNotValidException(validationFailures);
            }
        }
    }
}
