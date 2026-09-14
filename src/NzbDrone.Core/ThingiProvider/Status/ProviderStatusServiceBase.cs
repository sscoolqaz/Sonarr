using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.ThingiProvider.Events;

namespace NzbDrone.Core.ThingiProvider.Status
{
    public interface IProviderStatusServiceBase<TModel>
        where TModel : ProviderStatusBase, new()
    {
        Task<List<TModel>> GetBlockedProviders();
        Task RecordSuccess(int providerId);
        Task RecordFailure(int providerId, TimeSpan minimumBackOff = default(TimeSpan));
        Task RecordConnectionFailure(int providerId);
    }

    public abstract class ProviderStatusServiceBase<TProvider, TModel> : IProviderStatusServiceBase<TModel>, IHandleAsync<ProviderDeletedEvent<TProvider>>
        where TProvider : IProvider
        where TModel : ProviderStatusBase, new()
    {
        // NOTE: was a plain `lock (object)`; converted to SemaphoreSlim because the critical
        // section now needs to `await` repository calls, and C# forbids awaiting inside `lock`.
        protected readonly SemaphoreSlim _syncRoot = new SemaphoreSlim(1, 1);

        protected readonly IProviderStatusRepository<TModel> _providerStatusRepository;
        protected readonly IEventAggregator _eventAggregator;
        protected readonly IRuntimeInfo _runtimeInfo;
        protected readonly Logger _logger;

        protected int MaximumEscalationLevel { get; set; } = EscalationBackOff.Periods.Length - 1;
        protected TimeSpan MinimumTimeSinceInitialFailure { get; set; } = TimeSpan.Zero;
        protected TimeSpan MinimumTimeSinceStartup { get; set; } = TimeSpan.FromMinutes(15);

        public ProviderStatusServiceBase(IProviderStatusRepository<TModel> providerStatusRepository, IEventAggregator eventAggregator, IRuntimeInfo runtimeInfo, Logger logger)
        {
            _providerStatusRepository = providerStatusRepository;
            _eventAggregator = eventAggregator;
            _runtimeInfo = runtimeInfo;
            _logger = logger;
        }

        public virtual async Task<List<TModel>> GetBlockedProviders()
        {
            return (await _providerStatusRepository.All()).Where(v => v.IsDisabled()).ToList();
        }

        protected virtual async Task<TModel> GetProviderStatus(int providerId)
        {
            return await _providerStatusRepository.FindByProviderId(providerId) ?? new TModel { ProviderId = providerId };
        }

        protected virtual TimeSpan CalculateBackOffPeriod(TModel status)
        {
            var level = Math.Min(MaximumEscalationLevel, status.EscalationLevel);

            return TimeSpan.FromSeconds(EscalationBackOff.Periods[level]);
        }

        public virtual async Task RecordSuccess(int providerId)
        {
            if (providerId <= 0)
            {
                return;
            }

            await _syncRoot.WaitAsync();

            try
            {
                var status = await GetProviderStatus(providerId);

                if (status.EscalationLevel == 0)
                {
                    return;
                }

                status.EscalationLevel--;
                status.DisabledTill = null;

                await _providerStatusRepository.Upsert(status);

                _eventAggregator.PublishEvent(new ProviderStatusChangedEvent<TProvider>(providerId, status));
            }
            finally
            {
                _syncRoot.Release();
            }
        }

        protected virtual async Task RecordFailure(int providerId, TimeSpan minimumBackOff, bool escalate)
        {
            if (providerId <= 0)
            {
                return;
            }

            await _syncRoot.WaitAsync();

            try
            {
                var status = await GetProviderStatus(providerId);

                var now = DateTime.UtcNow;
                status.MostRecentFailure = now;

                if (status.EscalationLevel == 0)
                {
                    status.InitialFailure = now;
                    status.EscalationLevel = 1;
                    escalate = false;
                }

                var inStartupGracePeriod = (_runtimeInfo.StartTime + MinimumTimeSinceStartup) > now;
                var inGracePeriod = (status.InitialFailure.Value + MinimumTimeSinceInitialFailure) > now;

                if (escalate && !inGracePeriod && !inStartupGracePeriod)
                {
                    status.EscalationLevel = Math.Min(MaximumEscalationLevel, status.EscalationLevel + 1);
                }

                if (minimumBackOff != TimeSpan.Zero)
                {
                    while (status.EscalationLevel < MaximumEscalationLevel && CalculateBackOffPeriod(status) < minimumBackOff)
                    {
                        status.EscalationLevel++;
                    }
                }

                if (!inGracePeriod || minimumBackOff != TimeSpan.Zero)
                {
                    status.DisabledTill = now + CalculateBackOffPeriod(status);
                }

                if (inStartupGracePeriod && minimumBackOff == TimeSpan.Zero && status.DisabledTill.HasValue)
                {
                    var maximumDisabledTill = now + TimeSpan.FromSeconds(EscalationBackOff.Periods[2]);
                    if (maximumDisabledTill < status.DisabledTill)
                    {
                        status.DisabledTill = maximumDisabledTill;
                    }
                }

                await _providerStatusRepository.Upsert(status);

                _eventAggregator.PublishEvent(new ProviderStatusChangedEvent<TProvider>(providerId, status));
            }
            finally
            {
                _syncRoot.Release();
            }
        }

        public virtual async Task RecordFailure(int providerId, TimeSpan minimumBackOff = default(TimeSpan))
        {
            await RecordFailure(providerId, minimumBackOff, true);
        }

        public virtual async Task RecordConnectionFailure(int providerId)
        {
            await RecordFailure(providerId, default(TimeSpan), false);
        }

        // NOTE: IHandleAsync<TEvent> is a shared eventing interface (18 implementers app-wide);
        // its `void HandleAsync(TEvent message)` signature is out of scope to convert. The event
        // aggregator already dispatches IHandleAsync handlers via Task.Factory.StartNew on a
        // thread-pool thread (see EventAggregator.cs), not an ASP.NET Core request thread, so
        // blocking here via GetAwaiter().GetResult() does not risk a sync-context deadlock.
        public virtual void HandleAsync(ProviderDeletedEvent<TProvider> message)
        {
            _providerStatusRepository.DeleteByProviderId(message.ProviderId).GetAwaiter().GetResult();
        }
    }
}
