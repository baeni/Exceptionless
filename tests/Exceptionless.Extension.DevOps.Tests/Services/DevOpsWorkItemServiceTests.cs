using Exceptionless.Core.Models;
using Exceptionless.Core.Repositories;
using Exceptionless.Extension.DevOps.Clients;
using Exceptionless.Extension.DevOps.Services;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Exceptionless.Extension.DevOps.Tests.Services;

public class DevOpsWorkItemServiceTests
{
    private readonly IStackRepository _mockStackRepository;
    private readonly IDevOpsClient _mockDevOpsClient;
    private readonly TimeProvider _mockTimeProvider;
    private readonly ILoggerFactory _mockLoggerFactory;

    private readonly IDevOpsWorkItemService _devOpsWorkItemService;

    public DevOpsWorkItemServiceTests()
    {
        _mockStackRepository = Substitute.For<IStackRepository>();
        _mockDevOpsClient = Substitute.For<IDevOpsClient>();
        _mockTimeProvider = Substitute.For<TimeProvider>();
        _mockLoggerFactory = Substitute.For<ILoggerFactory>();

        _devOpsWorkItemService = new DevOpsWorkItemService(
            _mockStackRepository,
            _mockDevOpsClient,
            _mockTimeProvider,
            _mockLoggerFactory
        );
    }

    [Fact]
    public async Task UpdateStackStatus_To_Fixed_Should_Update_Stack_Status_And_Set_DateFixed()
    {
        // Arrange
        var stack = new Stack
        {
            Id = "stackId",
            Status = StackStatus.Open,
            DateFixed = null
        };

        var newStatus = StackStatus.Fixed;
        var mockDateTime = new DateTime(2024, 12, 1, 11, 12, 13, DateTimeKind.Utc);
        _mockTimeProvider.GetUtcNow().Returns(new DateTimeOffset(mockDateTime));

        // Act
        await _devOpsWorkItemService.UpdateStackStatus(stack, newStatus);

        // Assert
        Assert.Equal(newStatus, stack.Status);
        Assert.Equal(mockDateTime, stack.DateFixed);

        await _mockStackRepository.Received(1).SaveAsync(stack);
    }

    private readonly Random _random = new Random();

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
}
