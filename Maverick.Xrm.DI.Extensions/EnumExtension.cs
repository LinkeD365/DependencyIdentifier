﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Maverick.Xrm.DI.CustomAttributes;

namespace Maverick.Xrm.DI.Extensions
{
    public static class EnumExtension
    {
        public static TAttribute GetAttribute<TAttribute>(this Enum value) where TAttribute : Attribute
        {
            var type = value.GetType();
            var name = Enum.GetName(type, value);
            return type.GetField(name)
                .GetCustomAttributes(false)
                .OfType<TAttribute>()
                .SingleOrDefault();
        }
    }
}
