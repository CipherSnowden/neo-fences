using NeoFences.Core.Lifecycle;

namespace NeoFences.Core.Tests.Lifecycle;

/// <summary>M38 (spec §2): the results file the test pass writes inside a VM, read into a report on the host.</summary>
public class CheckReportTests
{
    [Fact]
    public void Read_ListsEveryCheckAndCountsTheFailures()
    {
        var report = CheckReport.Read("""
            { "machine": "Windows 10 22H2 (19045)", "version": "0.25.0", "finished": true,
              "checks": [
                { "id": "install", "ok": true, "note": "welcome shown" },
                { "id": "icons-after-kill", "ok": false, "note": "icons stayed hidden" },
                { "id": "update", "ok": true }
              ] }
            """);
        Assert.Equal(("Windows 10 22H2 (19045)", "0.25.0", 3, 1), (report.Machine, report.Version, report.Checks.Count, report.Failed));
        Assert.Equal(["install", "icons-after-kill", "update"], report.Checks.Select(check => check.Id));
        Assert.Equal("icons stayed hidden", report.Checks[1].Note);
        Assert.Equal("", report.Checks[2].Note);
        Assert.Equal("Windows 10 22H2 (19045), 0.25.0: 2 of 3 passed; failed: icons-after-kill (icons stayed hidden)", report.Summary);
    }

    [Fact] // M38 review I6: a pass that hung or timed out has rows, but no "finished"
    public void Read_APassThatStoppedEarly_IsAFailedReport()
    {
        var report = CheckReport.Read("""{ "machine": "m", "version": "v", "checks": [ { "id": "install", "ok": true } ] }""");
        Assert.Equal(1, report.Failed);
        Assert.Equal("m, v: 1 of 1 passed; the test pass did not finish (stopped after install)", report.Summary);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{ not json")]
    [InlineData("""{ "machine": "x" }""")]
    [InlineData("""{ "checks": [ null, { "ok": true } ] }""")]
    public void Read_ADamagedOrUnfinishedFile_IsAFailedReport_NeverAThrow(string text)
    {
        var report = CheckReport.Read(text);
        Assert.True(report.Failed >= 1);
        Assert.Contains("no results", report.Summary);
    }
}
