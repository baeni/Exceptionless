using Xunit;

namespace Exceptionless.Extension.DevOps.Tests.Clients;

public class DevOpsClientTests
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

    [Fact]
    public void Test3()
    {
        Thread.Sleep(_random.Next(5, 100));
    }

    [Fact]
    public void Test4()
    {
        Thread.Sleep(_random.Next(5, 100));
    }
}
