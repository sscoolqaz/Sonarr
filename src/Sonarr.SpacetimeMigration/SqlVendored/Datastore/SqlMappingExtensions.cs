using System.Reflection;
using Dapper;
using NzbDrone.Common.Reflection;

namespace NzbDrone.Core.Datastore
{
    internal static class SqlMappingExtensions
    {
        public static bool IsMappableProperty(this MemberInfo memberInfo)
        {
            var propertyInfo = memberInfo as PropertyInfo;

            if (propertyInfo == null)
            {
                return false;
            }

            if (!propertyInfo.IsReadable() || !propertyInfo.IsWritable())
            {
                return false;
            }

#pragma warning disable 618
            SqlMapper.LookupDbType(propertyInfo.PropertyType, "", false, out var handler);
#pragma warning restore 618
            if (propertyInfo.PropertyType.IsSimpleType() || handler != null)
            {
                return true;
            }

            return false;
        }
    }
}
