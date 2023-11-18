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

using System.Collections.Concurrent;

namespace MultiMSIProcessor.FunctionCollections
{
    public static class EachScanList
    {
        public static IEnumerable<TResult> SelectMany<TSource, TCollection, TResult>(this IEnumerable<TSource> source, Func<TSource, IEnumerable<TCollection>> collectionSelector, Func<TSource, TCollection, int, TResult> resultSelector)
        {
            foreach (TSource sourceCurent in source)
            {
                int resultSectorIndex = -1;
                foreach (TCollection resultCurrent in collectionSelector(sourceCurent))
                {
                    resultSectorIndex++;
                    yield return resultSelector(sourceCurent, resultCurrent, resultSectorIndex);
                }
            }
            yield break;
        }

        public static ConcurrentDictionary<TKey, TValue> ToConcurrentDictionary<TKey, TValue>(
        this IEnumerable<KeyValuePair<TKey, TValue>> source)
        {
            return new ConcurrentDictionary<TKey, TValue>(source);
        }

        public static ConcurrentDictionary<TKey, TValue> ToConcurrentDictionary<TKey, TValue>(
            this IEnumerable<TValue> source, Func<TValue, TKey> keySelector)
        {
            return new ConcurrentDictionary<TKey, TValue>(
                from v in source
                select new KeyValuePair<TKey, TValue>(keySelector(v), v));
        }

        public static ConcurrentDictionary<TKey, TElement> ToConcurrentDictionary<TKey, TValue, TElement>(
            this IEnumerable<TValue> source, Func<TValue, TKey> keySelector, Func<TValue, TElement> elementSelector)
        {
            return new ConcurrentDictionary<TKey, TElement>(
                from v in source
                select new KeyValuePair<TKey, TElement>(keySelector(v), elementSelector(v)));
        }

    }
}
