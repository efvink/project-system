﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.VisualStudio.ProjectSystem.Properties
{
    /// <summary>
    /// A project property provider that intercepts all the callbacks for a specific property name
    /// on the default <see cref="IProjectPropertiesProvider"/> for validation and/or transformation of the property value.
    /// </summary>
    internal interface IInterceptingPropertyValueProvider
    {
        /// <summary>
        /// Gets the name of the property.
        /// </summary>
        string GetPropertyName();

        /// <summary>
        /// Validate and/or transform the given evaluated property value.
        /// </summary>
        Task<string> InterceptGetEvaluatedPropertyValueAsync(string evaluatedPropertyValue, IProjectProperties defaultProperties);

        /// <summary>
        /// Validate and/or transform the given unevaluated property value, i.e. "raw" value read from the project file.
        /// </summary>
        Task<string> InterceptGetUnevaluatedPropertyValueAsync(string unevaluatedPropertyValue, IProjectProperties defaultProperties);

        /// <summary>
        /// Validate and/or transform the given unevaluated property value to be written back to the project file.
        /// </summary>
        Task<string> InterceptSetPropertyValueAsync(string unevaluatedPropertyValue, IProjectProperties defaultProperties, IReadOnlyDictionary<string, string> dimensionalConditions = null);
    }
}