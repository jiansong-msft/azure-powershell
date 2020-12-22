//
// Copyright (c) Microsoft.  All rights reserved.
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//   http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//
namespace Microsoft.WindowsAzure.Build.Tasks
{
    using Microsoft.Build.Framework;
    using Microsoft.Build.Utilities;

    using Newtonsoft.Json;

    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Runtime.CompilerServices;
    using System.Text.RegularExpressions;

    using YamlDotNet.Serialization;
    using YamlDotNet.Serialization.NamingConventions;

    /// <summary>
    /// A simple Microsoft Build task used to generate a list of test assemblies to be
    /// used for testing Azure PowerShell.
    /// </summary>
    public class CIFilterTask : Task
    {
        /// <summary>
        /// Gets or sets the files changed in a given pull request.
        /// </summary>
        [Required]
        public string[] FilesChanged { get; set; }

        /// <summary>
        /// Gets or set the TargetModule, e.g. Storage
        /// </summary>
        public string TargetModule { get; set; }

        /// <summary>
        /// Gets or sets the test assemblies output produced by the task.
        /// </summary>
        [Output]
        public Dictionary<string, HashSet<string>> Result { get; set; }

        private const string TaskMappingConfigName = ".ci-config.yml";

        private const string AllModule = "all";
        private const string SingleModule = "module";

        /// <summary>
        /// Executes the task to generate a list of test assemblies
        /// based on file changes from a specified Pull Request.
        /// The output it produces is said list.
        /// </summary>
        /// <returns> Returns a value indicating wheter the success status of the task. </returns>
        public override bool Execute()
        {
            Result = new Dictionary<string, HashSet<string>>();
            string configPath = Path.GetFullPath(TaskMappingConfigName);
            if (!File.Exists(configPath))
            {
                throw new Exception("CI step config is not found!");
            }
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(new CamelCaseNamingConvention())
                .Build();
            string content = File.ReadAllText(configPath);
            CIStepFilterConfig config = deserializer.Deserialize<CIStepFilterConfig>(content);
            List<(Regex, List<string>)> RuleList = config.Rules.Select(rule => (new Regex(rule.Pattern), rule.Steps)).ToList();

            foreach (string filePath in FilesChanged)
            {
                List<string> steps = new List<string>();
                foreach ((Regex regex, List<string> ruleSteps) in RuleList)
                {
                    if (regex.IsMatch(filePath))
                    {
                        steps = ruleSteps;
                        break;
                    }
                }
                Console.WriteLine(string.Format("{0}: [{1}]", filePath, string.Join(", ", steps)));
                foreach (string step in steps)
                {
                    string stepName = step.Split(':')[0];
                    string scope = step.Split(':')[1];
                    HashSet<string> scopes = Result.ContainsKey(stepName) ? Result[stepName] : new HashSet<string>();
                    if (!scopes.Contains(AllModule))
                    {
                        if (scope.Equals(AllModule))
                        {
                            scopes.Clear();
                            scopes.Add(AllModule);
                        }
                        else if (scope.Equals(SingleModule))
                        {
                            string moduleName = filePath.Split('/')[1];
                            scopes.Add(moduleName);
                        }
                        Result[stepName] = scopes;
                    }
                }
            }


            return true;
        }
    }
}
