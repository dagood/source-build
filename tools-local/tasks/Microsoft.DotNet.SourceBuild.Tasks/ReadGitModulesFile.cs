// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Microsoft.Build.Tasks;
using NuGet.Packaging;
using NuGet.Packaging.Core;

namespace Microsoft.DotNet.Build.Tasks
{
    public class ReadGitModulesFile : Task
    {
        [Required]
        public string File { get; set; }

        /// <summary>
        /// A list of repositories to specifically fetch configuration for. Metadata is added to
        /// these items and they are output by AugmentedRepositories.
        /// 
        /// The "GitModulePath" metadata on each item is used to associate it to a config entry.
        /// </summary>
        public ITaskItem[] Repositories { get; set; }

        [Output]
        public ITaskItem[] SubmoduleConfiguration { get; set; }

        [Output]
        public ITaskItem[] AugmentedRepositories { get; set; }

        private const string __SubmoduleSrcKey = "submodule.";
        private const string __GitModulePathMetadataName = "GitModulePath";

        public override bool Execute()
        {
            var r = new Exec
            {
                BuildEngine = BuildEngine,
                Command = $"git config --file {File} --list",
                ConsoleToMSBuild = true
            };
            if (!r.Execute())
            {
                return false;
            }

            SubmoduleConfiguration = r.ConsoleOutput
                .Select(item =>
                {
                    string line = item.ItemSpec;

                    // Ignore non-submodule options. In case some misc. git config info is added to
                    // .gitmodules or this task is pointed at a different file.
                    if (!line.StartsWith(__SubmoduleSrcKey))
                    {
                        return null;
                    }

                    line = line.Substring(__SubmoduleSrcKey.Length);

                    int keyValueSeparator = line.IndexOf('=');
                    string key = line.Substring(0, keyValueSeparator);
                    string value = line.Substring(keyValueSeparator + 1);

                    int lastKeySegmentSeparator = key.LastIndexOf('.');
                    string submoduleName = key.Substring(0, lastKeySegmentSeparator);
                    string configKey = key.Substring(lastKeySegmentSeparator + 1);

                    return new
                    {
                        ItemSpec = submoduleName,
                        MetadataName = UppercaseFirstChar(configKey),
                        MetadataValue = value
                    };
                })
                .Where(i => i != null)
                .GroupBy(i => i.ItemSpec)
                .Select(g => new TaskItem(
                    g.Key,
                    g.ToDictionary(
                        i => i.MetadataName,
                        i => i.MetadataValue)))
                .ToArray();

            AugmentedRepositories = Repositories
                .Select(item =>
                {
                    string modulePath = item.GetMetadata(__GitModulePathMetadataName);

                    ITaskItem matchingConfig = SubmoduleConfiguration
                        .FirstOrDefault(c => c.ItemSpec == modulePath);

                    if (matchingConfig == null)
                    {
                        return null;
                    }

                    var mergedItem = new TaskItem(item);
                    matchingConfig.CopyMetadataTo(mergedItem);
                    return mergedItem;
                })
                .Where(item => item != null)
                .ToArray();

            return true;
        }

        private static string UppercaseFirstChar(string s)
        {
            return s.Substring(0, 1).ToUpperInvariant() + s.Substring(1);
        }
    }
}
