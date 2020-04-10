﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;

namespace FhirSchemaManager.Exceptions
{
    public class SchemaOperationFailedException : Exception
    {
        public SchemaOperationFailedException(string message)
            : base(message)
        {
            EnsureArg.IsNotNullOrWhiteSpace(message, nameof(message));
        }
    }
}
