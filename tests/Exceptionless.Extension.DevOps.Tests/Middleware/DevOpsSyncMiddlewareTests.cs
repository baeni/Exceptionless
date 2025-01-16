using Xunit;

namespace Exceptionless.Extension.DevOps.Tests.Middleware;

public class DevOpsSyncMiddlewareTests
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

    [Fact]
    public void Test5()
    {
        Thread.Sleep(_random.Next(5, 100));
    }

    [Fact]
    public void Test6()
    {
        Thread.Sleep(_random.Next(5, 100));
    }
}
