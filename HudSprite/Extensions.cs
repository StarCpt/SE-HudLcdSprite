using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HudSprite;

internal static class Extensions
{
    public static void RemoveFast<T>(this List<T> list, T item)
    {
        int i = list.IndexOf(item);
        if (i >= 0)
        {
            list.RemoveAtFast(i);
        }
    }
}
