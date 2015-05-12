using Host.Library.Services;
using Integrator.Hub.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Host.Services.Core.Actions
{
    public class JavaScriptService : MicroService
    {
       

        private string _name = "javascriptaction";
        public override string Name
        {
            get
            {
                return _name;
            }
        }
        public override ServiceConfig GetAdapterConfigMetadata()
        {
            // Get base config
            var actionConfig = base.GetAdapterConfigMetadata();

            actionConfig.id = "javascriptaction";
            actionConfig.type = "javascriptaction";
            actionConfig.baseType = "javascriptaction";
            actionConfig.title = "JavaScript";
            actionConfig.assemblyType = this.GetType().FullName;
            actionConfig.description = "Executes Scripts";
            actionConfig.image = "../../Images/javascriptaction.png";

            // Set collections
            actionConfig.config.staticConfig = new List<Host.Library.Services.ConfigProperty> 
                            {
                                new ConfigProperty{id="script", name = "Script", description = "Script", type= "script", visibility="hidden", value = @"function transform(source, destination) {
        /*
        You can transform using the source- and destination parameters, Eg:

        destination.id = source.number
        destination.fullName = source.firstName + ' ' + source.lastName;
        */


}"},                            new ConfigProperty
            {
                id = "routingExpression",
                name = "Routing expression",
                description = "Script",
                type = "script",
                visibility = "hidden",
                value = @"// This expression is evaluated on the 'route' variable.
var route = true;"
            }       };
            actionConfig.config.securityConfig = new List<Host.Library.Services.ConfigProperty>();


            return actionConfig;

        }
        public override async Task ProcessMessage(IntegrationMessage message, bool isFirstAction)
        {
            try
            {
                if (message.IsFault)
                {
                    await this.HostChannel.SubmitMessage(message, this.PersistOptions, this);
                    return;
                }
                var script = this.Config.config.staticConfig.FirstOrDefault(p => p.id == "script").value.ToString();
                var newMessage = this.RunScript(message, script);
                this.HostChannel.SubmitMessage(newMessage, this.PersistOptions, this);
                base.ProcessMessage(message,false);
            }
            catch (Exception ex)
            {
                this.HostChannel.LogEvent("Unable execute script");
                this.HostChannel.TrackEvent(new TrackingMessage(message._messageBuffer, message.ContentType)
                {
                    FaultCode = "0",
                    FaultDescription = ex.Message,
                    State = TrackingMessageStatusCodes.Failed,
                    InterchangeId = message.InterchangeId,
                    IntegrationId = message.IntegrationId,
                    ItineraryId = message.ItineraryId,
                    LastActivity = this.Name,
                    TimeStamp = DateTime.UtcNow,
                    Variables = message.Variables
                });
                message.FaultCode = "500";
                message.FaultDescripton = ex.Message;
                message.IsFault = true;
                this.HostChannel.SubmitMessage(message, this.PersistOptions, this);

            }
        }
        public override byte[] GetImage()
        {
            return Host.Services.Core.Resources.JavaScriptAction;
        }
        public override List<ValidationError> Validate()
        {
            return base.Validate();

        }
    }
    
}
