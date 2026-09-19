using System.Reflection;
using Omnimud.Core.Options;

namespace Omnimud.UI.Tests.Presenters;

/// <summary>In-memory <see cref="IOptionsService"/> with the real inheritance rules, recording what is written.</summary>
internal sealed class FakeOptionsService : IOptionsService
{
    private readonly Dictionary<(OptionScope, int?), OmnimudOptions> _blocks = [];

    public List<(OptionScope Scope, int? ScopeId, OmnimudOptions Options)> Saved { get; } = [];
    public List<(OptionScope Scope, int? ScopeId)> Resets { get; } = [];
    public event EventHandler<OptionsChangedEventArgs>? Changed;

    public FakeOptionsService With(OptionScope scope, int? scopeId, OmnimudOptions options)
    {
        _blocks[(scope, scope == OptionScope.Global ? null : scopeId)] = options;
        return this;
    }

    public Task<OmnimudOptions> ResolveAsync(int? mudId, int? characterId, CancellationToken ct = default)
    {
        if (characterId is not null && _blocks.TryGetValue((OptionScope.Character, characterId), out var c)) return Task.FromResult(c);
        if (mudId is not null && _blocks.TryGetValue((OptionScope.Mud, mudId), out var m)) return Task.FromResult(m);
        return Task.FromResult(_blocks.TryGetValue((OptionScope.Global, null), out var g) ? g : new OmnimudOptions());
    }

    public Task<bool> HasOwnOptionsAsync(OptionScope scope, int? scopeId, CancellationToken ct = default) =>
        Task.FromResult(_blocks.ContainsKey((scope, scope == OptionScope.Global ? null : scopeId)));

    public Task SaveAsync(OptionScope scope, int? scopeId, OmnimudOptions options, CancellationToken ct = default)
    {
        With(scope, scopeId, options);
        Saved.Add((scope, scopeId, options));
        Changed?.Invoke(this, new OptionsChangedEventArgs(scope, scopeId));
        return Task.CompletedTask;
    }

    public Task ResetToInheritedAsync(OptionScope scope, int? scopeId, CancellationToken ct = default)
    {
        _blocks.Remove((scope, scope == OptionScope.Global ? null : scopeId));
        Resets.Add((scope, scopeId));
        Changed?.Invoke(this, new OptionsChangedEventArgs(scope, scopeId));
        return Task.CompletedTask;
    }
}

/// <summary>Options built by reflection, so a property added to <see cref="OmnimudOptions"/> takes part in the tests by itself.</summary>
internal static class OptionsSamples
{
    public static IReadOnlyList<PropertyInfo> Properties { get; } = typeof(OmnimudOptions)
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(p => p.CanRead && p.SetMethod is not null && p.GetIndexParameters().Length == 0)
        .ToArray();

    /// <summary>Every property different from its default, and the whole block valid for the dialog.</summary>
    public static OmnimudOptions AllNonDefault()
    {
        var options = new OmnimudOptions();
        foreach (var property in Properties)
            property.SetValue(options, NonDefaultValue(property));
        return options;
    }

    /// <summary>The defaults with ONE property changed.</summary>
    public static OmnimudOptions WithOnly(PropertyInfo property)
    {
        var options = new OmnimudOptions();
        property.SetValue(options, NonDefaultValue(property));
        return options;
    }

    public static object NonDefaultValue(PropertyInfo property)
    {
        var current = property.GetValue(OmnimudOptions.Default);
        var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;

        if (type == typeof(bool)) return !(bool)current!;
        if (type == typeof(int)) return (int)current! > 0 ? (int)current - 1 : 1;
        if (type == typeof(float)) return (float)current! + 2.5f;
        if (type == typeof(char)) return property.Name == nameof(OmnimudOptions.ConcatChar) ? '|' : property.Name == nameof(OmnimudOptions.RepeatChar) ? '*' : '~';
        if (type.IsEnum) return Enum.GetValues(type).Cast<object>().Last(v => !v.Equals(current));
        if (type == typeof(string))
        {
            return property.Name switch
            {
                nameof(OmnimudOptions.Language) => "es",
                nameof(OmnimudOptions.FontFamily) => "Arial",
                nameof(OmnimudOptions.LogDirectory) => Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar),
                nameof(OmnimudOptions.ProxyHost) => "proxy.example.org",
                _ => "value of " + property.Name
            };
        }
        throw new NotSupportedException($"OptionsSamples does not know how to build a {type.Name} for {property.Name}: teach it.");
    }
}
