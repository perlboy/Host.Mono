using Host.Services.Core.Helpers;
using Host.Library.Services;
using Integrator.Hub.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.Serialization.Json;
using System.ServiceModel;
using System.ServiceModel.Channels;
using System.ServiceModel.Description;
using System.ServiceModel.Web;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using System.Reflection;

namespace Host.Services.Core.Adapters
{
    public class RESTInboundService : TwowayInboundService
    {
        #region Public Member
        public static ConcurrentDictionary<Guid, IntegrationMessage> OutboundMessageSpool = new ConcurrentDictionary<Guid, IntegrationMessage>();
        public static ConcurrentDictionary<Guid, IntegrationMessage> InboundMessageSpool = new ConcurrentDictionary<Guid, IntegrationMessage>();
        #endregion

        private string _name = "restInboundService";
        Uri _baseAddress = null;
        RESTAdapterServiceHost _webServiceHost = null;
        public string HttpVerb { get; set; }
        public List<string> HttpVerbs 
        { 
            get 
            {
                return HttpVerb.Replace(" ", "").ToUpper().Split(',').ToList();
            } 
        }
        public override string Name
        {
            get
            {
                return _name;
            }
        }
        public override async Task Start()
        {
            try
            {
				if (Environment.GetEnvironmentVariable("MONO_STRICT_MS_COMPLIANT") != "yes") 
				{ 
					Environment.SetEnvironmentVariable("MONO_STRICT_MS_COMPLIANT", "yes"); 
				}

                this._baseAddress = new Uri(this.Config.config.staticConfig.FirstOrDefault(p => p.id == "uri").value.ToString());
                this.HttpVerb = this.Config.config.staticConfig.FirstOrDefault(p => p.id == "verb").value.ToString();
              
				_webServiceHost = new RESTAdapterServiceHost(typeof(RestService), _baseAddress) { Adapter = this };
				ServiceEndpoint restEndpoint = _webServiceHost.AddServiceEndpoint(typeof(RestService), new WebHttpBinding(), "");
                //restEndpoint.EndpointBehaviors.Add(new WebHttpBehavior { });

                ServiceDebugBehavior stp = _webServiceHost.Description.Behaviors.Find<ServiceDebugBehavior>();
                stp.HttpHelpPageEnabled = false;
                stp.IncludeExceptionDetailInFaults = true;
                var behaviour = _webServiceHost.Description.Behaviors.Find<ServiceBehaviorAttribute>();
                behaviour.InstanceContextMode = InstanceContextMode.Single;
                _webServiceHost.Open();


                this.Address = this._baseAddress.ToString();
                await base.Start();
            }
            catch (Exception ex)
            {
                this.HostChannel.LogEvent(string.Format("Receive Adapter {0} was could not be started. {1}", this.Name, ex.Message));
            }
        }
        public override async Task Stop()
        {
            _webServiceHost.Close();
            _webServiceHost = null;
            base.Stop();
        }
        public override List<ValidationError> Validate()
        {
            return base.Validate();
        }
        public override ServiceConfig GetAdapterConfigMetadata()
        {
            // Get base config
            var actionConfig = base.GetAdapterConfigMetadata();
            actionConfig.id = this.Name;
            actionConfig.type = this.Name;
            actionConfig.baseType = "twowayreceiveadapter";
            actionConfig.title = "Receive REST";
            actionConfig.assemblyType = this.GetType().FullName;
            actionConfig.description = "Receives messages to a REST endpoint.";
            actionConfig.image = "../../Images/restReceiveAdapter.png";

            actionConfig.config.staticConfig = new List<Host.Library.Services.ConfigProperty> 
                            {
                                new Host.Library.Services.ConfigProperty{id="uri", name = "URI", description = "Service address", type= "string", value = "http://server/service/"},
                                new Host.Library.Services.ConfigProperty {id="verb", name = "HTTP Verb", description = "HTTP Verb, Eg POST, PUT", type = "string", value = "POST" },
                                new Host.Library.Services.ConfigProperty {id="accept", name = "Accept Header", description = "Accepted content type", type = "string", value = "application/json" },
                                new Host.Library.Services.ConfigProperty {id="contenttype", name = "Content Type Header", description = "Content type of the messae submitted", type = "string", value = "application/json" },
                  
                            };
            actionConfig.config.securityConfig = new List<ConfigProperty> 
                            {
                                new ConfigProperty{id="userName", name = "User name", description = "User name", type= "string"},
                                new ConfigProperty{id="password", name = "Password", description = "Password", type= "password"},
                            };

            return actionConfig;
        }
        internal void SubmitMessage(IntegrationMessage message)
        {
            this.HostChannel.SubmitMessage(message, this.PersistOptions, this);
        }
        public override async Task ProcessMessage(IntegrationMessage message, bool isFirstAction)
        {
            InboundMessageSpool.TryAdd(message.InterchangeId, message);
        }

