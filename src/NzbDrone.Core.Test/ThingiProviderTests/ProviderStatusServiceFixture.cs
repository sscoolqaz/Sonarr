using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NLog;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.ThingiProvider;
using NzbDrone.Core.ThingiProvider.Events;
using NzbDrone.Core.ThingiProvider.Status;

namespace NzbDrone.Core.Test.ThingiProviderTests
{
    public class MockProviderStatus : ProviderStatusBase
    {
    }

    public interface IMockProvider : IProvider
    {
    }

    public interface IMockProviderStatusRepository : IProviderStatusRepository<MockProviderStatus>
    {
    }

    public class MockProviderStatusService : ProviderStatusServiceBase<IMockProvider, MockProviderStatus>
    {
        public MockProviderStatusService(IMockProviderStatusRepository providerStatusRepository, IEventAggregator eventAggregator, IRuntimeInfo runtimeInfo, Logger logger)
            : base(providerStatusRepository, eventAggregator, runtimeInfo, logger)
        {
        }
    }

    public class ProviderStatusServiceFixture : CoreTest<MockProviderStatusService>
    {
        private readonly TimeSpan _disabledTillPrecision = TimeSpan.FromMilliseconds(500);
        private DateTime _epoch;

        [SetUp]
        public void SetUp()
        {
            _epoch = DateTime.UtcNow;

            Mocker.GetMock<IRuntimeInfo>()
                .SetupGet(v => v.StartTime)
                .Returns(_epoch - TimeSpan.FromHours(1));
        }

        private void GivenRecentStartup()
        {
            Mocker.GetMock<IRuntimeInfo>()
                .SetupGet(v => v.StartTime)
                .Returns(_epoch - TimeSpan.FromMinutes(12));
        }

        private MockProviderStatus WithStatus(MockProviderStatus status)
        {
            Mocker.GetMock<IMockProviderStatusRepository>()
                .Setup(v => v.FindByProviderId(1))
                .ReturnsAsync(status);

            Mocker.GetMock<IMockProviderStatusRepository>()
                .Setup(v => v.All())
                .ReturnsAsync(new[] { status });

            return status;
        }

        private void VerifyUpdate()
        {
            Mocker.GetMock<IMockProviderStatusRepository>()
                .Verify(v => v.Upsert(It.IsAny<MockProviderStatus>()), Times.Once());
        }

        private void VerifyNoUpdate()
        {
            Mocker.GetMock<IMockProviderStatusRepository>()
                .Verify(v => v.Upsert(It.IsAny<MockProviderStatus>()), Times.Never());
        }

        [Test]
        public async Task should_start_backoff_on_first_failure()
        {
            WithStatus(new MockProviderStatus());

            await Subject.RecordFailure(1);

            VerifyUpdate();

            var status = (await Subject.GetBlockedProviders()).FirstOrDefault();
            status.Should().NotBeNull();
            status.DisabledTill.Should().HaveValue();
            status.DisabledTill.Value.Should().BeCloseTo(_epoch + TimeSpan.FromMinutes(1), _disabledTillPrecision);
        }

        [Test]
        public async Task should_cancel_backoff_on_success()
        {
            WithStatus(new MockProviderStatus { EscalationLevel = 2 });

            await Subject.RecordSuccess(1);

            VerifyUpdate();

            var status = (await Subject.GetBlockedProviders()).FirstOrDefault();
            status.Should().BeNull();
        }

        [Test]
        public async Task should_not_store_update_if_already_okay()
        {
            WithStatus(new MockProviderStatus { EscalationLevel = 0 });

            await Subject.RecordSuccess(1);

            VerifyNoUpdate();
        }

        [Test]
        public async Task should_preserve_escalation_on_intermittent_success()
        {
            WithStatus(new MockProviderStatus
            {
                InitialFailure = _epoch - TimeSpan.FromSeconds(20),
                MostRecentFailure = _epoch - TimeSpan.FromSeconds(4),
                EscalationLevel = 3
            });

            await Subject.RecordSuccess(1);
            await Subject.RecordSuccess(1);
            await Subject.RecordFailure(1);

            var status = (await Subject.GetBlockedProviders()).FirstOrDefault();
            status.Should().NotBeNull();
            status.DisabledTill.Should().HaveValue();
            status.DisabledTill.Value.Should().BeCloseTo(_epoch + TimeSpan.FromMinutes(5), _disabledTillPrecision);
        }

