namespace RegistryToJson.Core;

public sealed class RegistryDiffService
{
    public RegistryDiffResult Compare(RegistrySnapshot baseline, RegistrySnapshot current)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(current);

        var entries = new List<RegistryDiffEntry>();
        CompareNodes(baseline.Root, current.Root, entries);

        return new RegistryDiffResult
        {
            ComparedAtUtc = DateTime.UtcNow,
            Entries = entries
                .OrderBy(static entry => entry.Path, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                .ToList(),
        };
    }

    private static void CompareNodes(RegistryNodeSnapshot? baseline, RegistryNodeSnapshot? current, List<RegistryDiffEntry> entries)
    {
        if (baseline is null && current is null)
        {
            return;
        }

        if (baseline is null && current is not null)
        {
            entries.Add(new RegistryDiffEntry { ChangeType = RegistryChangeType.Added, ItemType = "Key", Path = current.FullPath });
            AddDescendants(current, RegistryChangeType.Added, entries);
            return;
        }

        if (baseline is not null && current is null)
        {
            entries.Add(new RegistryDiffEntry { ChangeType = RegistryChangeType.Removed, ItemType = "Key", Path = baseline.FullPath });
            AddDescendants(baseline, RegistryChangeType.Removed, entries);
            return;
        }

        CompareValues(baseline!, current!, entries);

        var baselineChildren = baseline!.Children.ToDictionary(static child => child.Name, StringComparer.OrdinalIgnoreCase);
        var currentChildren = current!.Children.ToDictionary(static child => child.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var childName in baselineChildren.Keys.Union(currentChildren.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(static item => item, StringComparer.OrdinalIgnoreCase))
        {
            baselineChildren.TryGetValue(childName, out var baselineChild);
            currentChildren.TryGetValue(childName, out var currentChild);
            CompareNodes(baselineChild, currentChild, entries);
        }
    }

    private static void CompareValues(RegistryNodeSnapshot baseline, RegistryNodeSnapshot current, List<RegistryDiffEntry> entries)
    {
        var baselineValues = baseline.Values.ToDictionary(static value => value.Name, StringComparer.OrdinalIgnoreCase);
        var currentValues = current.Values.ToDictionary(static value => value.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var valueName in baselineValues.Keys.Union(currentValues.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(static item => item, StringComparer.OrdinalIgnoreCase))
        {
            baselineValues.TryGetValue(valueName, out var baselineValue);
            currentValues.TryGetValue(valueName, out var currentValue);

            if (baselineValue is null && currentValue is not null)
            {
                entries.Add(new RegistryDiffEntry { ChangeType = RegistryChangeType.Added, ItemType = "Value", Path = current.FullPath, Name = currentValue.Name, NewValue = currentValue.Data });
                continue;
            }

            if (baselineValue is not null && currentValue is null)
            {
                entries.Add(new RegistryDiffEntry { ChangeType = RegistryChangeType.Removed, ItemType = "Value", Path = baseline.FullPath, Name = baselineValue.Name, OldValue = baselineValue.Data });
                continue;
            }

            if (baselineValue is not null && currentValue is not null
                && (!string.Equals(baselineValue.Kind, currentValue.Kind, StringComparison.Ordinal)
                    || !string.Equals(baselineValue.Data, currentValue.Data, StringComparison.Ordinal)))
            {
                entries.Add(new RegistryDiffEntry
                {
                    ChangeType = RegistryChangeType.Modified,
                    ItemType = "Value",
                    Path = current.FullPath,
                    Name = currentValue.Name,
                    OldValue = baselineValue.Data,
                    NewValue = currentValue.Data,
                });
            }
        }
    }

    private static void AddDescendants(RegistryNodeSnapshot node, RegistryChangeType changeType, List<RegistryDiffEntry> entries)
    {
        foreach (var value in node.Values)
        {
            entries.Add(new RegistryDiffEntry
            {
                ChangeType = changeType,
                ItemType = "Value",
                Path = node.FullPath,
                Name = value.Name,
                OldValue = changeType == RegistryChangeType.Removed ? value.Data : null,
                NewValue = changeType == RegistryChangeType.Added ? value.Data : null,
            });
        }

        foreach (var child in node.Children)
        {
            entries.Add(new RegistryDiffEntry { ChangeType = changeType, ItemType = "Key", Path = child.FullPath });
            AddDescendants(child, changeType, entries);
        }
    }
}