        public override byte[] GetImage()
        {
            return Host.Services.Core.Resources.RESTReceiveAdapter;
        }

        internal delegate void MessageReceived(IntegrationMessage message);

        internal event MessageReceived OnMessageReceived;
    }
		
    [ServiceContract]
    public class RestService
    {
        private RESTAdapterServiceHost host
        {
            get {
                return OperationContext.Current.Host as RESTAdapterServiceHost;
            }
        }

        [OperationContract]
		[WebInvoke(RequestFormat = WebMessageFormat.Json,
			ResponseFormat = WebMessageFormat.Json,
			UriTemplate = "*", 
			Method="*")]
        public System.IO.Stream ProcessRequest(Message message)
		{
			byte[] resultBytes = Encoding.UTF8.GetBytes (string.Empty);
			HttpRequestMessageProperty requestProperties = message.Properties [HttpRequestMessageProperty.Name] as HttpRequestMessageProperty;

			var contextInfo = OperationContext.Current.RequestContext.GetPrivatePropertyValue<object> ("Context");
			var requestInfo = contextInfo.GetPrivatePropertyValue<object> ("Request");
			var baseUri = requestInfo.GetPrivatePropertyValue<Uri> ("Url");
			var prefixUri = new Uri (baseUri.AbsoluteUri.Replace (baseUri.PathAndQuery, string.Empty));
			var template = new UriTemplate ("*");
			var uriTemplateMatch = template.Match (prefixUri, baseUri);

			if (!host.Adapter.HttpVerbs.Contains (requestProperties.Method)) {
				resultBytes = Encoding.UTF8.GetBytes ("Verb type not supported.");
				WebOperationContext.Current.OutgoingResponse.StatusCode = HttpStatusCode.NotAcceptable;
				return new MemoryStream (resultBytes);
			} else if (requestProperties.Method.ToUpper () == "GET" ||
			           requestProperties.Method.ToUpper () == "DELETE") {
				return ProcessNoneBodyRequest (message,uriTemplateMatch);
			} else {
				return ProcessBodyRequest (message,uriTemplateMatch);
			}
		}
        
		private System.IO.Stream ProcessNoneBodyRequest(Message message, UriTemplateMatch uriTemplateMatch)
        {
            HttpRequestMessageProperty requestProperties = message.Properties[HttpRequestMessageProperty.Name] as HttpRequestMessageProperty;
          
            // Create request
            string json = string.Empty;
			foreach (var q in uriTemplateMatch.QueryParameters.AllKeys)
            {
                json += string.IsNullOrEmpty(json) ? string.Empty : ", ";
				json += string.Format(@"""{0}"":""{1}""", q, uriTemplateMatch.QueryParameters[q]);
            }

            json = @"{""header"":{" + json + @"}, ""body"":{}}";

            // Create request message
            var msg = new IntegrationMessage
            {
                InterchangeId = Guid.NewGuid(),
                IntegrationId = host.Adapter.Config.integrationId,
                ItineraryId = host.Adapter.Config.itineraryId,
                CreatedBy = host.Adapter.Name,
                ContentType = "application/json",
                Itinerary = host.Adapter.Itinerary
            };
            
            msg.AddMessage(Encoding.UTF8.GetBytes(json));

            // Add verb to metadata so we can route on it.
            msg.Variables.Add(new ContextVariable { Type = "String", Variable = "Verb", Value = requestProperties.Method });

            // Make the internal call...
            host.Adapter.SubmitMessage(msg);
            IntegrationMessage responseMessage = new IntegrationMessage();

            bool success = false;
            while (!success)
            {
                try
                {
                    success = RESTInboundService.InboundMessageSpool.TryGetValue(msg.InterchangeId, out responseMessage);
                }
                catch (Exception ex)
                {
                }
                Thread.Sleep(500);
            }
            host.Adapter.TrackComplete(responseMessage);
            return CreateResponse(responseMessage, WebOperationContext.Current.IncomingRequest.Accept);
        }
		private System.IO.Stream ProcessBodyRequest(Message message, UriTemplateMatch uriTemplateMatch)
        {
            HttpRequestMessageProperty requestProperties = message.Properties[HttpRequestMessageProperty.Name] as HttpRequestMessageProperty;

            // Build up header message from query parameters
            string header = string.Empty;
			foreach (var q in uriTemplateMatch.QueryParameters.AllKeys)
            {
                header += string.IsNullOrEmpty(header) ? string.Empty : ", ";
				header += string.Format(@"""{0}"":""{1}""", q, uriTemplateMatch.QueryParameters[q]);
            }

            // Add the body (always json)
            string body = string.Empty;

            string contentType = requestProperties.Headers["Content-Type"];

            if (contentType == "application/xml")
                body = GetXmlMessage(message);
            else 
                body = GetJsonMessage(message);

            // Create request message
            var msg = new IntegrationMessage
            {
                InterchangeId = Guid.NewGuid(),
                IntegrationId = host.Adapter.Config.integrationId,
                ItineraryId = host.Adapter.Config.itineraryId,
                CreatedBy = host.Adapter.Name,
                ContentType = "application/json",
                Itinerary = host.Adapter.Itinerary
            };

            msg.AddMessage(Encoding.UTF8.GetBytes(body));

            // Add verb to metadata so we can route on it.
            msg.Variables.Add(new ContextVariable { Type = "String", Variable = "Verb", Value = requestProperties.Method });

            // Make the internal call...
            host.Adapter.SubmitMessage(msg);

            // Wait until message is returned
            IntegrationMessage responseMessage = new IntegrationMessage();
            bool success = false;
            while (!success)
            {
                try
                {
                    success = RESTInboundService.InboundMessageSpool.TryGetValue(msg.InterchangeId, out responseMessage);
                }
                catch (Exception ex)
                {
                }
                Thread.Sleep(500);
            }
            return CreateResponse(responseMessage, WebOperationContext.Current.IncomingRequest.Accept);

        }

