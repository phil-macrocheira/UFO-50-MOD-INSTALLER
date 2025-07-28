﻿using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace UFO_50_MOD_INSTALLER
{
    public class ModInfo
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Creator { get; set; }
        public string Description { get; set; }
        public string ImageUrl { get; set; }
        public string PageUrl { get; set; }
        public long DateUpdated { get; set; }
        public long DateAdded { get; set; }
        public int Views { get; set; }
        public int Likes { get; set; }
    }

    public class LocalModInfo
    {
        public long DateUpdated { get; set; }
    }

    public class ModFile
    {
        public string FileName { get; set; }
        public string DownloadUrl { get; set; }
    }

    internal class ModDownloader
    {
        public async Task DownloadMods(string downloadPath, List<ModFile> filesToDownload, string localModInfoPath, Dictionary<string, ModInfo> fileToModInfoMap) {
            var localMods = new Dictionary<string, LocalModInfo>();
            if (File.Exists(localModInfoPath)) {
                var json = File.ReadAllText(localModInfoPath);
                localMods = JsonSerializer.Deserialize<Dictionary<string, LocalModInfo>>(json) ?? new Dictionary<string, LocalModInfo>();
            }

            try {
                foreach (var file in filesToDownload) {
                    Console.WriteLine($"Downloading {file.FileName}...");
                    await DownloadFile(file.DownloadUrl, Path.Combine(downloadPath, file.FileName));

                    // This will now work correctly
                    if (fileToModInfoMap.TryGetValue(file.FileName, out var modInfo)) {
                        // Save the server's "DateUpdated" timestamp locally
                        localMods[modInfo.Id] = new LocalModInfo { DateUpdated = modInfo.DateUpdated };
                    }
                }
            }
            finally {
                var updatedJson = JsonSerializer.Serialize(localMods, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(localModInfoPath, updatedJson);
            }
        }

        public static async Task<List<ModInfo>> GetModInfo(string game_id) {
            var mods = new List<ModInfo>();
            var page = 1;

            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", "UFO50ModInstaller/1.0");

            while (true) {
                var url = $"https://gamebanana.com/apiv11/Game/{game_id}/Subfeed?_nPage={page}&_sSort=default";

                HttpResponseMessage response;
                try {
                    response = await client.GetAsync(url);
                    response.EnsureSuccessStatusCode();
                }
                catch (HttpRequestException e) {
                    throw new Exception($"Failed to connect to Gamebanana. Status Code: {e.StatusCode}. Message: {e.Message}", e);
                }

                var json = await response.Content.ReadAsStringAsync();
                var doc = JsonDocument.Parse(json);
                var records = doc.RootElement.GetProperty("_aRecords");

                if (records.GetArrayLength() == 0) break;

                foreach (var element in records.EnumerateArray()) {
                    if (!element.TryGetProperty("_sModelName", out var modelNameElement) || modelNameElement.GetString() != "Mod") {
                        continue;
                    }

                    element.TryGetProperty("_idRow", out var idElement);
                    element.TryGetProperty("_sName", out var nameElement);
                    element.TryGetProperty("_sProfileUrl", out var pageUrlElement);
                    element.TryGetProperty("_sBody", out var descElement);

                    long dateUpdated = 0;
                    if (element.TryGetProperty("_tsDateUpdated", out var dateUpdatedElement)) {
                        dateUpdated = dateUpdatedElement.TryGetInt64(out var val) ? val : 0;
                    }

                    long dateAdded = 0;
                    if (element.TryGetProperty("_tsDateAdded", out var dateAddedElement)) {
                        dateAdded = dateAddedElement.TryGetInt64(out var val) ? val : 0;
                    }

                    int views = 0;
                    if (element.TryGetProperty("_nViewCount", out var viewsElement)) {
                        views = viewsElement.TryGetInt32(out var val) ? val : 0;
                    }

                    int likes = 0;
                    if (element.TryGetProperty("_nLikeCount", out var likesElement)) {
                        likes = likesElement.TryGetInt32(out var val) ? val : 0;
                    }

                    string creatorName = "N/A";
                    if (element.TryGetProperty("_aSubmitter", out var s) && s.TryGetProperty("_sName", out var n)) {
                        creatorName = n.GetString() ?? "N/A";
                    }

                    string imageUrl = "";
                    if (element.TryGetProperty("_aPreviewMedia", out var media) && media.TryGetProperty("_aImages", out var imgs) && imgs.GetArrayLength() > 0) {
                        if (imgs[0].TryGetProperty("_sBaseUrl", out var bu) && imgs[0].TryGetProperty("_sFile", out var f))
                            imageUrl = $"{bu.GetString()}/{f.GetString()}";
                    }

                    if (idElement.ValueKind != JsonValueKind.Undefined && nameElement.ValueKind != JsonValueKind.Undefined) {
                        mods.Add(new ModInfo
                        {
                            Id = idElement.ToString(),
                            Name = nameElement.GetString() ?? "Unnamed Mod",
                            PageUrl = pageUrlElement.ValueKind != JsonValueKind.Undefined ? pageUrlElement.GetString() : "",
                            Creator = creatorName,
                            Description = descElement.ValueKind != JsonValueKind.Undefined ? descElement.GetString() : "",
                            ImageUrl = imageUrl,
                            DateUpdated = dateUpdated,
                            DateAdded = dateAdded,
                            Views = views,
                            Likes = likes
                        });
                    }
                }
                page++;
            }
            return mods;
        }
        public static async Task<string> GetModFullDescription(string modId) {
            if (string.IsNullOrEmpty(modId)) return "No description available.";

            try {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.Add("User-Agent", "UFO50ModInstaller/1.0");

                // Use the correct endpoint you discovered: /ProfilePage
                var url = $"https://gamebanana.com/apiv11/Mod/{modId}/ProfilePage";

                var response = await client.GetAsync(url);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync();
                var doc = JsonDocument.Parse(json);

                // The full body text is in the "_sText" field of this endpoint's response.
                if (doc.RootElement.TryGetProperty("_sText", out var textElement)) {
                    string rawHtml = "";
                    if (textElement.ValueKind == JsonValueKind.Array && textElement.GetArrayLength() > 0) {
                        rawHtml = textElement[0].GetString() ?? "";
                    }
                    else {
                        rawHtml = textElement.GetString() ?? "";
                    }

                    // 1. Decode HTML entities like &nbsp;
                    string decodedText = WebUtility.HtmlDecode(rawHtml);

                    // 2. Replace <br> with newline
                    string plainText = Regex.Replace(decodedText, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);

                    // 3. Use a regular expression to strip out all remaining HTML tags
                    plainText = Regex.Replace(plainText, "<.*?>", String.Empty);

                    return plainText.Trim();
                }
            }
            catch (Exception ex) {
                return $"Could not load full description. Error: {ex.Message}";
            }

            return "Description not found.";
        }


        public static async Task<List<ModFile>> GetModFileInfo(string modId) {
            using var client = new HttpClient();
            var files = new List<ModFile>();
            string url = $"https://gamebanana.com/apiv11/Mod/{modId}/DownloadPage";
            var response = await client.GetAsync(url);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);
            foreach (var fileElement in doc.RootElement.GetProperty("_aFiles").EnumerateArray()) {
                files.Add(new ModFile
                {
                    FileName = fileElement.GetProperty("_sFile").GetString() ?? "unknown.zip",
                    DownloadUrl = fileElement.GetProperty("_sDownloadUrl").GetString() ?? ""
                });
            }
            return files;
        }

        private static async Task DownloadFile(string downloadUrl, string filePath) {
            using var client = new HttpClient();
            var response = await client.GetAsync(downloadUrl);
            response.EnsureSuccessStatusCode();
            await File.WriteAllBytesAsync(filePath, await response.Content.ReadAsByteArrayAsync());
        }
    }
}