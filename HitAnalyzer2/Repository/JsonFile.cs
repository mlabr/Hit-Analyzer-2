using System;
using System.IO;
using System.Text.Json;

namespace HitAnalyzer2.Repository
{
    internal static class JsonFile
    {
        internal static void Write<T>(string path, T data, bool overwrite, JsonSerializerOptions options)
        {
            var temporaryPath = WriteTemporaryFile(path, data, options);
            try
            {
                File.Move(temporaryPath, path, overwrite);
            }
            finally
            {
                File.Delete(temporaryPath);
            }
        }

        internal static string WriteTemporaryFile<T>(string path, T data, JsonSerializerOptions options)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
            try
            {
                using var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write);
                JsonSerializer.Serialize(stream, data, options);
                return temporaryPath;
            }
            catch
            {
                File.Delete(temporaryPath);
                throw;
            }
        }
    }
}
