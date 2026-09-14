using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Update.History
{
    public interface IUpdateHistoryRepository : IBasicRepository<UpdateHistory>
    {
        Task<UpdateHistory> LastInstalled();
        Task<UpdateHistory> PreviouslyInstalled();
        Task<List<UpdateHistory>> InstalledSince(DateTime dateTime);
    }
}
