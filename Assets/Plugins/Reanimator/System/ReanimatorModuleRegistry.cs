#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

internal static class ReanimatorModuleRegistry
{
    private static List<IReanimatorModule> modules;

    public static IReadOnlyList<IReanimatorModule> Modules
    {
        get
        {
            if (modules == null)
                modules = DiscoverModules();
            return modules;
        }
    }

    private static List<IReanimatorModule> DiscoverModules()
    {
        var result = new List<IReanimatorModule>();
        var moduleType = typeof(IReanimatorModule);
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        for (var assemblyIndex = 0; assemblyIndex < assemblies.Length; assemblyIndex++)
        {
            Type[] types;
            try
            {
                types = assemblies[assemblyIndex].GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types;
            }
            catch
            {
                continue;
            }

            if (types == null)
                continue;

            for (var typeIndex = 0; typeIndex < types.Length; typeIndex++)
            {
                var type = types[typeIndex];
                if (type == null || type.IsAbstract || type.IsInterface)
                    continue;
                if (!moduleType.IsAssignableFrom(type))
                    continue;
                if (type.GetConstructor(Type.EmptyTypes) == null)
                    continue;

                try
                {
                    if (Activator.CreateInstance(type) is IReanimatorModule module)
                        result.Add(module);
                }
                catch (Exception ex)
                {
                    ReanimatorLog.Error($"[Reanimator] Failed to create module {type.FullName}: {ex.Message}");
                }
            }
        }

        return result
            .GroupBy(module => module.Id ?? string.Empty)
            .Select(group => group.First())
            .OrderBy(module => module.SectionOrder)
            .ThenBy(module => module.ButtonOrder)
            .ThenBy(module => module.Id, StringComparer.Ordinal)
            .ToList();
    }
}
#endif
