using Host.Library.Helpers;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Dynamic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Web;
using System.Xml;

namespace Integrator.Hub.Models
{
    public class IntegrationMessage
    {
        public const string IGNOREMESSAGE = "@IGNORE@";
        public const string BINARYINDICATOR = "@BINARY@";
        private dynamic _itinerary;
        public  byte[] _messageBuffer;
        public IntegrationMessage()
        {
            this.MessageId = Guid.NewGuid();
            Created = DateTime.UtcNow;
            this.Metadata = new Dictionary<string, string>();
        }
        public Guid OrganizationId { get; set; }
        public Guid IntegrationId { get; set; }
        public Guid ItineraryId { get; set; }
        public Guid InterchangeId { get; set; }
        public List<ContextVariable> Variables { get; set; }
        public dynamic Itinerary
        {
            get { return _itinerary; }
            set
            {
                _itinerary = value;

                // Init all context variables
                if (this.Variables == null)
                {
                    this.Variables = new List<ContextVariable>();
                    var variables = _itinerary.variables as JArray;
                    if (variables != null)
                    {
                        foreach (var v in variables)
                        {
                            this.Variables.Add(new ContextVariable
                            {
                                Variable = v["Variable"].ToString(),
                                Value = v["Value"].ToString(),
                                Type = v["Type"].ToString()
                            });
                        }
                    }
                }
            }
        }
        public Guid MessageId { get; set; }
        public DateTime Created { get; set; }
        public string CreatedBy { get; set; }
        public string Sender { get; set; }
        public bool ReplyToSender { get; set; }
        public bool IsReplyMessage { get; set; }
        public bool IsBroadcast { get; set; }
        public bool IsFault { get; set; }
        public bool IsRequestResponse { get; set; }
        public string FaultCode { get; set; }
        public string FaultDescripton { get; set; }
        public Dictionary<string, string> Metadata { get; set; }
        public byte[] MessageBuffer 
        {
            get {
                switch (this.ContentType)
                {
                    case "text/xml":
                    case "application/xml":
                    case "application/json":
                        return this._messageBuffer;
                    default:
                        string base64String = Encoding.UTF8.GetString(this._messageBuffer);
                        if (this.IsBinary && base64String.StartsWith(@"""") && base64String.EndsWith(@""""))
                        {
                            base64String = base64String.Substring(1, base64String.Length - 2); 
                        }
                        if (this.IsBinary && base64String.StartsWith(@"\""") && base64String.EndsWith(@"\"""))
                        {
                            base64String = base64String.Substring(2, base64String.Length - 4);
                        }
                        else
                        {
                            string c = "Should not happen";
                        }
                        this._messageBuffer = Encoding.UTF8.GetBytes(base64String);
                        return Convert.FromBase64String(base64String);
                    
                }
            }
        }
        public string ContentType{ get; set; }
        public bool IsCorrelation { get; set; }
        public string CorrelationId { get; set; }
        public bool IsBinary {
            get
            {
                switch (this.ContentType)
                {
                    case "text/xml":
                    case "application/xml":
                    case "application/json":
                        return false;
                    default:
                        return true;;
                }
            }
        }
        public override string ToString()
        {
            string text = Encoding.UTF8.GetString(this.MessageBuffer);

            return text;
        }
        public void AddMessage(byte[] bytes)
        {
            switch (this.ContentType)
            {
                case "text/xml":
                case "application/xml":
                case "application/soap+xml":
                case "application/json":
                    this._messageBuffer = bytes;
                    break;
                default:
                     string base64String = Convert.ToBase64String(bytes);
                    this._messageBuffer = Encoding.UTF8.GetBytes(base64String);
                    break;
            }
        }
        public IntegrationMessage Clone()
        {
            var json = JsonConvert.SerializeObject(this);
            var clone = JsonConvert.DeserializeObject<IntegrationMessage>(json);
            clone.Created = DateTime.UtcNow;
            clone._messageBuffer = this._messageBuffer;
            clone.ContentType = this.ContentType;
            return clone;
        }
        private static void CopyTo(Stream src, Stream dest)
        {
            byte[] bytes = new byte[4096];

            int cnt;

            while ((cnt = src.Read(bytes, 0, bytes.Length)) != 0)
            {
                dest.Write(bytes, 0, cnt);
            }
        }
        public byte[] Compress()
        {
            string text = Encoding.UTF8.GetString(this._messageBuffer);
            if (this.IsBinary)
            {
                string base64String = Encoding.UTF8.GetString(this._messageBuffer);

                if (this.IsBinary && base64String.StartsWith(@"""") && base64String.EndsWith(@""""))
                {
                    base64String = base64String.Substring(1, base64String.Length - 2);
                }
                if (this.IsBinary && base64String.StartsWith(@"\""") && base64String.EndsWith(@"\"""))
                {
                    base64String = base64String.Substring(2, base64String.Length - 4);
                }

                this._messageBuffer = Encoding.UTF8.GetBytes(base64String);

            }
            text = this.ToString();

            string str = JsonConvert.SerializeObject(this);
            var bytes = Encoding.UTF8.GetBytes(str);

            using (var msi = new MemoryStream(bytes))
            using (var mso = new MemoryStream())
            {
                using (var gs = new GZipStream(mso, CompressionMode.Compress))
                {
                    //msi.CopyTo(gs);
                    CopyTo(msi, gs);
                }

                return mso.ToArray();
            }
        }
        public static IntegrationMessage Decompress(byte[] bytes)
        {
            try
            {
                using (var msi = new MemoryStream(bytes))
                using (var mso = new MemoryStream())
                {
                    using (var gs = new GZipStream(msi, CompressionMode.Decompress))
                    {
                        //gs.CopyTo(mso);
                        CopyTo(gs, mso);
                    }

                    var json = Encoding.UTF8.GetString(mso.ToArray());
                    var message = JsonConvert.DeserializeObject<IntegrationMessage>(json);
                    
                    if(message.IsBinary)
                    {
                        string base64String = Encoding.UTF8.GetString(message._messageBuffer);

                        if (message.IsBinary && base64String.StartsWith(@"""") && base64String.EndsWith(@""""))
                        {
                            base64String = base64String.Substring(1, base64String.Length - 2);
                        }
                        if (message.IsBinary && base64String.StartsWith(@"\""") && base64String.EndsWith(@"\"""))
                        {
                            base64String = base64String.Substring(2, base64String.Length - 4);
                        }

                       
                        message._messageBuffer = Encoding.UTF8.GetBytes(base64String);

                    }
                    return message;
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }
        public object message
        {
            get
            {
                if (this.ContentType == "application/json")
                {
                    var json = Encoding.UTF8.GetString(this._messageBuffer);
                    var obj = JsonConvert.DeserializeObject(json);
                    //var obj = JsonConvert.DeserializeObject<ExpandoObject>(json, new ExpandoObjectConverter());
                    return obj;
                }
                else if (this.ContentType == "application/xml" || this.ContentType == "text/xml" || this.ContentType == "application/soap+xml")
                {
                    var xml = Encoding.UTF8.GetString(this._messageBuffer);
                    return xml;
                }
                else
                {
                    return Encoding.UTF8.GetString(this._messageBuffer);
                    //return IntegrationMessage.IGNOREMESSAGE;
                }
            }
            set
            {
                this.ContentType = string.IsNullOrEmpty(this.ContentType) ? "application/json" : this.ContentType;
                var json = JsonConvert.SerializeObject(value);
                var bytes = Encoding.UTF8.GetBytes(json);
                this._messageBuffer = bytes;
            }
        }
        public void copyTo(string variableName)
        {
            var variable = this.Variables.FirstOrDefault(v => v.Variable == variableName);
            if (variable == null)
                throw new ApplicationException("Variable " + variableName + " does not exist.");
            if (variable.Type != ContextVariable.MESSAGETYPE)
                throw new ApplicationException(@"A message can only be copied to a vaiable of the type ""Message"".");

            string base64String = Convert.ToBase64String(this._messageBuffer);
            
            variable.Value = base64String;
            var l = base64String.Length;
            var temp = this.ToString();
        }
        public void copyFrom(string variableName)
        {
            var variable = this.Variables.FirstOrDefault(v => v.Variable == variableName);
            if (variable == null)
                throw new ApplicationException("Variable " + variableName + " does not exist.");
            if (variable.Type != ContextVariable.MESSAGETYPE)
                throw new ApplicationException(@"A message can only be copied to a vaiable of the type ""Message"".");

            string base64String = variable.Value.ToString();
            var l = base64String.Length;
            var bytes = Convert.FromBase64String(base64String);

            this._messageBuffer = bytes;
        }


        // Only used for  validation
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string ValidationErrors { get; set; }
    }

    public class TrackingMessage
    {
        public TrackingMessage(byte[] message, string contentType)
        {
            try
            {
                this._message = message;
                this.ContentType = contentType;
                this.TimeStamp = DateTime.UtcNow;
            }
            catch { }
        }
        public byte[] _message;
        public byte[] _itinerary;

        public Guid OrganizationId { get; set; }
        public Guid IntegrationId { get; set; }
        public Guid InterchangeId { get; set; }
        public Guid ItineraryId { get; set; }
        public List<ContextVariable> Variables { get; set; }
        public string Host { get; set; }
        public string LastActivity { get; set; }
        public string NextActivity { get; set; }
        public DateTime TimeStamp { get; set; }
        public string ContentType { get; set; }
        public byte[] Message
        {
            get
            {
                return this._message;
            }
        }
        public string State { get; set; }
        public bool IsFirstAction { get; set; }
        public string FaultCode { get; set; }
        public string FaultDescription { get; set; }
        private void CopyTo(Stream src, Stream dest)
        {
            byte[] bytes = new byte[4096];

            int cnt;

            while ((cnt = src.Read(bytes, 0, bytes.Length)) != 0)
            {
                dest.Write(bytes, 0, cnt);
            }
        }
        private byte[] Compress(string str)
        {
            var bytes = Encoding.UTF8.GetBytes(str);

            using (var msi = new MemoryStream(bytes))
            using (var mso = new MemoryStream())
            {
                using (var gs = new GZipStream(mso, CompressionMode.Compress))
                {
                    //msi.CopyTo(gs);
                    CopyTo(msi, gs);
                }

                return mso.ToArray();
            }
        }
        private string Decompress(byte[] bytes)
        {
            using (var msi = new MemoryStream(bytes))
            using (var mso = new MemoryStream())
            {
                using (var gs = new GZipStream(msi, CompressionMode.Decompress))
                {
                    //gs.CopyTo(mso);
                    CopyTo(gs, mso);
                }

                return Encoding.UTF8.GetString(mso.ToArray());
            }
        }
        public void AddItinerary(dynamic itinerary)
        {
            var json = JsonConvert.SerializeObject(itinerary);
            var bytes = Encoding.UTF8.GetBytes(json);
            string base64String = Convert.ToBase64String(bytes);
            this._itinerary = Encoding.UTF8.GetBytes(base64String);
        }
        public string GetVariablesData()
        {
            return JsonConvert.SerializeObject(this.Variables);
        }
    }
    public class TrackingMessage2
    {
        public TrackingMessage2(dynamic message, dynamic itinerary)
        {
            try
            {
                var json = JsonConvert.SerializeObject(itinerary);
                this._itinerary = Compress(json);
                this.ItineraryByteString = json;// Convert.ToBase64String(this._itinerary);
                json = JsonConvert.SerializeObject(message);
                this._message = Compress(json);
            }
            catch { }
        }

        private byte[] _itinerary;
        private byte[] _message;

        public Guid OrganizationId { get; set; }
        public Guid IntegrationId { get; set; }
        public Guid InterchangeId { get; set; }
        public Guid ItineraryId { get; set; }
        public byte[] Itinerary { get { return _itinerary; } }

        public string ItineraryByteString { get; set; }

        public string Host { get; set; }
        public string LastActivity { get; set; }
        public string NextActivity { get; set; }
        public DateTime TimeStamp { get; set; }
        public byte[] Message { get { return _message; } }
        public string State { get; set; }
        public string FaultCode { get; set; }
        public string FaultDescription { get; set; }

        public dynamic GetItinerary()
        {
            try
            {
                byte[] data = Convert.FromBase64String(this.ItineraryByteString); 
                var json = Decompress(this.Itinerary);
                dynamic response = JsonConvert.DeserializeObject(json);
                return response;
            }
            catch { return null; }
        }
        public dynamic GetMessage()
        {
            try
            {
                var json = Decompress(this.Message);
                dynamic response = JsonConvert.DeserializeObject(json);
                return response;
            }
            catch { return null; }
        }

        private void CopyTo(Stream src, Stream dest)
        {
            byte[] bytes = new byte[4096];

            int cnt;

            while ((cnt = src.Read(bytes, 0, bytes.Length)) != 0)
            {
                dest.Write(bytes, 0, cnt);
            }
        }
        private byte[] Compress(string str)
        {
            var bytes = Encoding.UTF8.GetBytes(str);

            using (var msi = new MemoryStream(bytes))
            using (var mso = new MemoryStream())
            {
                using (var gs = new GZipStream(mso, CompressionMode.Compress))
                {
                    //msi.CopyTo(gs);
                    CopyTo(msi, gs);
                }

                return mso.ToArray();
            }
        }
        private string Decompress(byte[] bytes)
        {
            using (var msi = new MemoryStream(bytes))
            using (var mso = new MemoryStream())
            {
                using (var gs = new GZipStream(msi, CompressionMode.Decompress))
                {
                    //gs.CopyTo(mso);
                    CopyTo(gs, mso);
                }

                return Encoding.UTF8.GetString(mso.ToArray());
            }
        }
    }
    public class TrackingMessageStatusCodes
    {
        public const string Started = "Started";
        public const string InProcess = "In process";
        public const string Complete = "Complete";
        public const string Aborted = "Aborted";
        public const string Failed = "Failed";
        public const string Disabled = "Disabled";
        public const string FollowingCorrelation = "FollowingCorrelation";
        public const string CreatingCorrelation = "CreatingCorrelation";
    }

    public class ContextVariable
    {
        private object _value;
        public const string STRINGTYPE = "String";
        public const string NUMBERTYPE = "Number";
        public const string DECIAMLTYPE = "Decimal";
        public const string DATETIMEYPE = "DateTime";
        public const string MESSAGETYPE = "Message";
        public const string OBJECTTYPE = "Object";

        public string Variable { get; set; }
        public object Value {
            get { return this._value; }
            set { this._value = value; }
        }
        public string Type { get; set; }
    }
}