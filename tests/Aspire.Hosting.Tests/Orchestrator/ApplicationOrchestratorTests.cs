// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Aspire.Dashboard.Model;
using Aspire.Hosting.Dcp;
using Aspire.Hosting.Eventing;
using Aspire.Hosting.Orchestrator;
using Aspire.Hosting.Tests.Utils;
using Aspire.Hosting.Utils;
using Microsoft.AspNetCore.InternalTesting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aspire.Hosting.Tests.Orchestrator;

public class ApplicationOrchestratorTests
{
    [Fact]
    public async Task ParentPropertySetOnChildResource()
    {
        var builder = DistributedApplication.CreateBuilder();

        var parentResource = builder.AddContainer("database", "image");
        var childResource = builder.AddResource(new CustomChildResource("child", parentResource.Resource));

        using var app = builder.Build();
        var distributedAppModel = app.Services.GetRequiredService<DistributedApplicationModel>();

        var events = new DcpExecutorEvents();
        var resourceNotificationService = ResourceNotificationServiceTestHelpers.Create();

        var appOrchestrator = CreateOrchestrator(distributedAppModel, notificationService: resourceNotificationService, dcpEvents: events);
        await appOrchestrator.RunApplicationAsync();

        string? parentResourceId = null;
        string? childParentResourceId = null;
        var watchResourceTask = Task.Run(async () =>
        {
            await foreach (var item in resourceNotificationService.WatchAsync())
            {
                if (item.Resource == parentResource.Resource)
                {
                    parentResourceId = item.ResourceId;
                }
                else if (item.Resource == childResource.Resource)
                {
                    childParentResourceId = item.Snapshot.Properties.SingleOrDefault(p => p.Name == KnownProperties.Resource.ParentName)?.Value?.ToString();
                }

                if (parentResourceId != null && childParentResourceId != null)
                {
                    return;
                }
            }
        });

        await events.PublishAsync(new OnResourcesPreparedContext(CancellationToken.None));

        await watchResourceTask.DefaultTimeout();

        Assert.Equal(parentResourceId, childParentResourceId);
    }

    [Fact]
    public async Task ParentAnnotationOnChildResource()
    {
        var builder = DistributedApplication.CreateBuilder();

        var parentResource = builder.AddResource(new CustomResource("parent"));
        var childResource = builder.AddResource(new CustomResource("child"))
            .WithParentRelationship(parentResource);

        using var app = builder.Build();
        var distributedAppModel = app.Services.GetRequiredService<DistributedApplicationModel>();

        var events = new DcpExecutorEvents();
        var resourceNotificationService = ResourceNotificationServiceTestHelpers.Create();

        var appOrchestrator = CreateOrchestrator(distributedAppModel, notificationService: resourceNotificationService, dcpEvents: events);
        await appOrchestrator.RunApplicationAsync();

        string? parentResourceId = null;
        string? childParentResourceId = null;
        var watchResourceTask = Task.Run(async () =>
        {
            await foreach (var item in resourceNotificationService.WatchAsync())
            {
                if (item.Resource == parentResource.Resource)
                {
                    parentResourceId = item.ResourceId;
                }
                else if (item.Resource == childResource.Resource)
                {
                    childParentResourceId = item.Snapshot.Properties.SingleOrDefault(p => p.Name == KnownProperties.Resource.ParentName)?.Value?.ToString();
                }

                if (parentResourceId != null && childParentResourceId != null)
                {
                    return;
                }
            }
        });

        await events.PublishAsync(new OnResourcesPreparedContext(CancellationToken.None));

        await watchResourceTask.DefaultTimeout();

        Assert.Equal(parentResourceId, childParentResourceId);
    }

