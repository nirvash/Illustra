using System;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Prism.Events;
using Illustra.MCPHost;
using Microsoft.Extensions.Logging; // Added for ILogger
using Illustra.Shared.Models.Tools; // Added for OpenFolderTool
using Illustra.Shared.Models; // Updated namespace
using Microsoft.Extensions.DependencyInjection; // Added for GetRequiredService
using Illustra.Services; // Added for IDispatcherService

namespace Illustra.Tests.MCPHost
{
    public class APIServiceTests
    {
        private Mock<IEventAggregator> _mockEventAggregator;
        private Mock<ILogger<APIService>> _mockLogger;
        private Mock<IServiceProvider> _mockServiceProvider;
        // Removed: private Mock<IDispatcherService> _mockDispatcherService; // No longer needed here
        private APIService _apiService;

        [SetUp]
        public void Setup()
        {
            _mockEventAggregator = new Mock<IEventAggregator>();
            _mockLogger = new Mock<ILogger<APIService>>();
            _mockServiceProvider = new Mock<IServiceProvider>();
            // Removed: _mockDispatcherService = new Mock<IDispatcherService>();

            // Removed setup for McpExecuteToolEvent
            // Removed setup for McpGetInfoEvent

            // Removed unnecessary local mockDispatcherService and its setup

            _apiService = new APIService(_mockEventAggregator.Object, _mockLogger.Object, _mockServiceProvider.Object);

            // Setup IServiceProvider mock to resolve OpenFolderTool (which no longer requires IDispatcherService)
            var openFolderToolInstance = new OpenFolderTool(); // Create instance without dispatcher
            _mockServiceProvider.Setup(sp => sp.GetService(typeof(OpenFolderTool)))
                                .Returns(openFolderToolInstance);
            // Setup GetRequiredService as well
             _mockServiceProvider.Setup(sp => sp.GetRequiredService(typeof(OpenFolderTool)))
                                .Returns(openFolderToolInstance);

            // Removed setup for mock dispatcher service
        }

        // TODO: Add tests for the new InvokeToolAsync logic, including:
        // - Finding the correct tool executor
        // - Resolving the executor from IServiceProvider
        // - Calling ExecuteAsync on the executor
        // - Handling ArgumentException from executor
        // - Handling NotSupportedException for unknown tools
        // - Handling general exceptions during resolution or execution
        // Removed ExecuteToolAsync tests (3 tests)

        // Removed GetInfoAsync tests (3 tests)
    }
}
