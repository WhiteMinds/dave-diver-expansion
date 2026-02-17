using System;
using System.Reflection;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace DaveDiverExpansion.Helpers;

/// <summary>
/// Utility methods for working with IL2CPP types and Unity objects.
/// </summary>
public static class Il2CppHelper
{
    /// <summary>
    /// Get a private/internal field value from an IL2CPP object using reflection.
    /// </summary>
    public static T GetFieldValue<T>(object obj, string fieldName)
    {
        if (obj == null) return default;

        var field = obj.GetType().GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public
        );
        if (field == null)
        {
            Plugin.Log.LogWarning($"Field '{fieldName}' not found on type {obj.GetType().Name}");
            return default;
        }

        return (T)field.GetValue(obj);
    }

    /// <summary>
    /// Find all active objects of a given type in the current scene.
    /// Uses Object.FindObjectsOfType which works with IL2CPP interop types.
    /// </summary>
    public static T[] FindAll<T>() where T : UnityEngine.Object
    {
        return UnityEngine.Object.FindObjectsOfType<T>();
    }
}
