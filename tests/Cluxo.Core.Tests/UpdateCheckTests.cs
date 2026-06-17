using Cluxo.Core;
using Xunit;

namespace Cluxo.Core.Tests;

public class UpdateCheckTests
{
    [Theory]
    [InlineData("1.0.0", "1.0.1", UpdateStatus.UpdateAvailable)]
    [InlineData("1.0.1", "1.0.1", UpdateStatus.UpToDate)]
    [InlineData("1.0.2", "1.0.1", UpdateStatus.LocalAhead)]
    [InlineData("v1.0.0", "v1.0.1", UpdateStatus.UpdateAvailable)] // v 접두사 허용
    [InlineData("1.2.0", "1.10.0", UpdateStatus.UpdateAvailable)]  // 숫자 비교(10 > 2), 사전식 아님
    [InlineData("1.0", "1.0.0", UpdateStatus.UpToDate)]            // 길이 다름 → 빈 구획 0
    [InlineData("2.0.0", "1.9.9", UpdateStatus.LocalAhead)]
    public void Compare_NumericVersions(string current, string latest, UpdateStatus expected)
        => Assert.Equal(expected, UpdateCheck.Compare(current, latest));

    [Fact]
    public void Compare_EmptyLatest_IsError()
        => Assert.Equal(UpdateStatus.Error, UpdateCheck.Compare("1.0.0", ""));
}
