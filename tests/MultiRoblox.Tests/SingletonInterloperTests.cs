using MultiRoblox.Core.Services;
using Xunit;

namespace MultiRoblox.Tests;

public class SingletonInterloperTests
{
    [Fact]
    public void EnumerateHandles_returns_a_populated_table()
    {
        // Exercises the NtQuerySystemInformation call + struct marshalling. The current process alone
        // holds dozens of handles, so the system-wide table is never empty on a healthy machine.
        var handles = SingletonInterloper.EnumerateHandles();
        Assert.NotEmpty(handles);
        Assert.Contains(handles, h => (int)h.UniqueProcessId == Environment.ProcessId);
    }

    [Fact]
    public void FreeExistingClients_is_a_safe_noop_when_no_roblox_is_running()
    {
        // No RobloxPlayerBeta.exe on the CI box -> must return 0 and never throw.
        if (System.Diagnostics.Process.GetProcessesByName("RobloxPlayerBeta").Length != 0)
            return; // running locally with Roblox open; skip rather than touch a live client

        Assert.Equal(0, SingletonInterloper.FreeExistingClients());
    }
}
