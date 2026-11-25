using HotkeyLens.Core.Models;

namespace HotkeyLens.Core.Tests;

internal static class TestData
{
    public static DiscoveredApplication Application(
        string id,
        string name,
        string? executablePath = null,
        ApplicationCategory category = ApplicationCategory.Utility)
        => new()
        {
            Id = id,
            Name = name,
            ExecutablePath = executablePath,
            Category = category,
            SourceKind = ApplicationSourceKind.StartMenu,
            SourceDetail = "test"
        };

    public static ShortcutEntry Entry(
        DiscoveredApplication application,
        string gestureText,
        ShortcutScope scope = ShortcutScope.Window,
        ShortcutSourceKind sourceKind = ShortcutSourceKind.UserConfiguration,
        ShortcutOrigin origin = ShortcutOrigin.UserConfigured,
        ConfidenceLevel confidence = ConfidenceLevel.High,
        string? action = null,
        string? sequence = null,
        bool isEnabled = true,
        string? context = null)
    {
        var gesture = ShortcutGesture.Parse(gestureText);
        var resolvedAction = action ?? $"动作 {gesture.Canonical}";
        var display = sequence ?? gesture.Canonical;
        var resolvedContext = context ?? (scope == ShortcutScope.Contextual ? "editorTextFocus" : null);
        return new ShortcutEntry
        {
            Id = ShortcutEntry.BuildId(
                application.Id,
                display,
                resolvedAction,
                sourceKind,
                "test-source",
                resolvedContext),
            ApplicationId = application.Id,
            ApplicationName = application.Name,
            Gesture = gesture,
            KeySequence = sequence,
            ActionName = resolvedAction,
            Description = $"{application.Name} 的 {resolvedAction}",
            Scope = scope,
            SourceKind = sourceKind,
            SourceName = "测试来源",
            SourceReference = "test-source",
            Confidence = confidence,
            ConfidenceRationale = "自动化测试构造的数据。",
            Origin = origin,
            Context = resolvedContext,
            IsEnabled = isEnabled,
            CapturedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)
        };
    }
}