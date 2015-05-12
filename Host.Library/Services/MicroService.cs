using Host.Library.Services.Interfaces;
using Integrator.Hub.Models;
using Jint;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;

namespace Host.Library.Services
{
    public abstract class MicroService : IMicroService
    {
        private string _adapterConfig;
        abstract public string Name { get; }
        public Guid OrganizationId { get; set; }
        public Guid IntegrationId { get; set; }
        public Guid ItineraryId { get; set; }
        public HostChannel HostChannel { get; set; }
        public PersistOptions PersistOptions { get; set; }
        public dynamic Itinerary { get; set; }
        public ServiceConfig Config
        {
            get
            {
                var config = JsonConvert.DeserializeObject<ServiceConfig>(this._adapterConfig);
                return config;
            }
            set
            {
                this._adapterConfig = JsonConvert.SerializeObject(value);
            }
        }
        public ServiceConfig GetActionConfig(List<ContextVariable> contextVariables)
        {
            var config = JsonConvert.DeserializeObject<ServiceConfig>(this._adapterConfig);
            Regex regex = new Regex(@"{(.*?)}");


            // Overide configuration with variables
            foreach (var dynamicConfigProperty in config.config.dynamicConfig)
            {
                if (dynamicConfigProperty.value == null)
                    continue;

                var match = regex.Match(dynamicConfigProperty.value.ToString());

            
                while (match.Success)
                {
                    var variable = contextVariables.FirstOrDefault(v => v.Variable == match.Value.Replace("{", "").Replace("}", ""));
                    if (variable == null)
                        throw new ApplicationException("Variable not found.");

                    dynamicConfigProperty.value = dynamicConfigProperty.value.ToString().Replace(match.ToString(), variable.Value.ToString());
                    match = match.NextMatch();
                }
            }
            foreach (var securityConfigProperty in config.config.securityConfig)
            {
                if (securityConfigProperty.value == null)
                    continue;

                var match = regex.Match(securityConfigProperty.value.ToString());

                while (match.Success)
                {
                    var variable = contextVariables.FirstOrDefault(v => v.Variable == match.Value.Replace("{", "").Replace("}", ""));
                    if (variable == null)
                        throw new ApplicationException("Variable not found.");

                    securityConfigProperty.value = securityConfigProperty.value.ToString().Replace(match.ToString(), variable.Value.ToString());
                    match = match.NextMatch();
                }
            }
            foreach (var staticConfigProperty in config.config.staticConfig)
            {
                if (staticConfigProperty.value == null)
                    continue;

                var match = regex.Match (staticConfigProperty.value.ToString());

                
                while (match.Success)
                {
                    var variable = contextVariables.FirstOrDefault(v => v.Variable == match.Value.Replace("{", "").Replace("}", ""));
                    if (variable == null)
                        throw new ApplicationException("Variable not found.");

                    staticConfigProperty.value = staticConfigProperty.value.ToString().Replace(match.ToString(), variable.Value.ToString());
                    match = match.NextMatch();
                }
            }
            return config;
        }
        public virtual Task ProcessMessage(IntegrationMessage msg, bool isFirstAction) 
        {
            this.HostChannel.TrackEvent(new TrackingMessage(msg.MessageBuffer, msg.ContentType)
            {
                LastActivity = this.Config.id,
                NextActivity = null,
                Host = null,
                Variables = msg.Variables,
                OrganizationId = Guid.Empty,
                IntegrationId = msg.IntegrationId,
                InterchangeId = msg.InterchangeId,
                ItineraryId = msg.ItineraryId,
                TimeStamp = DateTime.UtcNow,
                State = TrackingMessageStatusCodes.Complete,
                IsFirstAction = isFirstAction
            });
            return null;
        }
        public virtual ServiceConfig GetAdapterConfigMetadata() {
            return new ServiceConfig
            {
                config = new ConfigSection
                {
                    generalConfig = new List<ConfigProperty> 
                            {
                                new ConfigProperty{id="name", name = "Name", description = "Name of adapter", type= "string", value = this.Name},
                                new ConfigProperty {id="description", name = "Description", description = "Transmits files to a directory.", type = "string"},
                                new ConfigProperty {id="host", name = "Host", description = "Which host do you want to handle this service?", type = "string"},
                                new ConfigProperty {id="enabled", name = "Enabled", description = "Activate the service", type = "bool", value=true}
                            },
                    staticConfig = new List<Host.Library.Services.ConfigProperty> 
                            {
                                new ConfigProperty{id="inboundScript", name = "Inbound script", description = "Script", type= "script", visibility="hidden", value = string.Empty},
                                new ConfigProperty{id="outboundScript", name = "Outbound script", description = "Script", type= "script", visibility="hidden", value = string.Empty},
                            },
                    securityConfig = new List<ConfigProperty> (),
                    dynamicConfig = new List<ConfigProperty> (),
                }
            };
        
        }
        public virtual new ServiceConfig GetDynamicConfiguration(ServiceConfig actionConfig) { return actionConfig; }
        public IntegrationMessage RunInboundScript(IntegrationMessage message, bool evaluate = false)
        {
            var inboundScriptConfig = this.Config.config.staticConfig.FirstOrDefault(p => p.id == "inboundScript");

            if (inboundScriptConfig != null)
            {
                var script = inboundScriptConfig.value.ToString();
                if (!string.IsNullOrEmpty(script))
                {
                    return RunScript(message, script, evaluate);
                }
            }
            
            return message;
        }
        public IntegrationMessage RunOutboundScript(IntegrationMessage message, bool evaluate = false)
        {
            var outboundScriptConfig = this.Config.config.staticConfig.FirstOrDefault(p => p.id == "outboundScript");

            if (outboundScriptConfig != null)
            {
                var script = outboundScriptConfig.value.ToString();
                if (!string.IsNullOrEmpty(script))
                {
                    return RunScript(message, script, evaluate);
                }
            }
                return message;
        }
        public IntegrationMessage RunScript(IntegrationMessage message, string script, bool evaluate = false)
        {
            IntegrationMessage responseMessage = null;
            try
            {
                var engine = new Engine();

                engine.SetValue("console", new console { HostChannel = this.HostChannel });
                engine.SetValue("xmlHelper", new xmlHelper());
                engine.SetValue("context", new context(message.Variables, message));

                string json = string.Empty;

                if (!message.IsBinary)
                {
                    json = JsonConvert.SerializeObject(message.message);
                    script = "var message = " + json + @";" + script + " message = JSON.stringify(message);";
                }

                // Add variables
                foreach (var cv in message.Variables)
                {
                    string text = string.Empty;

                    if (cv.Type == null || cv.Type == ContextVariable.STRINGTYPE || cv.Type == ContextVariable.DATETIMEYPE)
                        text = string.Format("{0} = '{1}';", cv.Variable, cv.Value.ToString());
                    else if (cv.Type == ContextVariable.MESSAGETYPE)
                    {

                    }
                    else
                        text = string.Format("{0} = {1};", cv.Variable, cv.Value = string.IsNullOrEmpty(cv.Value.ToString()) ? "{}" : cv.Value.ToString());

                    engine.Execute(text);
                }

                engine.Execute(script);

                // Read back variables
                foreach (var cv in message.Variables)
                {
                    try
                    {
                        if (cv.Type != ContextVariable.MESSAGETYPE)
                        {
                            engine.Execute(string.Format(@"var MEMBERVARIABLE = {0}", cv.Variable));
                            cv.Value = engine.GetValue("MEMBERVARIABLE").AsString();
                        }
                    }
                    catch (Exception ex)
                    { }
                }

                if (!message.IsBinary)
                {
                    json = engine.GetValue("message").ToString();

                    if (json.Replace(@"""", string.Empty) != IntegrationMessage.IGNOREMESSAGE)
                        message.message = JsonConvert.DeserializeObject(json);
                }
                responseMessage = message;

                responseMessage.Sender = this.Name;

            }
            catch (Exception ex)
            {
                throw new ApplicationException("Unable to execute script. " + ex.Message);
            }
            return responseMessage;
        }
        public bool ValidateRoutingExpression(IntegrationMessage message)
        {
            var script = string.Empty;
            try
            {

                var routingExpressionConfig = this.Config.config.staticConfig.FirstOrDefault(p => p.id == "routingExpression");

                if (routingExpressionConfig != null)
                {
                    script = routingExpressionConfig.value.ToString();
                    if (string.IsNullOrEmpty(script))
                    {
                        return true;
                    }
                }
                else
                    return true;

                var engine = new Engine();

                engine.SetValue("console", new console { HostChannel = this.HostChannel });
                engine.SetValue("xmlHelper", new xmlHelper());
                engine.SetValue("context", new context(message.Variables, message));
                var json = JsonConvert.SerializeObject(message.message);
                script = "var route = true; var message = " + json + @";" + script;

                // Add variables
                foreach (var cv in message.Variables)
                {
                    string text = string.Empty;

                    if (cv.Type == null || cv.Type == ContextVariable.STRINGTYPE || cv.Type == ContextVariable.DATETIMEYPE)
                        text = string.Format("{0} = '{1}';", cv.Variable, cv.Value.ToString());
                    else if (cv.Type == ContextVariable.MESSAGETYPE)
                    {

                    }
                    else
                        text = string.Format("{0} = {1};", cv.Variable, cv.Value = string.IsNullOrEmpty(cv.Value.ToString()) ? "{}" : cv.Value.ToString());

                    engine.Execute(text);
                }

                engine.Execute(script);

                // Read back variables
                foreach (var cv in message.Variables)
                {
                    try
                    {
                        if (cv.Type != ContextVariable.MESSAGETYPE)
                        {
                            engine.Execute(string.Format(@"var MEMBERVARIABLE = {0}", cv.Variable));
                            cv.Value = engine.GetValue("MEMBERVARIABLE");
                        }
                    }
                    catch (Exception ex)
                    { }
                }

                bool route = engine.GetValue("route").AsBoolean();

                return route;

            }
            catch (Exception ex)
            {
                this.HostChannel.LogEvent(string.Format("Unable to validate routing expression: {0}", script));
                throw;
            }
        }
        public string PrepareScript(string script)
        {
            script = script.Replace(@"""", "'");

            return script;
        }
        public bool IsEnabled()
        {
            var enabledConfig = this.Config.config.generalConfig.FirstOrDefault(g => g.id == "enabled");
            if (enabledConfig == null)
                return true;
            else
                return (bool)enabledConfig.value;
        }
        public virtual byte[] GetImage() 
        {
            return Host.Library.Resource.Unknown;
        }
        public virtual List<ValidationError> Validate() 
        {
            var validationErros = new List<ValidationError>();
            var generalConfig = this.Config.config.generalConfig;

            if (generalConfig.FirstOrDefault(g => g.id == "name") == null ||
                generalConfig.FirstOrDefault(g => g.id == "name").value == null ||
                string.IsNullOrEmpty(generalConfig.FirstOrDefault(g => g.id == "name").value.ToString()))
                validationErros.Add(new ValidationError { Source = this.Config.id, Property = "Name", Error = "Name can not be left empty." });

            if (generalConfig.FirstOrDefault(g => g.id == "host") == null ||
               generalConfig.FirstOrDefault(g => g.id == "host").value == null ||
               string.IsNullOrEmpty(generalConfig.FirstOrDefault(g => g.id == "host").value.ToString()))
                validationErros.Add(new ValidationError { Source = this.Config.id, Property = "Host", Error = "Host must be set." });


            return validationErros;
        }
    }
    
    public class console
    {
        public HostChannel HostChannel { get; set; }
        public void log(object msg)
        {
            if(msg is string)
                this.HostChannel.LogEvent(msg.ToString());
            else
            {
                var json = JsonConvert.SerializeObject(msg);
                this.HostChannel.LogEvent(json);
            }
                
        }
    }
    public class context
    {
        IntegrationMessage _message;
        List<ContextVariable> _variables;
        public context(List<ContextVariable> variables, IntegrationMessage message)
        {
            _variables = variables;
            _message = message;
        }
        public void add(string variable, string val)
        {
            _variables.Add(new ContextVariable { Variable = variable, Value = val });
        }
        public void copyTo(string variableName)
        {
            this._message.copyTo(variableName);
        }
        public void copyFrom(string variableName)
        {
            this._message.copyFrom(variableName);
        }
        public string contentType
        {
            get { return this._message.ContentType; }
            set { this._message.ContentType = value; }
        }
    }
    public class xmlHelper
    {
        public dynamic removeNamespaces(dynamic obj)
        {
            var json = JsonConvert.SerializeObject(obj);
            XmlDocument xmlDocument = JsonConvert.DeserializeXmlNode(json, "root", false);
            XElement xmlDocumentWithoutNs = RemoveAllNamespaces(XElement.Parse(xmlDocument.InnerXml));
            json = JsonConvert.SerializeXNode(xmlDocumentWithoutNs);
            dynamic ret = JsonConvert.DeserializeObject(json);
            return ret.root.Envelope.Body;
        }
        private XElement RemoveAllNamespaces(XElement xmlDocument)
        {
            if (!xmlDocument.HasElements)
            {
                XElement xElement = new XElement(xmlDocument.Name.LocalName);
                xElement.Value = xmlDocument.Value;

                foreach (XAttribute attribute in xmlDocument.Attributes())
                    xElement.Add(attribute);

                return xElement;
            }
            return new XElement(xmlDocument.Name.LocalName, xmlDocument.Elements().Select(el => RemoveAllNamespaces(el)));
        }
    }

}
