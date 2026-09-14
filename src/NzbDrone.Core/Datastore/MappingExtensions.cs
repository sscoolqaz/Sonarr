using System;
using System.Linq.Expressions;
using System.Reflection;

namespace NzbDrone.Core.Datastore
{
    public static class MappingExtensions
    {
        public static PropertyInfo GetMemberName<T, TChild>(this Expression<Func<T, TChild>> member)
        {
            if (!(member.Body is MemberExpression memberExpression))
            {
                memberExpression = (member.Body as UnaryExpression).Operand as MemberExpression;
            }

            return (PropertyInfo)memberExpression.Member;
        }
    }
}