        [Test]
        public async Task should_not_escalate_further_till_after_5_minutes_since_startup()
        {
            GivenRecentStartup();

            var origStatus = WithStatus(new MockProviderStatus
            {
                InitialFailure = _epoch - TimeSpan.FromMinutes(6),
                MostRecentFailure = _epoch - TimeSpan.FromSeconds(120),
                EscalationLevel = 3
            });

            await Subject.RecordFailure(1);
            await Subject.RecordFailure(1);
            await Subject.RecordFailure(1);
            await Subject.RecordFailure(1);
            await Subject.RecordFailure(1);
            await Subject.RecordFailure(1);
            await Subject.RecordFailure(1);

            var status = (await Subject.GetBlockedProviders()).FirstOrDefault();
            status.Should().NotBeNull();

            origStatus.EscalationLevel.Should().Be(3);
            status.DisabledTill.Should().BeCloseTo(_epoch + TimeSpan.FromMinutes(5), _disabledTillPrecision);
        }

        // The tests below exercise ProviderStatusServiceBase's own async plumbing rather than
        // its escalation math - this base class is shared by NotificationStatusService,
        // IndexerStatusService, ImportListStatusService and DownloadClientStatusService, so
        // covering it here covers all four real services' shared behavior at once.

        [Test]
        public void record_failure_should_propagate_repository_exception_and_not_publish_a_status_changed_event()
        {
            WithStatus(new MockProviderStatus());

            Mocker.GetMock<IMockProviderStatusRepository>()
                  .Setup(v => v.Upsert(It.IsAny<MockProviderStatus>()))
                  .ThrowsAsync(new InvalidOperationException("repository unavailable"));

            Assert.ThrowsAsync<InvalidOperationException>(async () => await Subject.RecordFailure(1));

            // ProviderStatusChangedEvent is published immediately after the awaited Upsert call
            // with no guard around it - if that exception were ever swallowed, the event would
            // still fire even though nothing was actually persisted.
            Mocker.GetMock<IEventAggregator>()
                  .Verify(e => e.PublishEvent(It.IsAny<ProviderStatusChangedEvent<IMockProvider>>()), Times.Never);
        }

        [Test]
        public async Task record_failure_should_release_its_lock_after_a_repository_exception_so_the_next_call_is_not_blocked_forever()
        {
            // RecordFailure/RecordSuccess serialize their read-modify-write against
            // _providerStatusRepository through a SemaphoreSlim (converted from a plain `lock`
            // specifically because the critical section now awaits) - the WaitAsync/Release is a
            // try/finally around the repository call, so an exception from that call must still
            // release the semaphore. If it didn't, every subsequent RecordSuccess/RecordFailure
            // call through this same service instance would hang forever waiting on the lock
            // instead of surfacing its own result.
            WithStatus(new MockProviderStatus());

            Mocker.GetMock<IMockProviderStatusRepository>()
                  .Setup(v => v.Upsert(It.IsAny<MockProviderStatus>()))
                  .ThrowsAsync(new InvalidOperationException("repository unavailable"));

            Assert.ThrowsAsync<InvalidOperationException>(async () => await Subject.RecordFailure(1));

            Mocker.GetMock<IMockProviderStatusRepository>()
                  .Setup(v => v.Upsert(It.IsAny<MockProviderStatus>()))
                  .ReturnsAsync(new MockProviderStatus());

            var secondCall = Task.Run(async () => await Subject.RecordFailure(1));
            var completed = await Task.WhenAny(secondCall, Task.Delay(TimeSpan.FromSeconds(5)));

            completed.Should().Be(secondCall, "the lock must have been released by the first call's failure, not left held");
            await secondCall;

            // Upsert was called once by the failing attempt and once more by the successful one.
            Mocker.GetMock<IMockProviderStatusRepository>()
                  .Verify(v => v.Upsert(It.IsAny<MockProviderStatus>()), Times.Exactly(2));
        }
    }
}
