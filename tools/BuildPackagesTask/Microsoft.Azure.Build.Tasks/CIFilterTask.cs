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
    using System.Runtime.InteropServices;
    using System.Text.RegularExpressions;
    using Serilog;

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
        /// Gets or set the Mode, e.g. Release
        /// </summary>
        [Required]
        public string Mode { get; set; }

        /// <summary>
        ///  Gets or sets the path to the files-to-module map.
        /// </summary>
        [Required]
        public string ModuleMapFilePath { get; set; }

        /// <summary>
        ///  Gets or sets the path to the files-to-csproj map.
        /// </summary>
        [Required]
        public string CsprojMapFilePath { get; set; }

        /// <summary>
        /// Gets or sets the test assemblies output produced by the task.
        /// </summary>
        [Output]
        public CIFilterTaskResult FilterTaskResult { get; set; }

        private const string TaskMappingConfigName = ".ci-config.yml";

        private const string AllModule = "all";
        private const string SingleModule = "module";

        private const string BUILD_PHASE = "build";
        private const string ANALYSIS_BREAKING_CHANGE_PHASE = "breaking-change";
        private const string ANALYSIS_HELP_PHASE = "help";
        private const string ANALYSIS_DEPENDENCY_PHASE = "dependency";
        private const string ANALYSIS_SIGNATURE_PHASE = "signature";
        private const string TEST_PHASE = "test";

        private Dictionary<string, string[]> ReadMapFile(string mapFilePath, string mapFileName)
        {
            if (mapFilePath == null)
            {
                throw new ArgumentNullException(string.Format("The {0} cannot be null.", mapFileName));
            }

            if (!File.Exists(mapFilePath))
            {
                throw new FileNotFoundException(string.Format("The {0} provided could not be found. Please provide a valid MapFilePath.", mapFileName));
            }

            return JsonConvert.DeserializeObject<Dictionary<string, string[]>>(File.ReadAllText(mapFilePath));
        }

        private List<string> GetRelatedCsprojList(string moduleName, Dictionary<string, string[]> csprojMap)
        {
            List<string> csprojList = new List<string>();

            if (csprojMap.ContainsKey(moduleName))
            {
                csprojList.AddRange(csprojMap[moduleName]);
            }
            else
            {
                string expectKey = string.Format("src/{0}/", moduleName);
                foreach (string key in csprojMap.Keys)
                {
                    if (key.ToLower().Equals(expectKey.ToLower()))
                    {
                        csprojList.AddRange(csprojMap[key]);
                    }
                }
            }

            return csprojList;
        }

        private List<string> GenerateBuildCsprojList(string moduleName, Dictionary<string, string[]> csprojMap)
        {
            return GetRelatedCsprojList(moduleName, csprojMap)
                .Where(x => !x.Contains("Test")).ToList();
        }

        private string GetModuleNameFromCsprojPath(string csprojPath)
        {
            return csprojPath.Replace('/', '\\')
                .Split(new string[] { "src\\" }, StringSplitOptions.None)[1]
                .Split('\\')[0];
        }

        private List<string> GenerateAnalysisModuleList(string moduleName, Dictionary<string, string[]> csprojMap)
        {
            return GetRelatedCsprojList(moduleName, csprojMap)
                .Select(GetModuleNameFromCsprojPath)
                .Distinct()
                .ToList();
        }

        private List<string> GenerateTestCsprojList(string moduleName, Dictionary<string, string[]> csprojMap)
        {
            return GetRelatedCsprojList(moduleName, csprojMap)
                .Where(x => x.Contains("Test")).ToList();
        }

        private bool ProcessTargetModule(Dictionary<string, string[]> csprojMap)
        {
            Dictionary<string, HashSet<string>> influencedModuleInfo = new Dictionary<string, HashSet<string>>
            {
                [BUILD_PHASE] = new HashSet<string>(from x in GenerateBuildCsprojList(TargetModule, csprojMap).ToList() select x),
                [ANALYSIS_BREAKING_CHANGE_PHASE] = new HashSet<string>(from x in GenerateAnalysisModuleList(TargetModule, csprojMap).ToList() select x),
                [ANALYSIS_DEPENDENCY_PHASE] = new HashSet<string>(from x in GenerateAnalysisModuleList(TargetModule, csprojMap).ToList() select x),
                [ANALYSIS_HELP_PHASE] = new HashSet<string>(from x in GenerateAnalysisModuleList(TargetModule, csprojMap).ToList() select x),
                [ANALYSIS_SIGNATURE_PHASE] = new HashSet<string>(from x in GenerateAnalysisModuleList(TargetModule, csprojMap).ToList() select x),
                [TEST_PHASE] = new HashSet<string>(from x in GenerateTestCsprojList(TargetModule, csprojMap).ToList() select x)
            };

            foreach (string stepName in influencedModuleInfo.Keys)
            {
                Serilog.Log.Information("-----------------------------------");
                Serilog.Log.Information(string.Format("{0}: [{1}]", stepName, string.Join(", ", influencedModuleInfo[stepName].ToList())));
            }

            FilterTaskResult.Step = influencedModuleInfo;
            
            return true;
        }

        private string ProcessSinglePattern(string pattern)
        {
            return pattern.Replace("**", ".*");
        }

        private bool ProcessFileChanged(Dictionary<string, string[]> csprojMap)
        {
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
            List<(Regex, List<string>)> RuleList = config.Rules.Select(rule => (new Regex(string.Join("|", rule.Patterns.Select(ProcessSinglePattern))), rule.Steps)).ToList();
            Dictionary<string, HashSet<string>> influencedModuleInfo = new Dictionary<string, HashSet<string>>();

            DateTime startTime = DateTime.Now;
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
                foreach (string step in steps)
                {
                    string stepName = step.Split(':')[0];
                    string scope = step.Split(':')[1];
                    HashSet<string> scopes = influencedModuleInfo.ContainsKey(stepName) ? influencedModuleInfo[stepName] : new HashSet<string>();
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
                        else
                        {
                            scopes.Add(scope);
                        }
                        influencedModuleInfo[stepName] = scopes;
                    }
                }
            }
            DateTime endOfRegularExpressionTime = DateTime.Now;

            foreach (string stepName in influencedModuleInfo.Keys)
            {
                if (stepName.Equals(BUILD_PHASE))
                {
                    HashSet<string> csprojSet = new HashSet<string>();
                    foreach (string moduleName in influencedModuleInfo[stepName])
                    {
                        csprojSet.UnionWith(GenerateBuildCsprojList(moduleName, csprojMap));
                    }
                    foreach (string filename in Directory.GetFiles(@"src/Accounts", "*.csproj", SearchOption.AllDirectories))
                    {
                        csprojSet.Add(filename);
                    }
                    influencedModuleInfo[stepName] = csprojSet;
                }
                else if (stepName.Equals(ANALYSIS_BREAKING_CHANGE_PHASE) ||
                    stepName.Equals(ANALYSIS_DEPENDENCY_PHASE) ||
                    stepName.Equals(ANALYSIS_HELP_PHASE) ||
                    stepName.Equals(ANALYSIS_SIGNATURE_PHASE))
                {
                    HashSet<string> moduleSet = new HashSet<string>();
                    foreach (string moduleName in influencedModuleInfo[stepName])
                    {
                        moduleSet.UnionWith(GenerateAnalysisModuleList(moduleName, csprojMap));
                    }
                    influencedModuleInfo[stepName] = moduleSet;
                }
                else if (stepName.Equals(TEST_PHASE))
                {
                    HashSet<string> csprojSet = new HashSet<string>();
                    foreach (string moduleName in influencedModuleInfo[stepName])
                    {
                        csprojSet.UnionWith(GenerateTestCsprojList(moduleName, csprojMap));
                    }
                    csprojSet.Add("tools/TestFx/TestFx.csproj");
                    influencedModuleInfo[stepName] = csprojSet;
                }
            }
            foreach (string stepName in influencedModuleInfo.Keys)
            {
                Serilog.Log.Information("-----------------------------------");
                Serilog.Log.Information(string.Format("{0}: [{1}]", stepName, string.Join(", ", influencedModuleInfo[stepName].ToList())));
            }
            DateTime endTime = DateTime.Now;
            Serilog.Log.Information(string.Format("Takes {0} seconds for RE match, {1} seconds for phase config.", (endOfRegularExpressionTime - startTime).TotalSeconds, (endTime - endOfRegularExpressionTime).TotalSeconds));
            //Console.WriteLine(string.Format("Count: {0}", FilterTaskResult.MetadataCount));

            FilterTaskResult.Step = influencedModuleInfo;

            return true;
        }

        /// <summary>
        /// Executes the task to generate a list of test assemblies
        /// based on file changes from a specified Pull Request.
        /// The output it produces is said list.
        /// </summary>
        /// <returns> Returns a value indicating wheter the success status of the task. </returns>
        public override bool Execute()
        {
            Serilog.Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.Console(outputTemplate: "[{Level:u3}] {Message}{NewLine}{Exception}")
                .WriteTo.File("logs\\.log", rollingInterval: RollingInterval.Day)
                .CreateLogger();
            FilterTaskResult = new CIFilterTaskResult();

            var csprojMap = ReadMapFile(CsprojMapFilePath, "CsprojMapFilePath");
            var moduleMap = ReadMapFile(ModuleMapFilePath, "ModuleMapFilePath");

            Serilog.Log.Debug(string.Format("FilesChanged: {0}", FilesChanged.Length));
            if (FilesChanged != null && FilesChanged.Length > 0)
            {
                return ProcessFileChanged(csprojMap);
            }
            else if (!string.IsNullOrWhiteSpace(TargetModule))
            {
                return ProcessTargetModule(csprojMap);
            }
            else
            {
            }
            return true;
        }
    }
}