    [Fact]
    public async Task InitializeResourceEventPublished()
    {
        var builder = DistributedApplication.CreateBuilder();

        var resource = builder.AddResource(new CustomResource("resource"));

        using var app = builder.Build();
        var distributedAppModel = app.Services.GetRequiredService<DistributedApplicationModel>();

        var events = new DcpExecutorEvents();
        var resourceNotificationService = ResourceNotificationServiceTestHelpers.Create();
        var applicationEventing = builder.Eventing;

        var initResourceTcs = new TaskCompletionSource();
        InitializeResourceEvent? initEvent = null;
        resource.OnInitializeResource((_, @event, _) =>
        {
            initEvent = @event;
            initResourceTcs.SetResult();
            return Task.CompletedTask;
        });

        applicationEventing.Subscribe<InitializeResourceEvent>(resource.Resource, (@event, ct) =>
        {
            initEvent = @event;
            initResourceTcs.SetResult();
            return Task.CompletedTask;
        });

        var appOrchestrator = CreateOrchestrator(distributedAppModel, notificationService: resourceNotificationService, dcpEvents: events, applicationEventing: applicationEventing);
        await appOrchestrator.RunApplicationAsync();

        await events.PublishAsync(new OnResourcesPreparedContext(CancellationToken.None));

        await initResourceTcs.Task; //.DefaultTimeout();

        Assert.True(initResourceTcs.Task.IsCompletedSuccessfully);
        Assert.NotNull(initEvent);
        Assert.NotNull(initEvent.Logger);
        Assert.NotNull(initEvent.Services);
        Assert.Equal(resource.Resource, initEvent.Resource);
        Assert.Equal(resourceNotificationService, initEvent.Notifications);
        Assert.Equal(applicationEventing, initEvent.Eventing);
    }

    [Fact]
    public async Task WithParentRelationshipSetsParentPropertyCorrectly()
    {
        var builder = DistributedApplication.CreateBuilder();

        var parent = builder.AddContainer("parent", "image");
        var child = builder.AddContainer("child", "image").WithParentRelationship(parent);
        var child2 = builder.AddContainer("child2", "image").WithParentRelationship(parent);

        var nestedChild = builder.AddContainer("nested-child", "image").WithParentRelationship(child);

        using var app = builder.Build();
        var distributedAppModel = app.Services.GetRequiredService<DistributedApplicationModel>();

        var events = new DcpExecutorEvents();
        var resourceNotificationService = ResourceNotificationServiceTestHelpers.Create();

        var appOrchestrator = CreateOrchestrator(distributedAppModel, notificationService: resourceNotificationService, dcpEvents: events);
        await appOrchestrator.RunApplicationAsync();

        string? parentResourceId = null;
        string? childResourceId = null;
        string? childParentResourceId = null;
        string? child2ParentResourceId = null;
        string? nestedChildParentResourceId = null;
        var watchResourceTask = Task.Run(async () =>
        {
            await foreach (var item in resourceNotificationService.WatchAsync())
            {
                if (item.Resource == parent.Resource)
                {
                    parentResourceId = item.ResourceId;
                }
                else if (item.Resource == child.Resource)
                {
                    childResourceId = item.ResourceId;
                    childParentResourceId = item.Snapshot.Properties.SingleOrDefault(p => p.Name == KnownProperties.Resource.ParentName)?.Value?.ToString();
                }
                else if (item.Resource == nestedChild.Resource)
                {
                    nestedChildParentResourceId = item.Snapshot.Properties.SingleOrDefault(p => p.Name == KnownProperties.Resource.ParentName)?.Value?.ToString();
                }
                else if (item.Resource == child2.Resource)
                {
                    child2ParentResourceId = item.Snapshot.Properties.SingleOrDefault(p => p.Name == KnownProperties.Resource.ParentName)?.Value?.ToString();
                }

                if (parentResourceId != null && childParentResourceId != null && nestedChildParentResourceId != null && child2ParentResourceId != null)
                {
                    return;
                }
            }
        });

        await events.PublishAsync(new OnResourcesPreparedContext(CancellationToken.None));

        await watchResourceTask.DefaultTimeout();

        Assert.Equal(parentResourceId, childParentResourceId);
        Assert.Equal(parentResourceId, child2ParentResourceId);

        // Nested child should be parented on the direct parent
        Assert.Equal(childResourceId, nestedChildParentResourceId);
    }

