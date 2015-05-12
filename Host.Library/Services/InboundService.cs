using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Host.Library.Services
{
    public abstract class InboundService : AdapterService
    {
        public override ServiceConfig GetAdapterConfigMetadata()
        {
            var actionConfig = base.GetAdapterConfigMetadata();

            return actionConfig;
        }
    }
}
