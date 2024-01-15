using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OoLunar.ConvenientCompany.Tools.SemVerParser.Entities
{
    public sealed class LocalModJsonConverter : JsonConverter<LocalMod>
    {
        public override LocalMod? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            string? value = reader.GetString();
            if (value is null)
            {
                return null;
            }

            string[] parts = value.Split('-');
            if (parts.Length != 3 || !Version.TryParse(parts[2], out Version? version))
            {
                return null;
            }

            return new LocalMod
            {
                Author = parts[0],
                ModName = parts[1],
                Version = version
            };
        }

        public override void Write(Utf8JsonWriter writer, LocalMod value, JsonSerializerOptions options) => writer.WriteStringValue($"{value.Author}-{value.ModName}-{value.LatestVersion ?? value.Version}");
    }
}