    [Fact]
    public async Task LastWithParentRelationshipWins()
    {
        var builder = DistributedApplication.CreateBuilder();

        var firstParent = builder.AddContainer("firstParent", "image");
        var secondParent = builder.AddContainer("secondParent", "image");

        var child = builder.AddContainer("child", "image");

        child.WithParentRelationship(firstParent);
        child.WithParentRelationship(secondParent);

        using var app = builder.Build();
        var distributedAppModel = app.Services.GetRequiredService<DistributedApplicationModel>();

        var events = new DcpExecutorEvents();
        var resourceNotificationService = ResourceNotificationServiceTestHelpers.Create();

        var appOrchestrator = CreateOrchestrator(distributedAppModel, notificationService: resourceNotificationService, dcpEvents: events);
        await appOrchestrator.RunApplicationAsync();

        string? firstParentResourceId = null;
        string? secondParentResourceId = null;
        string? childParentResourceId = null;
        var watchResourceTask = Task.Run(async () =>
        {
            await foreach (var item in resourceNotificationService.WatchAsync())
            {
                if (item.Resource == firstParent.Resource)
                {
                    firstParentResourceId = item.ResourceId;
                }
                else if (item.Resource == secondParent.Resource)
                {
                    secondParentResourceId = item.ResourceId;
                }
                else if (item.Resource == child.Resource)
                {
                    childParentResourceId = item.Snapshot.Properties.SingleOrDefault(p => p.Name == KnownProperties.Resource.ParentName)?.Value?.ToString();
                }

                if (firstParentResourceId != null && secondParentResourceId != null && childParentResourceId != null)
                {
                    return;
                }
            }
        });

        await events.PublishAsync(new OnResourcesPreparedContext(CancellationToken.None));

        await watchResourceTask.DefaultTimeout();

        // child should be parented to the last parent set
        Assert.Equal(secondParentResourceId, childParentResourceId);
    }

    [Fact]
    public async Task WithParentRelationshipWorksWithProjects()
    {
        var builder = DistributedApplication.CreateBuilder();

        var projectA = builder.AddProject<ProjectA>("projecta");
        var projectB = builder.AddProject<ProjectB>("projectb").WithParentRelationship(projectA);

        using var app = builder.Build();
        var distributedAppModel = app.Services.GetRequiredService<DistributedApplicationModel>();

        var events = new DcpExecutorEvents();
        var resourceNotificationService = ResourceNotificationServiceTestHelpers.Create();

        var appOrchestrator = CreateOrchestrator(distributedAppModel, notificationService: resourceNotificationService, dcpEvents: events);
        await appOrchestrator.RunApplicationAsync();

        string? projectAResourceId = null;
        string? projectBParentResourceId = null;
        var watchResourceTask = Task.Run(async () =>
        {
            await foreach (var item in resourceNotificationService.WatchAsync())
            {
                if (item.Resource == projectA.Resource)
                {
                    projectAResourceId = item.ResourceId;
                }
                else if (item.Resource == projectB.Resource)
                {
                    projectBParentResourceId = item.Snapshot.Properties.SingleOrDefault(p => p.Name == KnownProperties.Resource.ParentName)?.Value?.ToString();
                }

                if (projectAResourceId != null && projectBParentResourceId != null)
                {
                    return;
                }
            }
        });

        await events.PublishAsync(new OnResourcesPreparedContext(CancellationToken.None));

        await watchResourceTask.DefaultTimeout();

        Assert.Equal(projectAResourceId, projectBParentResourceId);
    }

    [Fact]
    public void DetectsCircularDependency()
    {
        using var builder = TestDistributedApplicationBuilder.Create();

        var container1 = builder.AddContainer("container1", "image");
        var container2 = builder.AddContainer("container2", "image2");
        var container3 = builder.AddContainer("container3", "image3");

        container1.WithParentRelationship(container2);
        container2.WithParentRelationship(container3);
        container3.WithParentRelationship(container1);

        using var app = builder.Build();

        var e = Assert.Throws<InvalidOperationException>(() => app.Services.GetService<ApplicationOrchestrator>());
        Assert.Contains("Circular dependency detected", e.Message);
    }

