using System;
using System.Collections.Generic;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Update.History
{
    public interface IUpdateHistoryRepository : IBasicRepository<UpdateHistory>
    {
        UpdateHistory LastInstalled();
        UpdateHistory PreviouslyInstalled();
        List<UpdateHistory> InstalledSince(DateTime dateTime);
    }
}
