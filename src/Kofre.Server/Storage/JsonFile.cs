using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kofre.Server.Storage;

/// <summary>Reads and writes the server's JSON files, readable and editable by the admin.</summary>
internal static class JsonFile
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>The file's value; <paramref name="missing"/> when there is no file.</summary>
    public static T Read<T>(string path, T missing)
    {
        if (!File.Exists(path))
        {
            return missing;
        }

        using var input = File.OpenRead(path);
        return JsonSerializer.Deserialize<T>(input, Options) ?? missing;
    }

    /// <summary>Writes under a temporary name, then renames: a crash leaves the old file or the new one.</summary>
    public static void Write<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        using (var output = File.Create(temporary))
        {
            JsonSerializer.Serialize(output, value, Options);
        }

        File.Move(temporary, path, overwrite: true);
    }
}
