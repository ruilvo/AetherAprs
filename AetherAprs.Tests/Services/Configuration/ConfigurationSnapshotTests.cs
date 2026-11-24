// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using AetherAprs.Services.Configuration;
using AetherAprs.Services.Platform;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AetherAprs.Tests.Services.Configuration;

/// <summary>
/// Permanent guard for <see cref="ConfigurationService.GetSettingsSnapshot"/>:
/// every public settable property on the settings graph must survive a
/// snapshot, so adding a setting without teaching the clone about it fails a
/// test instead of silently appearing to have no effect at runtime.
/// </summary>
public sealed class ConfigurationSnapshotTests
{
    [Fact]
    public void GetSettingsSnapshot_PreservesEveryPublicSettableProperty()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            File.WriteAllText(
                Path.Combine(directory, "appsettings.json"),
                "{\"Aprs\":{\"Callsign\":\"N0CALL\"}}");
            var service = new ConfigurationService(
                new TestAppDataDirProvider(directory),
                NullLogger<ConfigurationService>.Instance);

            var counter = 0;
            var assignedPaths = new Dictionary<string, object?>(StringComparer.Ordinal);
            AssignDistinctValues(service.Settings, "Settings", assignedPaths, ref counter);

            var snapshot = service.GetSettingsSnapshot();

            Assert.Contains("Settings.SchemaVersion", assignedPaths);
            Assert.Contains("Settings.Aprs.Callsign", assignedPaths);
            Assert.Contains("Settings.MessageTemplates.Templates[0].Name", assignedPaths);
            AssertSettingsGraphEqual(service.Settings, snapshot, "Settings");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// Sets every public writable property reachable from
    /// <paramref name="target"/> to a value that differs from its default.
    /// Complex properties are replaced with fresh instances, lists receive one
    /// distinct element, and dictionaries receive one distinct entry.
    /// </summary>
    private static void AssignDistinctValues(
        object target,
        string path,
        IDictionary<string, object?> assignedPaths,
        ref int counter)
    {
        foreach (var property in target.GetType().GetProperties(
            BindingFlags.Public | BindingFlags.Instance))
        {
            if (!property.CanWrite || property.GetIndexParameters().Length > 0)
            {
                continue;
            }

            var propertyPath = path + "." + property.Name;
            var propertyType = property.PropertyType;
            var currentValue = property.GetValue(target);

            if (IsScalar(propertyType))
            {
                var value = CreateScalarValue(
                    propertyType,
                    currentValue,
                    ref counter);
                property.SetValue(target, value);
                assignedPaths[propertyPath] = value;
                continue;
            }

            if (IsDictionary(propertyType))
            {
                var dictionary = (IDictionary)Activator.CreateInstance(propertyType)!;
                property.SetValue(target, dictionary);
                counter++;

                var keyType = propertyType.GetGenericArguments()[0];
                var valueType = propertyType.GetGenericArguments()[1];
                var key = CreateScalarValue(keyType, null, ref counter);
                var keyText = key?.ToString() ?? "null";
                var value = IsScalar(valueType)
                    ? CreateScalarValue(valueType, null, ref counter)
                    : CreatePopulatedInstance(
                        valueType,
                        propertyPath + "[" + keyText + "]",
                        assignedPaths,
                        ref counter);
                dictionary.Add(key!, value);
                assignedPaths[propertyPath] = dictionary;
                continue;
            }

            if (IsList(propertyType))
            {
                var list = (IList)Activator.CreateInstance(propertyType)!;
                property.SetValue(target, list);

                var elementType = propertyType.GetGenericArguments()[0];
                var element = IsScalar(elementType)
                    ? CreateScalarValue(elementType, null, ref counter)
                    : CreatePopulatedInstance(
                        elementType,
                        propertyPath + "[0]",
                        assignedPaths,
                        ref counter);
                list.Add(element);
                assignedPaths[propertyPath] = list;
                continue;
            }

            var instance = currentValue ?? Activator.CreateInstance(propertyType)!;
            property.SetValue(target, instance);
            assignedPaths[propertyPath] = instance;
            AssignDistinctValues(instance, propertyPath, assignedPaths, ref counter);
        }
    }

    private static object CreatePopulatedInstance(
        Type type,
        string path,
        IDictionary<string, object?> assignedPaths,
        ref int counter)
    {
        var instance = Activator.CreateInstance(type)!;
        AssignDistinctValues(instance, path, assignedPaths, ref counter);
        return instance;
    }

