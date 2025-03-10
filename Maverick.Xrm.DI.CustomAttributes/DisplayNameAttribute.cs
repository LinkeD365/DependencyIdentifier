﻿using System;
using System.Collections.Generic;
using System.Text;

namespace Maverick.Xrm.DI.CustomAttributes
{
    public class DisplayAttribute : Attribute
    {
        internal DisplayAttribute(string displayName)
        {
            this.Name = displayName;
        }
        public string Name { get; private set; }
    }
}
