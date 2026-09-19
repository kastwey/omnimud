namespace Omnimud.UI.Tests;

/// <summary>
/// Traits for tests that cannot run everywhere.
/// <para><see cref="InteractiveDesktop"/>: the test needs a real interactive desktop — a window that becomes the
/// ACTIVE one, or an external UI Automation client looking at a window on screen. A developer machine has one;
/// a CI runner does not guarantee it (the job may run in a session without foreground rights), so CI runs
/// <c>dotnet test --filter "Category!=InteractiveDesktop"</c> (see .github/workflows/ci.yml) and these are run
/// locally, where <c>dotnet test Omnimud.sln</c> still runs everything.</para>
/// Every other form test creates its windows without showing them (or shows them off screen without activation)
/// and runs anywhere.
/// </summary>
internal static class TestCategories
{
    public const string Category = "Category";
    public const string InteractiveDesktop = "InteractiveDesktop";
}
