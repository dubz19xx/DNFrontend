using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Windows.Storage;

namespace Test1.Utilities
{
    public static class FileStorageService
    {
        private const string FilesKey = "StoredFiles";
        private static readonly StorageFolder localFolder = ApplicationData.Current.LocalFolder;

        public static async Task<List<FileItem>> LoadFilesAsync()
        {
            try
            {
                var file = await localFolder.TryGetItemAsync("files.json");
                if (file != null)
                {
                    var content = await FileIO.ReadTextAsync((StorageFile)file);
                    return JsonSerializer.Deserialize<List<FileItem>>(content);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading files: {ex.Message}");
            }
            return new List<FileItem>();
        }

        public static async Task SaveFileAsync(StorageFile newFile)
        {
            try
            {
                var files = await LoadFilesAsync();
                var properties = await newFile.GetBasicPropertiesAsync();

                files.Add(new FileItem
                {
                    Name = newFile.Name,
                    Size = $"{properties.Size / 1024:F2} KB",
                    Date = DateTime.Now.ToString("g"),
                    FilePath = newFile.Path
                });

                var content = JsonSerializer.Serialize(files);
                var file = await localFolder.CreateFileAsync("files.json", CreationCollisionOption.ReplaceExisting);
                await FileIO.WriteTextAsync(file, content);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving file: {ex.Message}");
                throw;
            }
        }
    }
}

public class FileItem
{
    public string Name { get; set; }
    public string Size { get; set; }
    public string Date { get; set; }
    public string FilePath { get; set; }
}
