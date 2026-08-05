using Microsoft.AspNetCore.SignalR;
using Moq;

namespace VietTien.Tests.TestHelpers
{
    /// <summary>
    /// Mock IHubContext&lt;THub&gt; cho SalesHub / WarehouseHub / NotificationHub — SendAsync trở thành no-op.
    /// </summary>
    public static class MockHubContext
    {
        public static Mock<IHubContext<THub>> Create<THub>() where THub : Hub
        {
            var clientProxy = new Mock<ISingleClientProxy>();
            clientProxy
                .Setup(p => p.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var clients = new Mock<IHubClients>();
            clients.Setup(c => c.All).Returns(clientProxy.Object);
            clients.Setup(c => c.Group(It.IsAny<string>())).Returns(clientProxy.Object);
            clients.Setup(c => c.User(It.IsAny<string>())).Returns(clientProxy.Object);
            clients.Setup(c => c.Client(It.IsAny<string>())).Returns(clientProxy.Object);

            var hubContext = new Mock<IHubContext<THub>>();
            hubContext.Setup(h => h.Clients).Returns(clients.Object);
            hubContext.Setup(h => h.Groups).Returns(new Mock<IGroupManager>().Object);

            return hubContext;
        }
    }
}