    private static object? CreateScalarValue(
        Type type,
        object? currentValue,
        ref int counter)
    {
        counter++;

        var underlyingType = Nullable.GetUnderlyingType(type);
        var valueType = underlyingType ?? type;

        if (valueType == typeof(string))
        {
            // Six uppercase alphanumeric characters satisfy the callsign
            // validation while remaining a valid APRS address everywhere else.
            return "T" + counter.ToString("D5");
        }

        if (valueType == typeof(bool))
        {
            return !(bool)(currentValue ?? false);
        }

        if (valueType == typeof(int))
        {
            return 100_001 + counter;
        }

        if (valueType == typeof(long))
        {
            return 100_001L + counter;
        }

        if (valueType == typeof(decimal))
        {
            return 100_001.5m + counter;
        }

        if (valueType == typeof(double))
        {
            return 100_001.5 + counter;
        }

        if (valueType.IsEnum)
        {
            // Walk from the end: SymbolCode's first member (Space) is not a
            // representable APRS symbol, while the last members are.
            var values = Enum.GetValues(valueType);
            for (var index = values.Length - 1; index >= 0; index--)
            {
                var candidate = values.GetValue(index);
                if (currentValue is null || !Equals(candidate, currentValue))
                {
                    return candidate;
                }
            }

            throw new InvalidOperationException(
                $"No alternative value exists for enum {valueType.Name}.");
        }

        throw new InvalidOperationException(
            $"The snapshot guard does not know how to assign {type.Name}.");
    }

    private static void AssertSettingsGraphEqual(
        object expected,
        object actual,
        string path)
    {
        Assert.NotNull(actual);

        foreach (var property in expected.GetType().GetProperties(
            BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetIndexParameters().Length > 0)
            {
                continue;
            }

            var propertyPath = path + "." + property.Name;
            var expectedValue = property.GetValue(expected);
            var actualValue = property.GetValue(actual);
            var propertyType = property.PropertyType;

            if (IsScalar(propertyType))
            {
                Assert.Equal(expectedValue, actualValue);
                continue;
            }

            if (IsDictionary(propertyType))
            {
                AssertDictionaryEqual(expectedValue, actualValue, propertyPath);
                continue;
            }

            if (IsList(propertyType))
            {
                AssertListEqual(expectedValue, actualValue, propertyPath);
                continue;
            }

            Assert.NotNull(expectedValue);
            Assert.NotNull(actualValue);
            AssertSettingsGraphEqual(expectedValue!, actualValue!, propertyPath);
        }
    }

    private static void AssertDictionaryEqual(
        object? expected,
        object? actual,
        string path)
    {
        var expectedDictionary = Assert.IsAssignableFrom<IDictionary>(expected);
        var actualDictionary = Assert.IsAssignableFrom<IDictionary>(actual);
        Assert.Equal(expectedDictionary.Count, actualDictionary.Count);

        foreach (DictionaryEntry entry in expectedDictionary)
        {
            Assert.True(
                actualDictionary.Contains(entry.Key),
                $"{path} is missing key '{entry.Key}'.");
            AssertValueEqual(
                entry.Value,
                actualDictionary[entry.Key],
                path + "[" + entry.Key + "]");
        }
    }

    private static void AssertListEqual(object? expected, object? actual, string path)
    {
        var expectedList = Assert.IsAssignableFrom<IList>(expected);
        var actualList = Assert.IsAssignableFrom<IList>(actual);
        Assert.Equal(expectedList.Count, actualList.Count);

        for (var index = 0; index < expectedList.Count; index++)
        {
            AssertValueEqual(
                expectedList[index],
                actualList[index],
                path + "[" + index + "]");
        }
    }

    private static void AssertValueEqual(object? expected, object? actual, string path)
    {
        if (expected is null || actual is null || IsScalar(expected.GetType()))
        {
            Assert.Equal(expected, actual);
            return;
        }

        AssertSettingsGraphEqual(expected, actual, path);
    }

    private static bool IsScalar(Type type)
    {
        var valueType = Nullable.GetUnderlyingType(type) ?? type;

        return valueType.IsEnum
            || valueType.IsPrimitive
            || valueType == typeof(string)
            || valueType == typeof(decimal)
            || valueType == typeof(DateTime)
            || valueType == typeof(DateTimeOffset)
            || valueType == typeof(Guid)
            || valueType == typeof(TimeSpan);
    }

    private static bool IsDictionary(Type type) =>
        typeof(IDictionary).IsAssignableFrom(type);

    private static bool IsList(Type type) =>
        typeof(IList).IsAssignableFrom(type);

    private static string CreateTemporaryDirectory()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"AetherAprsSnapshot-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private sealed class TestAppDataDirProvider(string directory) : IAppDataDirProviderService
    {
        public string GetAppDataDirectory() => directory;

        public string GetPrivateAppDataDirectory() => directory;
    }
}
