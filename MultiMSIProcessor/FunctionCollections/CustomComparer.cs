// Copyright 2023 Siwei Bi, Manjiangcuo Wang, and Dan Du
// 
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// you may obtain a copy of the License at
// 
//      http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

// inspired by the https://stackoverflow.com/questions/9988937/sort-string-numbers

using System.Text.RegularExpressions;

namespace MultiMSIProcessor.FunctionCollections
{
    public class CustomComparer : IComparer<string>
    {
        public int Compare(string x, string y)
        {
            var regex = new Regex(@".+[a-zA-Z\\]([0-9].*)(\.raw)");

            // run the regex on both strings
            var xRegexResult = regex.Match(x);
            var yRegexResult = regex.Match(y);

            // check if they are both numbers
            if (xRegexResult.Success && yRegexResult.Success)
            {
                return int.Parse(xRegexResult.Groups[1].Value).CompareTo(int.Parse(yRegexResult.Groups[1].Value));
            }

            // otherwise return as string comparison
            return x.CompareTo(y);
        }
    }
}
