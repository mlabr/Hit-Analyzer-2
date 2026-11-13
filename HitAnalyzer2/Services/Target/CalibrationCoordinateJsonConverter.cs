using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HitAnalyzer2.Services.Target
{
    internal sealed class CalibrationCoordinateJsonConverter : JsonConverter<double>
    {
        public override double Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => reader.GetDouble();

        public override void Write(Utf8JsonWriter writer, double value, JsonSerializerOptions options)
            => writer.WriteRawValue(value.ToString("F4", CultureInfo.InvariantCulture));
    }
}
