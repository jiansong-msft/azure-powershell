using Microsoft.Build.Framework;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

namespace Microsoft.WindowsAzure.Build.Tasks
{
    public class CIFilterTaskResult : ITaskItem
    {
        string _spec = "12312365456";
        public Dictionary<string, HashSet<string>> Step = new Dictionary<string, HashSet<string>>();


        public string ItemSpec
        {
            get { return _spec; }
            set { }
        }

        public ICollection MetadataNames
        {
            get
            {
                return Step.Keys;
            }
        }
        public int MetadataCount
        {
            get { return Step.Keys.Count; }
        }

        public IDictionary CloneCustomMetadata()
        {
            Dictionary<string, string> Result = new Dictionary<string, string>();

            foreach (string key in Step.Keys)
            {
                Result[key] = string.Join(";", Step[key].ToList());
            }

            return Result;
        }

        public void CopyMetadataTo(ITaskItem destinationItem)
        {
        }

        public string GetMetadata(string metadataName)
        {
            return string.Format("[{0}]", string.Join(", ", Step[metadataName].ToList()));
        }

        public void RemoveMetadata(string metadataName)
        {
        }

        public void SetMetadata(string metadataName, string metadataValue)
        {
        }
    }
}
