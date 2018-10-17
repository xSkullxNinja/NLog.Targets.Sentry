// -----------------------------------------------------------------------
// <copyright file="VersionNumberType.cs" company="Curtis Instruments">
// Copyright (c) Curtis Instruments. All rights reserved.
// </copyright>
// <author>willsont</author>
// -----------------------------------------------------------------------

namespace NLog.Targets
{
    /// <summary>
    /// Type of Version Number to send to Sentry
    /// </summary>
    public enum VersionNumberType
    {
        /// <summary>
        /// The Assembly Version
        /// </summary>
        AssemblyVersion,

        /// <summary>
        /// The Assembly File Version
        /// </summary>
        AssemblyFileVersion,

        /// <summary>
        /// The Assembly Informational Version (the Product Version in Windows Explorer or Version in new-style csproj's).
        /// </summary>
        AssemblyInformationalVersion,
    }
}