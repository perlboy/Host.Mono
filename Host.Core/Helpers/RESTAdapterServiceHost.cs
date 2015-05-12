using Host.Services.Core.Adapters;
using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceModel.Web;
using System.Text;
using System.Threading.Tasks;

namespace Host.Services.Core.Helpers
{
    public class RESTAdapterServiceHost : WebServiceHost
    {
        public RESTAdapterServiceHost(Type serviceType, params Uri[] baseAddresses)
            : base(serviceType, baseAddresses) { }
        public RESTInboundService Adapter { get; set; }
    }
}
