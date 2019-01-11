// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Microsoft.DotNet.SourceBuild.Tasks
{
    /// <summary>
    /// For each nupkg directory under the PackageCacheDir, find all directories containing only
    /// dlls under the /ref/ folder.  These are packages that contain references only.
    /// Copies all found expanded reference-only packages to the destination directory, excluding
    /// the .nupkg file.  
    /// </summary>
    public class CopyReferenceOnlyPackages : Task
    {
        private static readonly string[] extensionsToExclude = { ".exe", ".dylib", ".so", ".profdata", ".pgd" };
        private static readonly string[] pathsToExclude = { "testdata" };
        private static readonly string refPath = string.Concat(Path.DirectorySeparatorChar, "ref", Path.DirectorySeparatorChar);

        /// <summary>
        /// Package cache dir containing prebuilt nupkgs. Path is expected to be like:
        /// 
        /// {PackageCacheDir}/{lowercase id}/{version}/{lowercase id}.{version}.nupkg
        ///
        /// This code assumes that these are expanded packages loaded from nuget.
        /// </summary>
        [Required]
        public string PackageCacheDir { get; set; }

        /// <summary>
        /// The destination directory for the dlls in reference packages.
        /// Paths are preserved in the destination.
        /// </summary>
        [Required]
        public string DllDestinationDir { get; set; }

        /// <summary>
        /// The destination directory for the nupkg files identified 
        /// as containing reference-only packages.
        /// </summary>
        [Required]
        public string IdentifiedPackagesDir { get; set; }

        /// <summary>
        /// The destination directory for the reference packages.
        /// </summary>
        [Required]
        public string DestinationDir { get; set; }

        /// <summary>
        /// A list of package nupkgs to forcibly treat as reference packages. Native binary files
        /// will be skipped if they exist in the package.
        /// </summary>
        public string[] ForcePackageFiles { get; set; }

        /// <summary>
        /// Enumerate all files in a directory and its sub-directories.
        /// </summary>
        private static IEnumerable<string> EnumerateAllFiles(string path, string searchPattern)
        {
            return Directory.EnumerateFiles(path, searchPattern, SearchOption.AllDirectories);
        }

        public override bool Execute()
        {
            DateTime startTime = DateTime.Now;

            var referenceOnlyPackages = EnumerateAllFiles(PackageCacheDir, "*.nupkg")
                .Select(nupkgFilePath => new
                {
                    Files = EnumerateAllFiles(Path.GetDirectoryName(nupkgFilePath), "*.*").ToArray(),
                    ForceRefPackage = ForcePackageFiles?.Contains(nupkgFilePath) == true
                })
                .Where(nupkg =>
                {
                    if (nupkg.ForceRefPackage)
                    {
                        return true;
                    }

                    // Do not include directories that contain exes, shared object files, OSX dynamic libraries
                    // or profiling data
                    if (nupkg.Files
                        .Any(
                            file => extensionsToExclude.Contains(Path.GetExtension(file)) 
                            || pathsToExclude.Any(path => file.Contains(path))))
                    {
                        return false;
                    }
                    
                    // Return directories that, if containing dlls, only have dlls in the
                    // ref folder
                    return nupkg.Files
                            .Where(file => String.Equals(Path.GetExtension(file), ".dll", StringComparison.OrdinalIgnoreCase))
                            .All(dir => dir.Contains(refPath));
                })
                .ToArray();

            Directory.CreateDirectory(IdentifiedPackagesDir);
            foreach (var package in referenceOnlyPackages)
            {
                foreach (var file in package.Files)
                {
                    if (file.EndsWith(".nupkg")) 
                    {
                        File.Copy(file, Path.Combine(IdentifiedPackagesDir, Path.GetFileName(file)), true);
                    }
                    else if (file.EndsWith(".nupkg.sha512") ||
                        extensionsToExclude.Contains(Path.GetExtension(file)) ||
                        Path.GetFileName(Path.GetDirectoryName(file)) == "native")
                    {
                        Log.LogMessage(MessageImportance.Low, $"Skipped {file}");
                    }
                    else
                    {
                        var destination = file.Replace(PackageCacheDir, DestinationDir);
                        if (file.EndsWith(".dll"))
                        {
                            destination = file.Replace(PackageCacheDir, DllDestinationDir);
                        }
                        Directory.CreateDirectory(Path.GetDirectoryName(destination));
                        File.Copy(file, destination, true);
                        
                        // Add a wildcard for files in each nuspec
                        if (destination.EndsWith(".nuspec"))
                        {
                            var fileText = File.ReadAllText(destination);
                            File.WriteAllText(destination, fileText.Replace("</package>", "<files><file src=\".\\**\\*\"/></files>\n</package>"));
                        }
                    }
                }
            }

            // Report status on the task
            Log.LogMessage(
                MessageImportance.High,
                "Identified reference-only packages. " +
                    $"Found {referenceOnlyPackages.Count()} packages.  Took {DateTime.Now - startTime} ");

            return !Log.HasLoggedErrors;
        }

    }
}
