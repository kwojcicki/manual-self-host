using System.ComponentModel;
using System.Globalization;

namespace httpclientestdouble.lib
{
    public interface IObjectDeserializer
    {
        object? ConvertValue(string value, Type outType);
    }

    public class ObjectDeserializer : IObjectDeserializer
    {
        public object? ConvertValue(string value, Type outType)
        {
            if (value == null)
            {
                return null;
            }
            else if (outType.IsEnum && Enum.TryParse(outType, value, out object? result))
            {
                return result;
            }
            else if (outType != typeof(string) && outType.IsClass)
            {
                return Newtonsoft.Json.JsonConvert.DeserializeObject(value, outType);
            }

            TypeConverter obj = TypeDescriptor.GetConverter(outType);
            object? outValue = obj.ConvertFromString(null, CultureInfo.InvariantCulture, value);
            return outValue;
        }
    }
}