        private System.IO.Stream CreateResponse(IntegrationMessage responseMessage, string contentType)
        {
            contentType = string.IsNullOrEmpty(contentType) ? "application/json; charset=utf-8" : contentType;
            byte[] resultBytes = Encoding.UTF8.GetBytes(string.Empty);

            // Check if aborted...
            if (responseMessage.ItineraryId == Guid.Empty)
            {
                resultBytes = Encoding.UTF8.GetBytes("Request was aborted from the server.");
                WebOperationContext.Current.OutgoingResponse.StatusCode = HttpStatusCode.InternalServerError;
                return new MemoryStream(resultBytes);
            }
            
            // Exception
            if (responseMessage.IsFault)
            {
                resultBytes = Encoding.UTF8.GetBytes(responseMessage.FaultDescripton);
                WebOperationContext.Current.OutgoingResponse.StatusCode = HttpStatusCode.InternalServerError;
                return new MemoryStream(resultBytes);
            }

            if (contentType.ToLower().StartsWith("application/json"))
            {
                WebOperationContext.Current.OutgoingResponse.ContentType = "application/json; charset=utf-8";
                resultBytes = responseMessage.MessageBuffer;
            }
            else if (contentType.ToLower().StartsWith("application/xml"))
            {
                WebOperationContext.Current.OutgoingResponse.ContentType = "application/xml; charset=utf-8";
                var jsonResponse = @"{""root"":" + Encoding.UTF8.GetString(responseMessage.MessageBuffer) + "}";
                var xmlNode = JsonConvert.DeserializeXmlNode(jsonResponse, "root");
                resultBytes = Encoding.UTF8.GetBytes(xmlNode.InnerXml);
            }
            else
            {
                // Default to json...
                WebOperationContext.Current.OutgoingResponse.ContentType = "application/json; charset=utf-8";
                resultBytes = responseMessage.MessageBuffer;
            }
            return new MemoryStream(resultBytes);
        }
        private string GetJsonMessage(Message message) {
            MemoryStream ms = new MemoryStream();
            XmlDictionaryWriter writer = JsonReaderWriterFactory.CreateJsonWriter(ms);
            message.WriteMessage(writer);
            writer.Flush();
            string jsonString = Encoding.UTF8.GetString(ms.ToArray());
            return jsonString;
        }
        private string GetXmlMessage(Message message) 
        {
           string xml = message.ToString();
           var xmlNode = XDocument.Parse(xml);
           var json = JsonConvert.SerializeXNode(xmlNode,Newtonsoft.Json.Formatting.None);
           return json;
        }
    }
}
