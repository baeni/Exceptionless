using Xunit;

namespace Exceptionless.Extension.DevOps.Tests.Modules;

public class DevOpsSyncModuleTests
{
    private readonly Random _random = new Random();

    [Fact]
    public void Test1()
    {
        Thread.Sleep(_random.Next(5, 100));
    }

    [Fact]
    public void Test2()
    {
        Thread.Sleep(_random.Next(5, 100));
    }
}
