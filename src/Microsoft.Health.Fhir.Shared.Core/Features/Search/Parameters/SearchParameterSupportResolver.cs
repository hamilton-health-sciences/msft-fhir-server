﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using EnsureThat;
using Hl7.Fhir.Introspection;
using Hl7.FhirPath;
using Hl7.FhirPath.Expressions;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Search.Converters;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.Core.Features.Search.Parameters
{
    public class SearchParameterSupportResolver : ISearchParameterSupportResolver
    {
        private readonly ISearchParameterDefinitionManager _definitionManager;
        private readonly IFhirNodeToSearchValueTypeConverterManager _searchValueTypeConverterManager;
        private static readonly FhirPathCompiler _compiler = new FhirPathCompiler();
        private const string _codeOfTName = "codeOfT";

        public SearchParameterSupportResolver(
            ISearchParameterDefinitionManager definitionManager,
            IFhirNodeToSearchValueTypeConverterManager searchValueTypeConverterManager)
        {
            EnsureArg.IsNotNull(definitionManager, nameof(definitionManager));
            EnsureArg.IsNotNull(searchValueTypeConverterManager, nameof(searchValueTypeConverterManager));

            _definitionManager = definitionManager;
            _searchValueTypeConverterManager = searchValueTypeConverterManager;
        }

        public (bool Supported, bool IsPartiallySupported) IsSearchParameterSupported(SearchParameterInfo parameterInfo)
        {
            EnsureArg.IsNotNull(parameterInfo, nameof(parameterInfo));

            if (string.IsNullOrWhiteSpace(parameterInfo.Expression))
            {
                return (false, false);
            }

            Expression parsed = _compiler.Parse(parameterInfo.Expression);

            (SearchParamType Type, Expression, Uri DefinitionUrl)[] componentExpressions = parameterInfo.Component
                ?.Select(x => (x.ResolvedSearchParameter.Type,
                    _compiler.Parse(x.Expression),
                    x.DefinitionUrl))
                .ToArray();

            IEnumerable<string> resourceTypes = (parameterInfo.TargetResourceTypes ?? Enumerable.Empty<string>()).Concat(parameterInfo.BaseResourceTypes ?? Enumerable.Empty<string>());

            if (!resourceTypes.Any())
            {
                throw new NotSupportedException("No target resources defined.");
            }

            foreach (var resource in resourceTypes)
            {
                SearchParameterTypeResult[] results = SearchParameterToTypeResolver.Resolve(
                    resource,
                    (parameterInfo.Type, parsed, parameterInfo.Url),
                    componentExpressions).ToArray();

                var converters = results
                    .Select(result => (
                        result,
                        hasConverter: _searchValueTypeConverterManager.TryGetConverter(
                            GetBaseType(result.ClassMapping),
                            SearchIndexer.GetSearchValueTypeForSearchParamType(result.SearchParamType),
                            out IFhirNodeToSearchValueTypeConverter converter),
                        converter))
                    .ToArray();

                if (!converters.Any())
                {
                    return (false, false);
                }

                if (!converters.All(x => x.hasConverter))
                {
                    bool partialSupport = converters.Any(x => x.hasConverter);
                    return (partialSupport, partialSupport);
                }
            }

            string GetBaseType(ClassMapping classMapping)
            {
                return classMapping.IsCodeOfT ? _codeOfTName : classMapping.Name;
            }

            return (true, false);
        }
    }
}
