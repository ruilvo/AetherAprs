// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using AetherAprs.Configuration.Settings;
using AetherAprs.Configuration.Settings.Documents;
using Xunit;

namespace AetherAprs.Tests.Services.Configuration;

/// <summary>
/// Guards the two properties the settings graph relies on: the published
/// snapshot carries no mutable state, and the load-time document still mirrors
/// every snapshot property. Adding a setting to one without the other fails
/// here instead of silently ignoring the value at runtime.
/// </summary>
public sealed class ConfigurationSnapshotTests
{
    private static readonly (Type Snapshot, Type Document)[] MirroredGraph =
    [
        (typeof(AppSettings), typeof(AppSettingsDocument)),
        (typeof(AprsSettings), typeof(AprsSettingsDocument)),
        (typeof(MapSettings), typeof(MapSettingsDocument)),
        (typeof(MessageTemplatesSettings), typeof(MessageTemplatesSettingsDocument)),
        (typeof(MessageTemplate), typeof(MessageTemplateDocument)),
    ];

    [Theory]
    [MemberData(nameof(MirroredTypes))]
    public void SettingsSnapshot_ExposesNoMutableState(Type snapshotType)
    {
        List<string> mutableMembers = [];

        foreach (PropertyInfo property in PublicInstanceProperties(snapshotType))
        {
            MethodInfo? setter = property.GetSetMethod();
            if (setter is null)
            {
                continue;
            }

            bool isInitOnly = Array.IndexOf(
                setter.ReturnParameter.GetRequiredCustomModifiers(),
                typeof(IsExternalInit)) >= 0;

            if (!isInitOnly)
            {
                mutableMembers.Add($"{snapshotType.Name}.{property.Name}");
            }
        }

        Assert.Empty(mutableMembers);
    }

    [Theory]
    [MemberData(nameof(MirroredPairs))]
    public void SettingsDocument_CarriesEverySnapshotProperty(Type snapshotType, Type documentType)
    {
        List<string> missing = [];

        foreach (PropertyInfo property in PublicInstanceProperties(snapshotType))
        {
            PropertyInfo? documentProperty = documentType.GetProperty(
                property.Name,
                BindingFlags.Public | BindingFlags.Instance);

            if (documentProperty is null
                || !documentProperty.CanWrite
                || !AreCompatible(property.PropertyType, documentProperty.PropertyType))
            {
                missing.Add(property.Name);
            }
        }

        Assert.Empty(missing);
    }

    public static TheoryData<Type> MirroredTypes()
    {
        TheoryData<Type> data = [];
        foreach ((Type? Snapshot, Type _) in MirroredGraph)
        {
            data.Add(Snapshot);
        }

        return data;
    }

    public static TheoryData<Type, Type> MirroredPairs()
    {
        TheoryData<Type, Type> data = [];
        foreach ((Type? Snapshot, Type? Document) in MirroredGraph)
        {
            data.Add(Snapshot, Document);
        }

        return data;
    }

    private static IEnumerable<PropertyInfo> PublicInstanceProperties(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance);

    /// <summary>
    /// The document is allowed to hold a mutable collection where the snapshot
    /// holds a read-only view of it, and a nested section is allowed to use its
    /// own document type; every other mismatch is a mapping bug.
    /// </summary>
    private static bool AreCompatible(Type snapshotType, Type documentType)
    {
        if (snapshotType == documentType)
        {
            return true;
        }

        if (DocumentFor.TryGetValue(snapshotType, out Type? expectedDocument))
        {
            return documentType == expectedDocument;
        }

        if (!snapshotType.IsGenericType || !documentType.IsGenericType)
        {
            return false;
        }

        return IsSequence(snapshotType) && IsSequence(documentType)
            || IsMap(snapshotType) && IsMap(documentType);
    }

    private static readonly Dictionary<Type, Type> DocumentFor =
        MirroredGraph.ToDictionary(pair => pair.Snapshot, pair => pair.Document);

    private static bool IsSequence(Type type) =>
        type.IsGenericType
        && type.GetGenericArguments().Length == 1
        && typeof(IEnumerable).IsAssignableFrom(type);

    private static bool IsMap(Type type) =>
        type.IsGenericType && type.GetGenericArguments().Length == 2;
}
