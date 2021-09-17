﻿using MASA.Contrib.Dispatcher.InMemory.Internal.Configurations;

namespace MASA.Contrib.Dispatcher.InMemory;

internal class DispatcherBase
{
    protected static DispatchRelationNetwork _sharingRelationNetwork;

    protected readonly IServiceCollection _services;

    private readonly ILogger<DispatcherBase> _logger;

    public DispatcherBase(IServiceCollection services, bool forceInit)
    {
        _services = services;
        var serviceProvider = services.BuildServiceProvider();
        if (_sharingRelationNetwork == null || forceInit)
        {
            _sharingRelationNetwork = new DispatchRelationNetwork(serviceProvider.GetRequiredService<ILogger<DispatchRelationNetwork>>());
        }
        _logger = serviceProvider.GetRequiredService<ILogger<DispatcherBase>>();
    }

    public async Task PublishEventAsync<TEvent>(IServiceProvider serviceProvider, TEvent @event)
        where TEvent : IEvent
    {
        var eventType = typeof(TEvent);
        if (!_sharingRelationNetwork.RelationNetwork.TryGetValue(eventType, out List<DispatchRelationOptions>? dispatchRelations) || dispatchRelations == null)
        {
            _logger.LogError($"Dispatcher:No binding handler was found for the current event [{eventType.FullName}]");
            throw new ArgumentNullException($"No binding handler was found for [{eventType.FullName}]");
        }
        await ExecuteEventHandlerAsync(serviceProvider, dispatchRelations, @event);
    }

    private async Task ExecuteEventHandlerAsync<TEvent>(IServiceProvider serviceProvider,
        List<DispatchRelationOptions> dispatchRelations,
        TEvent @event)
        where TEvent : IEvent
    {
        var executionStrategy = serviceProvider.GetRequiredService<IExecutionStrategy>();
        StrategyOptions strategyOptions = new StrategyOptions();
        bool isCancel = false;
        EventHandlerAttribute dispatchHandler;
        foreach (var dispatchRelation in dispatchRelations)
        {
            if (isCancel) return;
            dispatchHandler = dispatchRelation.Handler;

            strategyOptions.SetStrategy(dispatchHandler);

            await executionStrategy.ExecuteAsync(strategyOptions, @event, async (@event) =>
            {
                _logger.LogDebug("----- Publishing event {@Event}: message id: {messageId} -----", @event, @event.Id);
                await dispatchHandler.ExcuteAction(serviceProvider, @event);
            }, async (@event, ex, failureLevels) =>
            {
                if (failureLevels != FailureLevels.Ignore)
                {
                    isCancel = true;
                    if (dispatchRelation.CancelHandlers.Any())
                    {
                        await ExecuteEventCanceledHandlerAsync(serviceProvider, _logger, executionStrategy, dispatchRelation.CancelHandlers, @event);
                    }
                    else
                    {
                        throw ex;
                    }
                }
                else
                {
                    _logger.LogWarning("----- Publishing event {@Event} error rollback is ignored: message id: {messageId} -----", @event, @event.Id);
                }
            });
        }
    }

    private async Task ExecuteEventCanceledHandlerAsync<TEvent>(IServiceProvider serviceProvider,
        ILogger<DispatcherBase> logger,
        IExecutionStrategy executionStrategy,
        IEnumerable<EventHandlerAttribute> cancelHandlers,
        TEvent @event)
        where TEvent : IEvent
    {
        StrategyOptions strategyOptions = new StrategyOptions();
        foreach (var cancelHandler in cancelHandlers)
        {
            strategyOptions.SetStrategy(cancelHandler);
            await executionStrategy.ExecuteAsync(strategyOptions, @event, async (@event) =>
            {
                logger.LogDebug("----- Publishing event {@Event} rollback start: message id: {messageId} -----", @event, @event.Id);
                await cancelHandler.ExcuteAction(serviceProvider, @event);
            }, (TEvent @event, Exception ex, FailureLevels failureLevels) =>
            {
                if (failureLevels != FailureLevels.Ignore)
                {
                    throw ex;
                }
                logger.LogWarning("----- Publishing event {@Event} rollback error ignored: message id: {messageId} -----", @event, @event.Id);
                return Task.CompletedTask;
            });
        }
    }

    protected void AddRelationNetwork(Type parameterType, EventHandlerAttribute handler)
    {
        _sharingRelationNetwork.Add(parameterType, handler);
    }

    protected IEnumerable<Type> GetAddServiceTypeList() => _sharingRelationNetwork.HandlerRelationNetwork
        .Concat(_sharingRelationNetwork.CancelRelationNetwork)
        .SelectMany(relative => relative.Value)
        .Where(dispatchHandler => dispatchHandler.InvokeDelegate != null)
        .Select(dispatchHandler => dispatchHandler.InstanceType).Distinct();

    protected void Build() => _sharingRelationNetwork.Build();

    protected bool IsSagaMode(Type handlerType, MethodInfo method) =>
      (typeof(IEventHandler<>).IsGenericInterfaceAssignableFrom(handlerType) && method.Name.Equals(nameof(IEventHandler<IEvent>.HandleAsync))) ||
      (typeof(ISagaEventHandler<>).IsGenericInterfaceAssignableFrom(handlerType) && method.Name.Equals(nameof(ISagaEventHandler<IEvent>.CancelAsync)));
}