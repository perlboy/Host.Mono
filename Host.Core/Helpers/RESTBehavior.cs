
using System;
using System.Net;
using System.ServiceModel;
using System.ServiceModel.Channels;
using System.ServiceModel.Dispatcher;
using System.ServiceModel.Web;
using System.ServiceModel.Description;

namespace Host.Services.Core.Helpers
{
	public class RESTBehavior : IEndpointBehavior
	{
		#region IEndpointBehavior implementation

		public void AddBindingParameters (ServiceEndpoint endpoint, BindingParameterCollection parameters)
		{
			throw new NotImplementedException ();
		}

		public void ApplyDispatchBehavior (ServiceEndpoint serviceEndpoint, EndpointDispatcher dispatcher)
		{
			throw new NotImplementedException ();
		}

		public void ApplyClientBehavior (ServiceEndpoint serviceEndpoint, ClientRuntime behavior)
		{
			throw new NotImplementedException ();
		}

		public void Validate (ServiceEndpoint serviceEndpoint)
		{
			throw new NotImplementedException ();
		}

		#endregion


	}
}