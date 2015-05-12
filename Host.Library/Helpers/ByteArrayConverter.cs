using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Host.Library.Helpers
{
    public class ByteArrayConverter : JsonConverter
    {
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            if (value == null)
            {
                writer.WriteNull();
                return;
            }

            byte[] data = (byte[])value;
            MemoryStream m = new MemoryStream(data);
            writer.WriteValue(Convert.ToBase64String(m.ToArray()));
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            var m = new MemoryStream(Convert.FromBase64String((string)reader.Value));
            var json = Encoding.UTF8.GetString(m.GetBuffer());
            return json;
        }

        public override bool CanConvert(Type objectType)
        {
            var responce = objectType == typeof(byte[]);
            return responce;
        }
    }
}
