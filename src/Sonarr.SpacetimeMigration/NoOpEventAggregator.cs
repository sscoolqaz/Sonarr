using NzbDrone.Common.Messaging;
using NzbDrone.Core.Messaging.Events;

namespace Sonarr.SpacetimeMigration
{
    // Real repository constructors take IEventAggregator, but nothing this tool does ever
    // triggers a publish - BasicRepository/SpacetimeBasicRepository only call
    // PublishModelEvent from Insert/Update/Delete/SetFields, and this tool only ever reads from
    // the source repositories (.All()) and writes through MigrateInsert, which bypasses the
    // normal Insert path entirely (see SpacetimeBasicRepository.MigrateInsert's doc comment).
    internal class NoOpEventAggregator : IEventAggregator
    {
        public void PublishEvent<TEvent>(TEvent @event)
            where TEvent : class, IEvent
        {
        }
    }
}
