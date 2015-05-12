using Host.Library.Services.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Host.Library.Services
{
    public abstract class AdapterService : MicroService, IAdapterService
    {
        public string Address { get; set; }
        public string ContentType { get; set; }
       
        public virtual async Task Start()
        {
            this.HostChannel.LogEvent(String.Format("| {0,20}| {1,10}| {2,40}|", this.Name.PadRight(20), "Started  ", this.Address.PadRight(40)));
            //this.HostChannel.LogEvent(string.Format("{0} was successfully started {1}", this.Name, string.IsNullOrEmpty(this.Address) ? string.Empty : "at " + this.Address));
        }
        public virtual async Task Stop()
        {
            this.HostChannel.LogEvent(String.Format("| {0,20}| {1,10}| {2,40}|", this.Name.PadRight(20), "Stopped  ",""));
 
 //           this.HostChannel.LogEvent(string.Format("{0} was stopped.", this.Name));
        }
       
        public override ServiceConfig GetAdapterConfigMetadata()
        {
            var actionConfig = base.GetAdapterConfigMetadata();

            var correlationConfig = new ConfigProperty
            {
                id = "correlationId",
                name = "Correlation Id",
                description = "The unique identifier of the instance.",
                type = "string"
            };

            actionConfig.config.generalConfig.Add(correlationConfig);
            return actionConfig;
        }
        public override List<ValidationError> Validate()
        {
            return base.Validate();
        }
    }
    public class ValidationError
    {
        public string Id { get; set; }
        public string Source { get; set; }
        public string Property { get; set; }
        public string Error { get; set; }
    }
    public class ActionError
    {
        public string id { get; set; }
        public List<ValidationError> errors { get; set; }
    }
}