    [Fact]
    public async Task GrandChildResourceWithConnectionString()
    {
        var builder = DistributedApplication.CreateBuilder();

        var parentResource = builder.AddResource(new ParentResourceWithConnectionString("parent"));
        var childResource = builder.AddResource(
            new ChildResourceWithConnectionString("child", new Dictionary<string, string> { {"Namespace", "ns"} }, parentResource.Resource)
        );
        var grandChildResource = builder.AddResource(
            new ChildResourceWithConnectionString("grand-child", new Dictionary<string, string> { {"Database", "db"} }, childResource.Resource)
        );

        await using var app = builder.Build();
        var distributedAppModel = app.Services.GetRequiredService<DistributedApplicationModel>();

        var events = new DcpExecutorEvents();
        var resourceNotificationService = ResourceNotificationServiceTestHelpers.Create();
        var applicationEventing = new DistributedApplicationEventing();

        var appOrchestrator = CreateOrchestrator(distributedAppModel, notificationService: resourceNotificationService, dcpEvents: events, applicationEventing: applicationEventing);
        await appOrchestrator.RunApplicationAsync();

        bool parentConnectionStringAvailable = false;
        bool childConnectionStringAvailable = false;
        bool grandChildConnectionStringAvailable = false;

        applicationEventing.Subscribe<ConnectionStringAvailableEvent>(parentResource.Resource, (_, _) =>
        {
            parentConnectionStringAvailable = true;
            return Task.CompletedTask;
        });
        applicationEventing.Subscribe<ConnectionStringAvailableEvent>(childResource.Resource, (_, _) =>
        {
            childConnectionStringAvailable = true;
            return Task.CompletedTask;
        });
        applicationEventing.Subscribe<ConnectionStringAvailableEvent>(grandChildResource.Resource, (_, _) =>
        {
            grandChildConnectionStringAvailable = true;
            return Task.CompletedTask;
        });

        await events.PublishAsync(new OnResourceStartingContext(CancellationToken.None, KnownResourceTypes.Container, parentResource.Resource, parentResource.Resource.Name));

        Assert.True(parentConnectionStringAvailable);
        Assert.True(childConnectionStringAvailable);
        Assert.True(grandChildConnectionStringAvailable);
    }

    [Fact]
    public async Task ConnectionStringAvailableEventPublishesUpdateWithConnectionStringValue()
    {
        var builder = DistributedApplication.CreateBuilder();

        var resource = builder.AddResource(new TestResourceWithConnectionString("test-resource", "Server=localhost:5432;Database=testdb"));

        using var app = builder.Build();
        var distributedAppModel = app.Services.GetRequiredService<DistributedApplicationModel>();

        var events = new DcpExecutorEvents();
        var resourceNotificationService = ResourceNotificationServiceTestHelpers.Create();
        var applicationEventing = new DistributedApplicationEventing();

        var appOrchestrator = CreateOrchestrator(distributedAppModel, notificationService: resourceNotificationService, dcpEvents: events, applicationEventing: applicationEventing);
        await appOrchestrator.RunApplicationAsync();

        string? connectionStringProperty = null;
        bool? isSensitive = null;
        var watchResourceTask = Task.Run(async () =>
        {
            await foreach (var item in resourceNotificationService.WatchAsync())
            {
                if (item.Resource == resource.Resource)
                {
                    var connectionStringProp = item.Snapshot.Properties.SingleOrDefault(p => p.Name == KnownProperties.Resource.ConnectionString);
                    if (connectionStringProp is not null)
                    {
                        connectionStringProperty = connectionStringProp.Value?.ToString();
                        isSensitive = connectionStringProp.IsSensitive;
                        return;
                    }
                }
            }
        });

        // Publish the ConnectionStringAvailableEvent to trigger the update
        await applicationEventing.PublishAsync(new ConnectionStringAvailableEvent(resource.Resource, app.Services), CancellationToken.None);

        await watchResourceTask.DefaultTimeout();

        Assert.Equal("Server=localhost:5432;Database=testdb", connectionStringProperty);
        Assert.True(isSensitive);
    }

