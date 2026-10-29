using System.Text.Json;
using System.Text.Json.Serialization;

namespace graduatio_project.Services
{
    //  نقبل المعرف كرقم أو كنص في طلبات الإدخال
    //  الواجهة ترسل المعرفات نصوصًا كما اتفقنا، لكننا لا نريد أن ينكسر الطلب
    //  إذا أرسلها أحد كأرقام — نكتبها دائمًا كأرقام في الاستجابة
    public sealed class FlexibleIntConverter : JsonConverter<int>
    {
        public override int Read(ref Utf8JsonReader reader, Type typeToConvert,
            JsonSerializerOptions options)
        {
            return reader.TokenType switch
            {
                JsonTokenType.Number => reader.GetInt32(),
                JsonTokenType.String => int.TryParse(reader.GetString(), out var value)
                    ? value
                    : throw new JsonException("Value must be a valid integer."),
                _ => throw new JsonException("Expected a number or string.")
            };
        }

        public override void Write(Utf8JsonWriter writer, int value,
            JsonSerializerOptions options)
        {
            writer.WriteNumberValue(value);
        }
    }

    //  نفس الفكرة للمعرفات القابلة للـ null مثل therapistId في بعض الطلبات
    public sealed class FlexibleNullableIntConverter : JsonConverter<int?>
    {
        public override int? Read(ref Utf8JsonReader reader, Type typeToConvert,
            JsonSerializerOptions options)
        {
            return reader.TokenType switch
            {
                JsonTokenType.Null => null,
                JsonTokenType.Number => reader.GetInt32(),
                JsonTokenType.String => string.IsNullOrEmpty(reader.GetString())
                    ? null
                    : int.TryParse(reader.GetString(), out var value) ? value : null,
                _ => throw new JsonException("Expected a number or string.")
            };
        }

        public override void Write(Utf8JsonWriter writer, int? value,
            JsonSerializerOptions options)
        {
            if (value.HasValue)
            {
                writer.WriteNumberValue(value.Value);
            }
            else
            {
                writer.WriteNullValue();
            }
        }
    }
}
