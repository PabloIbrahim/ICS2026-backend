using Xunit;

namespace Dsw2025Tpi.UnitTests;

public class PipelineFailureDemoTests
{
    [Fact]
    public void Pipeline_ShouldFail_WhenTestIsIntentionallyBroken()
    {
        Assert.True(false, "FALLO INTENCIONAL: demostración del bloqueo del pipeline TP4.");
    }
}
