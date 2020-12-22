using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.WindowsAzure.Build.Tasks
{
    public class CIFilterTaskResult
    {
        public Dictionary<string, HashSet<string>> Step = new Dictionary<string, HashSet<string>>();
    }
}
