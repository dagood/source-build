﻿// Originally from
// https://raw.githubusercontent.com/NuGet/NuGet.Client/6007570d889b9392fb9826a6d31f99f6acf9e440/src/NuGet.Core/Microsoft.Build.NuGetSdkResolver/RestoreRunnerEx.cs

// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using NuGet.Commands;
using NuGet.Configuration;
using NuGet.Frameworks;
using NuGet.LibraryModel;
using NuGet.ProjectModel;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ILogger = NuGet.Common.ILogger;

namespace Microsoft.DotNet.SourceBuild.Tasks
{
    /// <summary>
    /// An extension of the NuGet.Commands.RestoreRunner class that contains APIs we do not yet have.
    /// https://github.com/NuGet/Home/issues/5919
    /// </summary>
    internal static class RestoreRunnerEx
    {
        // NuGet requires at least one framework, we use .NET Standard here just to get the API to do work.  The framework is not actually used.
        private static readonly List<NuGetFramework> TargetFrameworks = new List<NuGetFramework>
        {
            FrameworkConstants.CommonFrameworks.NetStandard
        };

        /// <summary>
        /// Restores a package by querying, downloading, and unzipping it without generating any other files (like project.assets.json).
        /// </summary>
        /// <param name="projectPath">The full path to the project.</param>
        /// <param name="id">The ID of the package.</param>
        /// <param name="version">The version of the package.</param>
        /// <param name="settings">The NuGet settings to use.</param>
        /// <param name="logger">An <see cref="ILogger"/> to use for logging.</param>
        /// <returns></returns>
        public static Task<IReadOnlyList<RestoreResultPair>> RunWithoutCommit(string projectPath, string id, string version, ISettings settings, ILogger logger)
        {
            using (var sourceCacheContext = new SourceCacheContext
            {
                IgnoreFailedSources = true,
            })
            {
                // The package spec details what packages to restore
                var packageSpec = new PackageSpec(TargetFrameworks.Select(i => new TargetFrameworkInformation
                {
                    FrameworkName = i,
                }).ToList())
                {
                    Dependencies = new List<LibraryDependency>
                    {
                        new LibraryDependency
                        {
                            LibraryRange = new LibraryRange(id, new VersionRange(NuGetVersion.Parse(version)), LibraryDependencyTarget.Package),
                            SuppressParent = LibraryIncludeFlags.All,
                            AutoReferenced = true,
                            IncludeType = LibraryIncludeFlags.None,
                            Type = LibraryDependencyType.Build
                        }
                    },
                    RestoreMetadata = new ProjectRestoreMetadata
                    {
                        ProjectPath = projectPath,
                        ProjectName = Path.GetFileNameWithoutExtension(projectPath),
                        ProjectStyle = ProjectStyle.PackageReference,
                        ProjectUniqueName = projectPath,
                        OutputPath = Path.GetTempPath(),
                        OriginalTargetFrameworks = TargetFrameworks.Select(i => i.ToString()).ToList(),
                        ConfigFilePaths = SettingsUtility.GetConfigFilePaths(settings).ToList(),
                        PackagesPath = SettingsUtility.GetGlobalPackagesFolder(settings),
                        Sources = SettingsUtility.GetEnabledSources(settings).ToList(),
                        FallbackFolders = SettingsUtility.GetFallbackPackageFolders(settings).ToList()
                    },
                    FilePath = projectPath,
                    Name = Path.GetFileNameWithoutExtension(projectPath),
                };

                var dependencyGraphSpec = new DependencyGraphSpec();

                dependencyGraphSpec.AddProject(packageSpec);

                dependencyGraphSpec.AddRestore(packageSpec.RestoreMetadata.ProjectUniqueName);

                IPreLoadedRestoreRequestProvider requestProvider = new DependencyGraphSpecRequestProvider(new RestoreCommandProvidersCache(), dependencyGraphSpec);

                var restoreArgs = new RestoreArgs
                {
                    AllowNoOp = true,
                    CacheContext = sourceCacheContext,
                    CachingSourceProvider = new CachingSourceProvider(new PackageSourceProvider(settings)),
                    Log = logger,
                };

                // Create requests from the arguments
                var requests = requestProvider.CreateRequests(restoreArgs).Result;

                // Restore the package without generating extra files
                return RestoreRunner.RunWithoutCommit(requests, restoreArgs);
            }
        }
    }
}
