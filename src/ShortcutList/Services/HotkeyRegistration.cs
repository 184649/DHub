namespace ShortcutList.Services;

// A small allowlist makes saved settings predictable and keeps Win32 calls testable.
internal readonly record struct HotkeyBinding(string Name, uint Modifiers, uint Key)
{
    internal const string DefaultName = "Ctrl+Shift+Space";
    internal static readonly HotkeyBinding[] Choices =
    {
        new(DefaultName, 0x0002 | 0x0004, 0x20),
        new("Ctrl+Alt+Space", 0x0002 | 0x0001, 0x20),
        new("Ctrl+Alt+D", 0x0002 | 0x0001, 0x44)
    };
    internal static HotkeyBinding Resolve(string? name) =>
        Choices.FirstOrDefault(x => x.Name == name) is { Name: not null } binding ? binding : Choices[0];
}

internal readonly record struct HotkeyRegistrationResult(bool Enabled, bool SearchRegistered, bool ManagementRegistered)
{
    internal bool HasFailure => Enabled && (!SearchRegistered || !ManagementRegistered);
}

internal sealed class HotkeyRegistration : IDisposable
{
    internal const int SearchId = 0x534C;
    internal const int ManagementId = 0x534D;
    private const uint NoRepeat = 0x4000;
    private readonly Func<int, uint, uint, bool> _register;
    private readonly Action<int> _unregister;
    private readonly HashSet<int> _registered = new();

    internal HotkeyRegistration(Func<int, uint, uint, bool> register, Action<int> unregister)
        { _register = register; _unregister = unregister; }

    internal HotkeyRegistrationResult Apply(bool enabled, string? searchName)
    {
        Dispose();
        if (!enabled) return new(false, false, false);
        var binding = HotkeyBinding.Resolve(searchName);
        var search = Register(SearchId, binding.Modifiers, binding.Key);
        var management = Register(ManagementId, 0x0002 | 0x0004, 0x44);
        return new(true, search, management);
    }

    private bool Register(int id, uint modifiers, uint key)
    {
        if (!_register(id, modifiers | NoRepeat, key)) return false;
        _registered.Add(id);
        return true;
    }

    public void Dispose()
    {
        foreach (var id in _registered) _unregister(id);
        _registered.Clear();
    }
}
