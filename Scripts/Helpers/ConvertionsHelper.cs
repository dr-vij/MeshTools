using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;
using Unity.Collections;
using LibTessDotNet;
using System;

public static class ConvertionsHelper
{
    public static NativeArray<T> ToNativeArray<T>(this T[] sourceArray, Allocator allocator) where T : struct
    {
        return new NativeArray<T>(sourceArray, allocator);
    }

    public static Dictionary<TKey, TValue> ToDictionary<TKey, TValue>(this NativeHashMap<TKey, TValue> initialNativeHashMap) where TKey : struct, IEquatable<TKey> where TValue : struct
    {
        var retDict = new Dictionary<TKey, TValue>(initialNativeHashMap.Count());
        using (var enumerator = initialNativeHashMap.GetEnumerator())
            while (enumerator.MoveNext())
                retDict.Add(enumerator.Current.Key, enumerator.Current.Value);
        return retDict;
    }

    public static HashSet<T> ToHashSet<T>(this NativeHashSet<T> initialHashset) where T : unmanaged, IEquatable<T>
    {
        var retHashSet = new HashSet<T>();
        using (var enumerator = initialHashset.GetEnumerator())
            while (enumerator.MoveNext())
                retHashSet.Add(enumerator.Current);
        return retHashSet;
    }
}