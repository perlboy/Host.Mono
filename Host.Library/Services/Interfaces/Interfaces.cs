using Integrator.Hub.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Host.Library.Services.Interfaces
{
    public interface IMicroService
    {
        ServiceConfig GetAdapterConfigMetadata();
        Task ProcessMessage(IntegrationMessage file, bool isFirstAction);
        List<ValidationError> Validate();
    }
    public interface IAdapterService
    {
        Task Start();
        Task Stop();


    }
    public interface IOnewayInboundService { }
    public interface IOnewayOutboundService
    {

    }
    public interface ITwowayOutboundService { }
    public interface ITwowayInboundService { }

}
