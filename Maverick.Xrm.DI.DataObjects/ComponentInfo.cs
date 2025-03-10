﻿using System;
using System.Collections.Generic;
using System.Text;

namespace Maverick.XTB.DI.DataObjects
{
    public class ComponentInfo
    {
        public ComponentInfo()
        {
            Description = "--";
            IsDashboard = false;
        }
        public string Name { get; set; }
        public string Description { get; set; }
        public bool IsDashboard { get; set; }
    }
}