    private static ApplicationOrchestrator CreateOrchestrator(
        DistributedApplicationModel distributedAppModel,
        ResourceNotificationService notificationService,
        DcpExecutorEvents? dcpEvents = null,
        IDistributedApplicationEventing? applicationEventing = null,
        ResourceLoggerService? resourceLoggerService = null)
    {
        var serviceProvider = new ServiceCollection().BuildServiceProvider();
        resourceLoggerService ??= new ResourceLoggerService();

        return new ApplicationOrchestrator(
            distributedAppModel,
            new TestDcpExecutor(),
            dcpEvents ?? new DcpExecutorEvents(),
            [],
            notificationService,
            resourceLoggerService,
            applicationEventing ?? new DistributedApplicationEventing(),
            serviceProvider,
            new DistributedApplicationExecutionContext(
                new DistributedApplicationExecutionContextOptions(DistributedApplicationOperation.Run) { ServiceProvider = serviceProvider }),
            new ParameterProcessor(
                notificationService,
                resourceLoggerService,
                CreateInteractionService(),
                NullLogger<ParameterProcessor>.Instance)
            );
    }

    private static InteractionService CreateInteractionService(DistributedApplicationOptions? options = null)
    {
        return new InteractionService(
            NullLogger<InteractionService>.Instance,
            options ?? new DistributedApplicationOptions(),
            new ServiceCollection().BuildServiceProvider());
    }

    private sealed class CustomResource(string name) : Resource(name);

    private sealed class CustomChildResource(string name, IResource parent) : Resource(name), IResourceWithParent
    {
        public IResource Parent => parent;
    }

    private sealed class ProjectA : IProjectMetadata
    {
        public string ProjectPath => "projectA";

        public LaunchSettings LaunchSettings { get; } = new();
    }

    private sealed class ProjectB : IProjectMetadata
    {
        public string ProjectPath => "projectB";
        public LaunchSettings LaunchSettings { get; } = new();
    }

    private abstract class ResourceWithConnectionString(string name)
        : Resource(name), IResourceWithConnectionString
    {
        protected abstract ReferenceExpression ConnectionString { get; }

        public ReferenceExpression ConnectionStringExpression
        {
            get
            {
                if (this.TryGetLastAnnotation<ConnectionStringRedirectAnnotation>(out var connectionStringAnnotation))
                {
                    return connectionStringAnnotation.Resource.ConnectionStringExpression;
                }

                return ConnectionString;
            }
        }
    }

    private sealed class ParentResourceWithConnectionString(string name) : ResourceWithConnectionString(name)
    {
        protected override ReferenceExpression ConnectionString =>
            ReferenceExpression.Create($"Server=localhost:8000");
    }

    private sealed class ChildResourceWithConnectionString(
        string name,
        Dictionary<string, string> kvConnectionString,
        IResourceWithConnectionString parent
    )
        : ResourceWithConnectionString(name), IResourceWithParent
    {
        private string SubConnectionString =>
            string.Join(';', kvConnectionString.Select(kv => $"{kv.Key}={kv.Value}"));

        protected override ReferenceExpression ConnectionString =>
            ReferenceExpression.Create($"{parent};{SubConnectionString}");

        public IResource Parent { get; } = parent;
    }

    private sealed class TestResourceWithConnectionString(string name, string connectionString)
        : Resource(name), IResourceWithConnectionString
    {
        public ReferenceExpression ConnectionStringExpression => ReferenceExpression.Create($"{connectionString}");

        public ValueTask<string?> GetConnectionStringAsync(CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult<string?>(connectionString);
        }
    }
}
