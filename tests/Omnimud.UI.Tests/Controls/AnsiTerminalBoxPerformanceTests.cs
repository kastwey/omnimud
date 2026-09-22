using System.Diagnostics;
using FluentAssertions;
using Omnimud.Core.Text;
using Omnimud.UI.Controls;

namespace Omnimud.UI.Tests.Controls;

/// <summary>
/// The original client slowed down as the received text grew. The v2 box keeps at most MaxLines lines
/// (10,000 by default) and appends through the Text Object Model, so a long session must stay usable.
/// </summary>
public class AnsiTerminalBoxPerformanceTests
{
    [Fact]
    public void Appending_TenThousandLines_StaysFast_AndTrimmingKeepsTheBoxBounded() => Sta.Run(() =>
    {
        using var form = new Form { ShowInTaskbar = false };
        using var box = new AnsiTerminalBox { Dock = DockStyle.Fill, MaxLines = 10_000 };
        form.Controls.Add(box);
        form.Show();
        StyledSegment[] line = [new StyledSegment("Un orco enorme te mira con odio desde la esquina de la plaza.", AnsiStyle.Default)];

        var watch = Stopwatch.StartNew();
        for (var i = 0; i < 10_000; i++) box.AppendLine(line);
        var fill = watch.Elapsed;

        watch.Restart();
        for (var i = 0; i < 2_000; i++) box.AppendLine(line);
        var steady = watch.Elapsed;

        box.LineCount.Should().BeLessThanOrEqualTo(10_000 + 1_000, "old lines are trimmed in batches");
        // Measured: about 2 ms per line with the document frozen, 10 ms without (the window visible).
        // Generous ceilings: a debug build on a loaded machine still passes; losing the Freeze does not.
        fill.Should().BeLessThan(TimeSpan.FromSeconds(45), $"10,000 lines took {fill.TotalMilliseconds:F0} ms");
        steady.Should().BeLessThan(TimeSpan.FromSeconds(10), $"2,000 more lines with trimming took {steady.TotalMilliseconds:F0} ms");
    });
}
